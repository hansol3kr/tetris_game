using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blockfall.Core;
using Blockfall.Platform;
using Blockfall.Theme;
using Blockfall.UI;

namespace Blockfall.Dev;

/// <summary>
/// Headless auto-play smoke test. Boots the REAL game and drives it through every
/// screen and a full solo run, asserting (1) no screen collapses to 0×0 — the
/// layout regression that shipped twice — and (2) nothing throws. It exists
/// because the presentation layer has no other automated coverage: the core rules
/// are unit-tested, but screens/controllers/HUD had only ever been *built*
/// headlessly, never *run*.
///
/// Strictly opt-in: Bootstrap only attaches it when <c>--autoplay</c> (or
/// <c>BLOCKFALL_AUTOPLAY=1</c>) is passed, so it can never run in a shipped build.
/// Exits the process with code 0 (all green) or 1 (any failure) so CI/scripts can
/// gate on it. Run via: <c>godot --headless --path game -- --autoplay</c>.
/// </summary>
public partial class AutoPlay : Node
{
    /// <summary>Startup opt-in — checked by Bootstrap.</summary>
    public static bool Requested
    {
        get
        {
            foreach (var a in OS.GetCmdlineArgs())
                if (a == "--autoplay") return true;
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a == "--autoplay") return true;
            return OS.GetEnvironment("BLOCKFALL_AUTOPLAY") == "1";
        }
    }

    private readonly List<string> _fails = new();
    private int _passes;

    public override void _Ready() => _ = Run();

    private void Ok(string msg) { _passes++; GD.Print($"[autoplay] ✓ {msg}"); }
    private void Fail(string msg) { _fails.Add(msg); GD.PrintErr($"[autoplay] ✗ {msg}"); }

    private async Task Wait(double sec)
        => await ToSignal(GetTree().CreateTimer(sec), SceneTreeTimer.SignalName.Timeout);

    private SceneRouter R => Bootstrap.Instance.Router;

    /// <summary>The screen currently mounted under the viewport host.</summary>
    private Node? Current()
    {
        var host = Bootstrap.Instance.ScreenHost;
        int n = host.GetChildCount();
        return n > 0 ? host.GetChild(n - 1) : null;
    }

    /// <summary>Assert the current screen filled the viewport (Control) or at least exists (Node2D).</summary>
    private void CheckLayout(string label, Type? expected = null)
    {
        var s = Current();
        if (s is null || !GodotObject.IsInstanceValid(s)) { Fail($"{label}: 화면이 없음"); return; }
        if (expected is not null && !expected.IsInstanceOfType(s))
        {
            Fail($"{label}: 예상 {expected.Name} 이지만 실제 {s.GetType().Name}");
            return;
        }
        var vp = Bootstrap.Instance.GetViewport().GetVisibleRect().Size;
        // Control screens: check the root. Node2D controllers: check their largest
        // Control descendant — that's the viewport-sized UI host (HUD/overlay parent)
        // whose 0×0 collapse was the shipped bug. Either way, it must fill the viewport.
        Vector2 size = s is Control c ? c.Size : LargestControl(s);
        string kind = s is Control ? "" : $" ({s.GetType().Name} UI 호스트)";
        if (size.X < vp.X * 0.9f || size.Y < vp.Y * 0.9f)
            Fail($"{label}: 레이아웃 붕괴 size={size} vs viewport={vp} (0×0 회귀 의심){kind}");
        else
            Ok($"{label}: size={size}{kind}");
    }

    /// <summary>Largest Control anywhere under <paramref name="n"/> — the viewport UI host.</summary>
    private static Vector2 LargestControl(Node n)
    {
        Vector2 best = Vector2.Zero;
        foreach (var child in n.GetChildren())
        {
            if (child is Control cc && cc.Size.X * cc.Size.Y > best.X * best.Y) best = cc.Size;
            var deeper = LargestControl(child);
            if (deeper.X * deeper.Y > best.X * best.Y) best = deeper;
        }
        return best;
    }

    /// <summary>Wait until the router is idle (no crossfade in flight) so a nav isn't a no-op.</summary>
    private async Task WaitIdle()
    {
        int g = 0;
        while (R.Busy && g++ < 60) await Wait(0.05); // ≤3s guard
    }

    /// <summary>Fire a navigation, let the crossfade settle, then check the result.</summary>
    private async Task Nav(string label, Action go, Type? expected = null)
    {
        await WaitIdle();   // a nav fired mid-transition is swallowed by the Busy guard
        try { go(); }
        catch (Exception e) { Fail($"{label}: 내비게이션 예외 {e.GetType().Name}: {e.Message}"); return; }
        await Wait(0.4);
        await WaitIdle();
        CheckLayout(label, expected);
    }

    private async Task Run()
    {
        GD.Print("[autoplay] ===== 시작 =====");
        await Wait(1.0); // 부팅/첫 화면 정착 대기

        // 시작 화면(튜토리얼일 수도)에서 메뉴로 강제 이동 후 정착 확인
        await Nav("MainMenu", () => R.GoToMainMenu(), typeof(MainMenu));

        // ── 1) 모든 메뉴 화면 레이아웃 스윕 (0×0 회귀 감시) ──────────
        await Nav("Settings", () => R.GoToSettings(), typeof(SettingsScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
        await Nav("Store", () => R.GoToStore(), typeof(StoreScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
        await Nav("Profile", () => R.GoToProfile(), typeof(ProfileScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
        await Nav("Replays", () => R.GoToReplays(), typeof(ReplaysScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
        await Nav("VersusSelect", () => R.GoToVersusSelect(), typeof(VersusSelectScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
        await Nav("Tutorial", () => R.GoToTutorial());   // Node2D
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));

        // ── 2) 솔로 한 판 완주: Marathon → 게임오버 → Results ────────
        await PlayToGameOver();

        // ── 3) 각 모드 컨트롤러 생성/첫 프레임 스모크 ───────────────
        foreach (var mode in new[] { GameModeId.Sprint, GameModeId.Ultra, GameModeId.Zen,
                                     GameModeId.Dig, GameModeId.Survival, GameModeId.Master })
        {
            await Nav($"Game({mode})", () => R.StartGame(mode));
            await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
        }
        await Nav("Daily", () => R.StartDaily());
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));

        // Versus vs CPU (두 보드 + 봇)
        await Nav("Versus(CPU)", () => R.StartVersus(BotDifficulty.Easy));
        await Wait(0.6); // 봇이 몇 수 두게
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));

        // 자유배치(Block Fit) 모드 — Node2D, 새 모드가 로드·레이아웃 안 깨지는지
        await Nav("BlockFit", () => R.StartBlockFit());
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));

        // 디센트: 지층 러너 + 드래프트/결과 화면 (신규 Control — 0×0 회귀 감시)
        await CheckDescent();

        // ── 4) 일시정지 오버레이 레이아웃 (Node2D 밑 스크림 — 0×0 회귀 이력) ──
        await CheckPauseOverlay();

        // ── 5) 스킨(테마) 장착 라이브 적용 ──────────────────────────
        CheckSkins();

        // ── 리포트 ─────────────────────────────────────────────────
        GD.Print("[autoplay] ===== 리포트 =====");
        GD.Print($"[autoplay] 통과 {_passes} · 실패 {_fails.Count}");
        foreach (var f in _fails) GD.PrintErr($"[autoplay] FAIL: {f}");
        GD.Print(_fails.Count == 0 ? "[autoplay] RESULT=PASS" : "[autoplay] RESULT=FAIL");
        GetTree().Quit(_fails.Count == 0 ? 0 : 1);
    }

    private async Task PlayToGameOver()
    {
        try { R.StartGame(GameModeId.Marathon); }
        catch (Exception e) { Fail($"Marathon 시작 예외 {e.GetType().Name}: {e.Message}"); return; }
        await Wait(0.6);
        CheckLayout("Game(Marathon)", typeof(Blockfall.Gameplay.GameController));

        // soft drop을 눌러 두고 hard drop을 펄스로 주입해 빠르게 top-out.
        Input.ActionPress("soft_drop");
        int guard = 0;
        while (Current() is not ResultsScreen && guard < 400)
        {
            Input.ParseInputEvent(new InputEventAction { Action = "hard_drop", Pressed = true });
            await Wait(0.06);
            Input.ParseInputEvent(new InputEventAction { Action = "hard_drop", Pressed = false });
            guard++;
        }
        Input.ActionRelease("soft_drop");

        if (Current() is ResultsScreen)
        {
            Ok($"Marathon→Results 전이 (약 {guard} 조각 후)");
            CheckLayout("Results", typeof(ResultsScreen));
        }
        else
        {
            Fail("Marathon: 게임오버→Results 전이 실패 (타임아웃 — 입력/락/탑아웃 경로 점검)");
        }
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
    }

    /// <summary>Descent smoke: the stage controller loads, and the two NEW Control
    /// screens (charm draft, run results) fill the viewport — the 0×0 bug class.</summary>
    private async Task CheckDescent()
    {
        // Stage 1 (Dig stratum) controller boots and lays out.
        await Nav("Descent(S1)", () => R.StartDescent(seed: 12345UL));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));

        // Charm draft: enter with a state whose first stratum is already cleared.
        var drafted = new Blockfall.Gameplay.DescentRunState(12345UL);
        drafted.RecordStage(new Blockfall.Gameplay.RunResults
        {
            Mode = GameModeId.Descent, Completed = true, Score = 1000,
            Stats = new RunStats(),
            Modifiers = Array.Empty<GameModifier>(),
            UnlockedAchievements = Array.Empty<string>(),
        });
        await Nav("CharmDraft", () => R.GoToCharmDraft(drafted), typeof(CharmDraftScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));

        // Run results: layout only — record:false keeps this fabricated run out of
        // the real save's career stats, achievements, and platform submits.
        var failed = new Blockfall.Gameplay.DescentRunState(54321UL);
        failed.RecordStage(new Blockfall.Gameplay.RunResults
        {
            Mode = GameModeId.Descent, Completed = false, Score = 0,
            Stats = new RunStats(),
            Modifiers = Array.Empty<GameModifier>(),
            UnlockedAchievements = Array.Empty<string>(),
        });
        await Nav("DescentResults", () => R.GoToDescentResults(failed, record: false), typeof(DescentResultsScreen));
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
    }

    /// <summary>Pause a live game and assert the scrim actually covers the viewport (it hangs
    /// off a Node2D controller — the exact place the 0×0 layout bug bit before).</summary>
    private async Task CheckPauseOverlay()
    {
        try { R.StartGame(GameModeId.Zen); } // endless — won't top out mid-check
        catch (Exception e) { Fail($"Zen 시작 예외 {e.GetType().Name}: {e.Message}"); return; }
        await Wait(0.6);
        var gc = Current();

        Pulse("pause_game"); await Wait(0.45);
        var vp = Bootstrap.Instance.GetViewport().GetVisibleRect().Size;
        var scrim = gc is null ? Vector2.Zero : LargestColorRect(gc);
        if (scrim.X >= vp.X * 0.9f && scrim.Y >= vp.Y * 0.9f)
            Ok($"일시정지 오버레이 size={scrim}");
        else
            Fail($"일시정지 오버레이 붕괴/미표시 size={scrim} vs viewport={vp} (Node2D 밑 스크림 0×0)");

        Pulse("pause_game"); await Wait(0.2); // resume
        await Nav("→Menu", () => R.GoToMainMenu(), typeof(MainMenu));
    }

    /// <summary>Fire an input action as a single press+release edge (routes through _UnhandledInput).</summary>
    private static void Pulse(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    /// <summary>Largest visible ColorRect anywhere under <paramref name="n"/> (finds the scrim).</summary>
    private static Vector2 LargestColorRect(Node n)
    {
        Vector2 best = Vector2.Zero;
        foreach (var child in n.GetChildren())
        {
            if (child is ColorRect { Visible: true } cr && cr.Size.X * cr.Size.Y > best.X * best.Y)
                best = cr.Size;
            var deeper = LargestColorRect(child);
            if (deeper.X * deeper.Y > best.X * best.Y) best = deeper;
        }
        return best;
    }

    private void CheckSkins()
    {
        var save = Bootstrap.Instance.Save;
        foreach (var item in StoreCatalog.Items)
        {
            if (item.Kind != StoreItemKind.Theme || !save.OwnsItem(item.Id)) continue;
            try
            {
                save.EquipTheme(item.Id);
                Palette.ApplyTheme(item.Theme);
                Bootstrap.Instance.Bg.ApplyThemeColors();
                Ok($"스킨 장착: {item.Id}");
            }
            catch (Exception e)
            {
                Fail($"스킨 {item.Id} 장착 예외 {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
