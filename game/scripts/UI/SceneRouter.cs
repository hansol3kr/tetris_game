using Godot;
using Blockfall.Core;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// Owns the current screen and swaps between menu, gameplay, and results with a
/// crossfade. Transition tweens are created on THIS node (never on the outgoing
/// screen — a tween dies silently with its owner, which would deadlock the
/// router), the old screen is freed from the tween callback, and a re-entrancy
/// guard plus a full-screen input blocker prevent double-navigation during the
/// fade. Retry paths ("play again" / rematch) use a fast swap so the hottest
/// loop in the game never waits on menu-grade choreography.
/// </summary>
public partial class SceneRouter : Node
{
    private Node? _current;
    private bool _busy;
    private Control _blocker = null!;

    public override void _Ready()
    {
        // Input blocker on a top layer: swallows clicks/taps mid-transition.
        var layer = new CanvasLayer { Layer = 99 };
        _blocker = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_blocker);
        AddChild(layer);
    }

    /// <summary>
    /// True while a crossfade is in flight. Public nav methods bail out on this
    /// BEFORE running their side effects (background dim, settings writes,
    /// controller construction) — a double-tap must be a no-op, not a half-nav.
    /// Public so a test harness (Dev.AutoPlay) can wait for a transition to settle
    /// before driving the next navigation; production code never reads it.
    /// </summary>
    public bool Busy => _busy;

    private void Swap(Node next, bool fast = false)
    {
        if (_busy) { next.QueueFree(); return; } // backstop; nav methods guard earlier
        _busy = true;
        _blocker.Visible = true;

        var old = _current;
        _current = next;

        float outDur = fast ? 0.05f : 0.14f;
        float inDur = fast ? 0.10f : 0.20f;

        var tw = CreateTween(); // bound to the router — survives screen frees
        if (old is not null)
        {
            old.ProcessMode = ProcessModeEnum.Disabled; // no input into the dying screen
            if (old is CanvasItem oldCi && !Motion.Reduced)
                tw.TweenProperty(oldCi, "modulate:a", 0f, outDur);
            tw.TweenCallback(Callable.From(old.QueueFree));
        }
        tw.TweenCallback(Callable.From(() =>
        {
            Bootstrap.Instance.ScreenHost.AddChild(next);
            if (next is CanvasItem ci)
            {
                ci.Modulate = new Color(1, 1, 1, 0);
                var tin = CreateTween();
                tin.TweenProperty(ci, "modulate:a", 1f, inDur);
                tin.TweenCallback(Callable.From(EndTransition));
            }
            else
            {
                EndTransition();
            }
        }));
    }

    private void EndTransition()
    {
        _busy = false;
        _blocker.Visible = false;
    }

    /// <summary>App entry: always land on the menu. The tutorial is never
    /// force-launched on first run — it's offered from the menu (HOW TO PLAY)
    /// and from Settings (REPLAY TUTORIAL), so the player is in control.</summary>
    public void GoToStart() => GoToMainMenu();

    public void GoToMainMenu()
    {
        if (Busy) return;
        Bootstrap.Instance.Bg.SetGameplayDim(false);
        var menu = new MainMenu();
        menu.ModeChosen += mode => StartGame(mode, mods: menu.SelectedModifiers());
        menu.SeedEntered += seed => StartGame(GameModeId.Marathon, seed, menu.SelectedModifiers());
        menu.DailyChosen += () => StartDaily();
        menu.SettingsChosen += GoToSettings;
        menu.StoreChosen += GoToStore;
        menu.VersusChosen += GoToVersusSelect;
        menu.TutorialChosen += GoToTutorial;
        menu.ReplaysChosen += GoToReplays;
        menu.ProfileChosen += GoToProfile;
        Swap(menu);
    }

    /// <summary>Progression hub: stats, achievements, and per-mode leaderboards.</summary>
    public void GoToProfile()
    {
        if (Busy) return;
        var screen = new ProfileScreen();
        screen.WatchReplay += data => WatchReplay(data, GoToProfile);
        screen.BackRequested += GoToMainMenu;
        Swap(screen);
    }

    /// <summary>Browse saved replays; watching one returns to the list.</summary>
    public void GoToReplays()
    {
        if (Busy) return;
        var screen = new ReplaysScreen();
        screen.WatchRequested += data => WatchReplay(data, GoToReplays);
        screen.BackRequested += GoToMainMenu;
        Swap(screen);
    }

    /// <summary>Interactive how-to-play onboarding; returns to the menu when done or skipped.</summary>
    public void GoToTutorial()
    {
        if (Busy) return;
        var tutorial = new Blockfall.Gameplay.TutorialController();
        tutorial.Finished += GoToMainMenu;
        Swap(tutorial);
    }

    public void GoToStore()
    {
        if (Busy) return;
        var screen = new StoreScreen();
        screen.BackRequested += GoToMainMenu;
        Swap(screen);
    }

    /// <summary>Pick the CPU difficulty (or online) before a versus match.</summary>
    public void GoToVersusSelect()
    {
        if (Busy) return;
        var screen = new VersusSelectScreen();
        screen.DifficultyChosen += d => StartVersus(d);
        screen.OnlineChosen += GoToNetLobby;
        screen.BackRequested += GoToMainMenu;
        Swap(screen);
    }

    /// <summary>Host/join screen for the online duel.</summary>
    public void GoToNetLobby()
    {
        if (Busy) return;
        var screen = new Blockfall.Net.NetLobbyScreen();
        screen.BackRequested += GoToMainMenu;
        screen.MatchReady += (peer, isHost, seed, rival, ranked) => StartNetVersus(peer, isHost, seed, rival, ranked);
        Swap(screen);
    }

    /// <summary>Start (or rematch) an online duel on an already-connected peer.
    /// Ranked (Quick Match) duels move the competitive ladder; direct-connect don't.</summary>
    public void StartNetVersus(Blockfall.Net.INetChannel peer, bool isHost, ulong seed, string rival, bool ranked, bool fast = false)
    {
        // No Busy guard bail here would leak the live connection — instead close
        // it if we truly cannot swap (double-fire is already prevented upstream).
        if (Busy) { peer.Close(); return; }
        Bootstrap.Instance.Bg.SetGameplayDim(true);
        var controller = new Blockfall.Net.NetVersusController(peer, isHost, seed, rival, ranked);
        controller.RematchStarted += newSeed => StartNetVersus(peer, isHost, newSeed, rival, ranked, fast: true);
        controller.QuitRequested += GoToMainMenu;
        Swap(controller, fast);
    }

    public void StartVersus(BotDifficulty difficulty, bool fast = false)
    {
        if (Busy) return;
        Bootstrap.Instance.Bg.SetGameplayDim(true);
        var controller = new VersusController(difficulty, GenerateSeed());
        controller.RematchRequested += () => StartVersus(difficulty, fast: true);
        controller.QuitRequested += GoToMainMenu;
        Swap(controller, fast);
    }

    public void GoToSettings()
    {
        if (Busy) return;
        var screen = new SettingsScreen();
        screen.BackRequested += GoToMainMenu;
        Swap(screen);
    }

    public void StartGame(GameModeId mode, ulong? seed = null, GameModifier[]? mods = null, bool fast = false)
    {
        if (Busy) return;
        Bootstrap.Instance.Bg.SetGameplayDim(true);
        RememberMode(mode);
        ulong s = seed ?? GenerateSeed();

        // Ghost race: on a clean run, race the pace of your best saved run for this mode.
        ReplayData? ghost = null;
        if (mods is null || mods.Length == 0)
        {
            foreach (var e in Bootstrap.Instance.Save.GetLeaderboard(mode)) // best-first
            {
                if (string.IsNullOrEmpty(e.ReplayPath)) continue;
                ghost = Blockfall.Platform.ReplayStore.Load(e.ReplayPath);
                if (ghost is not null) break;
            }
        }

        var controller = new GameController(mode, s, modifiers: mods, ghost: ghost);
        controller.RunFinished += results => GoToResults(mode, results);
        controller.QuitRequested += GoToMainMenu;
        Swap(controller, fast);
    }

    /// <summary>Start today's daily challenge: a fixed seed shared by all players.</summary>
    public void StartDaily(bool fast = false)
    {
        if (Busy) return;
        Bootstrap.Instance.Bg.SetGameplayDim(true);
        var (key, seed) = DailyChallenge.Today();
        var controller = new GameController(GameModeId.Daily, seed, dailyKey: key);
        controller.RunFinished += results => GoToResults(GameModeId.Daily, results);
        controller.QuitRequested += GoToMainMenu;
        Swap(controller, fast);
    }

    public void GoToResults(GameModeId mode, RunResults results)
    {
        if (Busy) return;
        Bootstrap.Instance.Bg.SetGameplayDim(false);
        var screen = new ResultsScreen(mode, results);
        // Retries take the fast path: ~150ms to a controllable piece, no stagger.
        if (mode == GameModeId.Daily) screen.PlayAgain += () => StartDaily(fast: true);
        else screen.PlayAgain += () => StartGame(mode, mods: results.Modifiers, fast: true); // fresh seed, same modifiers
        screen.ReplaySeed += () => StartGame(mode, results.Seed, results.Modifiers, fast: true); // exact rerun
        if (results.Replay is not null)
            screen.WatchReplay += () => GoToReplay(results.Replay, mode, results);
        screen.BackToMenu += GoToMainMenu;
        Swap(screen);
    }

    /// <summary>Watch the deterministic playback of a finished run, then return to its results.</summary>
    public void GoToReplay(ReplayData data, GameModeId mode, RunResults results)
        => WatchReplay(data, () => GoToResults(mode, results));

    /// <summary>Play back a replay, returning to wherever the caller came from.</summary>
    private void WatchReplay(ReplayData data, System.Action onBack)
    {
        if (Busy) return;
        var viewer = new Blockfall.Gameplay.ReplayViewer(data);
        viewer.BackRequested += onBack;
        Swap(viewer);
    }

    /// <summary>Track the hero-card mode (skips Daily — it has its own card).</summary>
    private static void RememberMode(GameModeId mode)
    {
        if (mode == GameModeId.Daily) return;
        var save = Bootstrap.Instance.Save;
        if (save.Settings.LastPlayedMode == mode.ToString()) return;
        save.Settings.LastPlayedMode = mode.ToString();
        save.SetSettings(save.Settings);
    }

    private static ulong GenerateSeed()
    {
        // Non-deterministic per run. Daily-challenge mode would pass a fixed seed instead.
        return (ulong)(Time.GetTicksUsec()) ^ (ulong)GD.Randi() << 32;
    }
}
