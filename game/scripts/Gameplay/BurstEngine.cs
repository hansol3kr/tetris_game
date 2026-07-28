using Godot;
using System;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>The shape of one burst particle. One struct drives every kind so the
/// update loop stays branch-light and allocation-free.</summary>
public enum FxKind : byte { Dot, Star, Streak, Petal, Shard, Ember, Bubble }

/// <summary>
/// The single shared line-clear particle engine — the anti-drift seam that both the
/// Block Fit board and the store's ArtifactPreview draw through, so the burst you buy
/// is byte-for-byte the burst you play. Owns the particle pools and the per-artifact
/// recipe switch; the caller owns the two canvas surfaces (a normal-alpha body pass and
/// a BlendMode.Add child) and forwards <see cref="DrawNormal"/>/<see cref="DrawAdditive"/>.
///
/// Pure juice: view-only, its own <see cref="RandomNumberGenerator"/>, never read by
/// core/scoring/replays — cosmetics can't touch determinism or fairness. Frame-rate
/// independent (velocity damping is <c>Vel*=Exp(-Drag*dt)</c>), and hard-capped so a big
/// combo can't flood the pool.
/// </summary>
public sealed class BurstEngine
{
    private struct FxSpark
    {
        public Vector2 Pos, Vel;
        public float Age, Life, Size, Grav, Rot, Spin, Drag, Flutter, Seed, Trail;
        public FxKind Kind;
        public bool Additive;
        public Color Col;
    }

    private struct FxRing { public Vector2 Pos; public float Age, Life, MaxR, Width; public Color Col; public bool Bloom, Rainbow; }
    private struct FxRibbon { public Vector2 Base; public float Age, Life, Seed, Hue, Cell; }
    private struct FxBolt { public Vector2 A, B; public float Age, Life, Seed; }

    /// <summary>An expanding displacement wave the BOARD samples — it moves cells that are
    /// already being drawn, so the whole grid ripples for zero extra draw calls and zero
    /// extra textures. Gated off entirely under reduced motion.</summary>
    private struct FxShock { public Vector2 C; public float Age, Life, Amp, Sigma; }

    private const int MaxFx = 260;
    private const int MaxShocks = 4;
    private const float FlutterFreq = 9f;
    private const float ShockSpeed = 520f;     // px/s ring expansion
    private const float ShockLife = 0.34f;
    /// <summary>Minimum gap between full-screen bleaches — a photosensitivity floor. Without
    /// it a fast multi-line combo could strobe the whole screen at frame rate.</summary>
    private const float FlashCooldown = 0.40f;
    /// <summary>Hard ceiling on any single frame's full-screen additive alpha. hdr_2d is baked
    /// OFF, so an unclamped bleach saturates SDR to a flat white rectangle and the artifact
    /// loses its colour identity.</summary>
    private const float MaxScreenAdditive = 1.6f;
    private const int BoltSegs = 8;            // hard cap — sizes the shared scratch buffer

    private readonly List<FxSpark> _fx = new();
    private readonly List<FxRing> _rings = new();
    private readonly List<FxRibbon> _ribbons = new();
    private readonly List<FxBolt> _bolts = new();
    private readonly FxShock[] _shocks = new FxShock[MaxShocks];
    private int _shockCount;
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>The rigid-chunk destruction pass. Pumped by this engine's Update/Draw, so a
    /// host gets it just by calling <see cref="SetDebrisBounds"/> once.</summary>
    public DebrisField Debris { get; } = new();

    /// <summary>Opt debris out for hosts with no room for it (tiny preview cards).</summary>
    public bool DebrisEnabled { get; set; } = true;

    // Screen-space envelope (Supernova bleach + vignette; Lightning short flash).
    private float _novaAge = 9f, _novaPeak;
    private bool _novaVignette;
    private float _sinceFlash = 9f;

    // Immediate-mode draw marshals these synchronously, so one shared scratch per shape is
    // safe and removes ~260 array allocations per peak frame (pure GC-pressure win).
    private static readonly Vector2[] S3 = new Vector2[3];
    private static readonly Vector2[] S4 = new Vector2[4];
    private static readonly Vector2[] S8 = new Vector2[8];
    private static readonly Vector2[] S12 = new Vector2[12]; private static readonly Color[] S12C = new Color[12];
    private static readonly Vector2[] S26 = new Vector2[26]; private static readonly Color[] S26C = new Color[26];
    private static readonly Vector2[] SBolt = new Vector2[BoltSegs + 1];   // bolt polyline (segs hard-capped)

    private static readonly Color[] Party =
    {
        new(1f, 0.35f, 0.45f), new(1f, 0.8f, 0.3f), new(0.4f, 0.95f, 0.6f), new(0.35f, 0.8f, 1f),
        new(0.75f, 0.5f, 1f), new(1f, 0.55f, 0.85f), new(0.5f, 1f, 0.9f),
    };

    public bool Active => _fx.Count > 0 || _rings.Count > 0 || _ribbons.Count > 0 || _bolts.Count > 0
                          || _shockCount > 0 || Debris.Active || _novaAge < 0.6f;

    public void Clear()
    {
        _fx.Clear(); _rings.Clear(); _ribbons.Clear(); _bolts.Clear();
        _shockCount = 0;
        Debris.Clear();
        _novaAge = 9f; _novaPeak = 0f;
    }

    /// <summary>Arm the debris pass: the wall box and the shelf chunks come to rest on. Pass a
    /// floor ABOVE the tray/exit strip so settled debris can never sit on a drag target.</summary>
    public void SetDebrisBounds(Rect2 walls, float floorY) => Debris.SetBounds(walls, floorY);

    // ---- Integration --------------------------------------------------------

    public void Update(float dt)
    {
        if (_novaAge < 2f) _novaAge += dt;
        if (_sinceFlash < 4f) _sinceFlash += dt;
        Debris.Integrate(dt);
        for (int i = _shockCount - 1; i >= 0; i--)
        {
            _shocks[i].Age += dt;
            if (_shocks[i].Age >= _shocks[i].Life) _shocks[i] = _shocks[--_shockCount];
        }

        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var s = _fx[i];
            s.Age += dt;
            if (s.Age < 0f) { _fx[i] = s; continue; }            // delayed stage (aerial shells)
            s.Vel.Y += s.Grav * dt;
            s.Vel *= Mathf.Exp(-s.Drag * dt);                     // frame-rate-independent damping
            s.Pos += s.Vel * dt;
            if (s.Flutter != 0f) s.Pos.X += s.Flutter * Mathf.Sin(s.Seed + s.Age * FlutterFreq) * dt;
            s.Rot += s.Spin * dt;
            if (s.Age >= s.Life) _fx.RemoveAt(i); else _fx[i] = s;
        }
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var r = _rings[i]; r.Age += dt;
            if (r.Age >= r.Life) _rings.RemoveAt(i); else _rings[i] = r;
        }
        for (int i = _ribbons.Count - 1; i >= 0; i--)
        {
            var r = _ribbons[i]; r.Age += dt;
            if (r.Age >= r.Life) _ribbons.RemoveAt(i); else _ribbons[i] = r;
        }
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i]; b.Age += dt;
            if (b.Age >= b.Life) _bolts.RemoveAt(i); else _bolts[i] = b;
        }
    }

    // ---- Emit ---------------------------------------------------------------

    /// <summary>Spawn one cleared line's celebration. <paramref name="preClear"/> yields the
    /// colour of cell k along the line (Shards fly in the popped blocks' real hues).
    /// <paramref name="budget"/> scales particle counts down on big combos.</summary>
    public void EmitLine(BurstArtifact art, bool rowLine, int index, Vector2 origin, float cell, int n,
                         float budget, Func<int, Color> preClear)
    {
        Vector2 lineCenter = rowLine
            ? origin + new Vector2(n * 0.5f * cell, (index + 0.5f) * cell)
            : origin + new Vector2((index + 0.5f) * cell, n * 0.5f * cell);
        Color Gold = new(1f, 0.95f, 0.6f);

        // Physical destruction first: the cleared cells crack and break apart underneath the
        // artifact's celebration. Inert until a host arms SetDebrisBounds, and refused outright
        // under reduced motion (single gate inside DebrisField).
        if (DebrisEnabled)
            Debris.EmitLine(rowLine, index, origin, cell, n, budget, preClear, Palette.EquippedMaterial);

        // The board's own recoil — one wave per cleared line, sampled by the cell draw loop.
        // Amplitude rides the combo: `budget` already encodes how many lines went at once.
        EmitShock(lineCenter, Mathf.Min(1f, 0.55f + (1f - budget)), cell);

        switch (art)
        {
            case BurstArtifact.Fireworks:
            {
                Flash(lineCenter, cell * 1.6f, 0.20f, Gold);
                // Comets rise, then aerial shells burst at apex.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Streak, 1, 220, 360, 0.35f, 0.5f,
                    0.10f, 0.16f, 60, 2.2f, 0, 0, 0, 0.05f, 0, true, _ => Party[_rng.RandiRange(0, Party.Length - 1)],
                    angBias: -Mathf.Pi / 2f, angSpread: 0.5f, stride: 3);
                for (int a = 0; a < 3; a++)
                {
                    var apex = lineCenter + new Vector2((a - 1) * n * cell * 0.28f, -cell * 2.5f);
                    var hue = Party[_rng.RandiRange(0, Party.Length - 1)];
                    Radial(apex, Round(14, budget), FxKind.Dot, 120, 240, 0.5f, 0.8f, cell * 0.05f, cell * 0.10f,
                        90, 4.2f, 0, 0, 0, 0, 0.4f, true, _ => hue);
                    Radial(apex, Round(6, budget), FxKind.Star, 100, 200, 0.5f, 0.8f, cell * 0.12f, cell * 0.18f,
                        60, 4.2f, 1.5f, 1.5f, 0, 0, 0.4f, true, _ => hue);
                    Ring(apex, cell * 3f, cell * 0.12f, 0.45f, hue, delay: 0.4f);
                    Radial(apex, Round(4, budget), FxKind.Ember, 20, 70, 1.0f, 1.4f, cell * 0.05f, cell * 0.08f,
                        30, 4.5f, 0, 0, 0, 0, 0.5f, true, _ => Gold);
                }
                break;
            }
            case BurstArtifact.Confetti:
            {
                Ring(lineCenter, cell * 1.5f, cell * 0.08f, 0.25f, new Color(1, 1, 1, 0.5f));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Petal, 4, 60, 200, 0.9f, 1.5f,
                    0.10f, 0.16f, 300, 2.0f, 3f, 7f, 90, 0, 0, false, _ => Party[_rng.RandiRange(0, Party.Length - 1)],
                    angBias: -Mathf.Pi / 2f, angSpread: 1.3f);
                break;
            }
            case BurstArtifact.Supernova:
            {
                Flash(lineCenter, cell * 4f, 0.35f, new Color(1f, 0.98f, 0.9f));
                TriggerNova(0.5f, vignette: true);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Star, 2, 160, 360, 0.4f, 0.7f,
                    0.10f, 0.18f, 0, 2.5f, 1.5f, 2.5f, 0, 0, 0, true, _ => new Color(1f, 0.98f, 0.92f));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 3, 20, 90, 0.8f, 1.4f,
                    0.03f, 0.05f, 0, 1.2f, 0, 0, 0, 0, 0, true, _ => new Color(0.75f, 0.85f, 1f));
                Ring(lineCenter, cell * n * 0.6f, cell * 0.16f, 0.5f, new Color(1f, 0.98f, 0.9f), bloom: true);
                Ring(lineCenter, cell * n * 0.9f, cell * 0.10f, 0.8f, new Color(0.8f, 0.9f, 1f));
                break;
            }
            case BurstArtifact.Shards:
            {
                for (int c = 0; c < 3; c++)
                    Streak(lineCenter, _rng.RandfRange(0, Mathf.Tau), cell * 3f, 0.15f, new Color(1, 1, 1, 0.9f));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Shard, 3, 160, 420, 0.35f, 0.6f,
                    0.10f, 0.20f, 160, 2.2f, -12f, 12f, 0, 0, 0, false, preClear);
                Ring(lineCenter, cell * 3.5f, cell * 0.08f, 0.3f, Palette.Accent);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 4, 20, 80, 0.5f, 0.9f,
                    0.02f, 0.04f, 120, 2.5f, 0, 0, 0, 0, 0, true, preClear);
                break;
            }
            case BurstArtifact.Rainbow:
            {
                Flash(lineCenter, cell * 1.6f, 0.2f, new Color(1, 1, 1, 0.85f));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 3, 80, 240, 0.6f, 1.0f,
                    0.08f, 0.14f, 80, 3.0f, 0, 0, 40, 0, 0, true,
                    k => Color.FromHsv((k / (float)n) % 1f, 0.85f, 1f));
                Ring(lineCenter, cell * 3f, cell * 0.14f, 0.5f, Colors.White, rainbow: true);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Star, 1, 40, 120, 1.0f, 1.2f,
                    0.12f, 0.16f, 20, 2.5f, 1.5f, 1.5f, 0, 0, 0, true,
                    k => Color.FromHsv((k / (float)n + 0.5f) % 1f, 0.7f, 1f), stride: 3);
                break;
            }
            case BurstArtifact.Aurora:
            {
                int ribbons = Round(4, budget);
                for (int i = 0; i < ribbons; i++)
                {
                    float bx = origin.X + _rng.RandfRange(0.15f, 0.85f) * n * cell;
                    _ribbons.Add(new FxRibbon { Base = new Vector2(bx, lineCenter.Y), Age = 0, Life = 1.4f,
                        Seed = _rng.RandfRange(0, Mathf.Tau), Hue = _rng.RandfRange(0.33f, 0.55f), Cell = cell });
                }
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 1, 10, 40, 1.2f, 1.8f,
                    0.03f, 0.05f, 20, 1.0f, 0, 0, 12, 0, 0, true, _ => new Color(0.7f, 0.9f, 1f), stride: 2);
                break;
            }
            case BurstArtifact.Lightning:
            {
                Flash(lineCenter, cell * 2f, 0.14f, new Color(0.7f, 0.95f, 1f));
                TriggerNova(0.22f, vignette: false);
                int arcs = Mathf.Max(1, Round(2, budget));
                for (int a = 0; a < arcs; a++)
                {
                    var A = rowLine ? origin + new Vector2(0, (index + 0.5f) * cell) : origin + new Vector2((index + 0.5f) * cell, 0);
                    var B = rowLine ? origin + new Vector2(n * cell, (index + 0.5f) * cell) : origin + new Vector2((index + 0.5f) * cell, n * cell);
                    _bolts.Add(new FxBolt { A = A, B = B, Age = 0, Life = 0.18f, Seed = _rng.RandfRange(0, 999f) });
                }
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Ember, 1, 40, 120, 0.4f, 0.7f,
                    0.05f, 0.08f, 40, 4.5f, 0, 0, 0, 0, 0, true, _ => new Color(0.7f, 0.95f, 1f), stride: 2);
                break;
            }
            case BurstArtifact.BubblePop:
            {
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Bubble, 2, 20, 70, 0.8f, 1.2f,
                    0.14f, 0.22f, -60, 1.5f, 0, 0, 50, 0, 0, true,
                    k => Color.FromHsv((k * 0.11f) % 1f, 0.45f, 1f), angBias: -Mathf.Pi / 2f, angSpread: 0.8f);
                break;
            }
            case BurstArtifact.PrismBloom:
            {
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 3, 30, 130, 0.9f, 1.4f,
                    0.10f, 0.18f, -30, 1.6f, 0, 0, 24, 0, 0, true,
                    k => Color.FromHsv((k / (float)n + _rng.RandfRange(0, 0.1f)) % 1f, 0.7f, 1f),
                    angBias: -Mathf.Pi / 2f, angSpread: 1.2f);
                Ring(lineCenter, cell * 2.6f, cell * 0.10f, 0.5f, Palette.AccentViolet);
                break;
            }
            case BurstArtifact.Starfall:
            {
                Flash(lineCenter, cell * 1.4f, 0.18f, new Color(0.9f, 0.85f, 1f));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Streak, 2, 260, 360, 0.5f, 0.8f,
                    0.10f, 0.16f, 520, 0.8f, 0, 0, 0, 0.05f, 0, true,
                    _ => new Color(1f, 0.9f, 0.6f), angBias: -Mathf.Pi / 2f, angSpread: 0.7f);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Star, 1, 30, 90, 0.8f, 1.1f,
                    0.10f, 0.14f, 60, 2.0f, 1.5f, 1.5f, 0, 0, 0, true, _ => new Color(1f, 0.95f, 0.8f), stride: 2);
                break;
            }
            default: // Sparks — warm gold fountain + ring + embers
            {
                Flash(lineCenter, cell * 1.4f, 0.18f, Gold);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 2, 60, 220, 0.35f, 0.6f,
                    0.06f, 0.12f, 120, 3.7f, 0, 0, 0, 0, 0, true, _ => JitterGold(Gold));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Star, 1, 40, 120, 0.5f, 0.8f,
                    0.14f, 0.18f, 40, 3.0f, 1.5f, 1.5f, 0, 0, 0, true, _ => Gold, stride: 2);
                Ring(lineCenter, cell * 2.2f, cell * 0.10f, 0.35f, Gold);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Ember, 1, 15, 50, 1.0f, 1.3f,
                    0.05f, 0.08f, -20, 4.5f, 0, 0, 0, 0, 0, true, _ => new Color(1f, 0.7f, 0.35f), stride: 3);
                break;
            }
        }
    }

    private int Round(int baseCount, float budget) => Mathf.Max(1, Mathf.RoundToInt(baseCount * budget));
    private Color JitterGold(Color g) => new(g.R, Mathf.Clamp(g.G + _rng.RandfRange(-0.06f, 0.03f), 0, 1), g.B * _rng.RandfRange(0.85f, 1.05f), 1f);

    private void Flash(Vector2 pos, float size, float life, Color col)
        => Add(FxKind.Dot, pos, Vector2.Zero, life, size, 0, 0, 0, 0, 0, 0, true, col);

    private void Ring(Vector2 pos, float maxR, float width, float life, Color col, bool bloom = false, bool rainbow = false, float delay = 0f)
    {
        // delay approximated by a shorter effective life offset is unnecessary for rings; spawn immediately.
        _rings.Add(new FxRing { Pos = pos, Age = -delay, Life = life, MaxR = maxR, Width = width, Col = col, Bloom = bloom, Rainbow = rainbow });
    }

    private void Streak(Vector2 pos, float ang, float len, float life, Color col)
        => Add(FxKind.Streak, pos, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (len / Mathf.Max(0.01f, life)), life, len * 0.12f, 0, 0.5f, 0, 0, 0.05f, 0, true, col);

    private void TriggerNova(float peak, bool vignette)
    {
        // Photosensitivity floor: refuse a second full-screen bleach inside the cooldown.
        // A five-line combo fires EmitLine five times in one frame — without this the screen
        // would strobe at frame rate.
        if (_sinceFlash < FlashCooldown) return;
        _sinceFlash = 0f;
        _novaAge = 0f; _novaPeak = peak; _novaVignette = vignette;
    }

    // ---- Board shock (the "it hit hard" displacement wave) ------------------

    /// <summary>Spawn an expanding displacement ring. Callers that draw a grid sample it with
    /// <see cref="BoardShock"/>; hosts that don't simply ignore it (no draw cost either way).</summary>
    public void EmitShock(Vector2 centre, float amp, float cell)
    {
        if (Motion.Reduced || amp <= 0f) return;
        if (_shockCount >= MaxShocks) _shockCount = MaxShocks - 1;   // newest wins
        _shocks[_shockCount++] = new FxShock
        {
            C = centre, Age = 0f, Life = ShockLife, Amp = amp, Sigma = Mathf.Max(1f, cell * 1.1f),
        };
    }

    /// <summary>
    /// Sample the active shock waves at a point. Returns false when nothing is displacing it —
    /// including ALWAYS under reduced motion, so callers need no second gate.
    /// <paramref name="amp"/> is a 0..1 scalar the caller multiplies by its own cell size.
    /// </summary>
    public bool BoardShock(Vector2 p, out Vector2 dir, out float amp)
    {
        dir = Vector2.Zero; amp = 0f;
        if (_shockCount == 0 || Motion.Reduced) return false;
        for (int i = 0; i < _shockCount; i++)
        {
            ref readonly var s = ref _shocks[i];
            if (s.Age < 0f) continue;
            var d = p - s.C;
            float dist = d.Length();
            float band = (dist - ShockSpeed * s.Age) / s.Sigma;
            float g = Mathf.Exp(-band * band);
            float a = s.Amp * g * (1f - s.Age / s.Life);
            if (a <= 0.002f) continue;
            amp += a;
            if (dist > 0.001f) dir += d / dist * a;
        }
        if (amp <= 0.002f) { amp = 0f; return false; }
        dir = dir.Normalized();
        amp = Mathf.Min(amp, 1f);
        return true;
    }

    private void Add(FxKind kind, Vector2 pos, Vector2 vel, float life, float size, float grav, float drag,
                     float spin, float flutter, float trail, float delay, bool additive, Color col)
    {
        if (_fx.Count >= MaxFx) return;
        _fx.Add(new FxSpark
        {
            Pos = pos, Vel = vel, Age = -delay, Life = life, Size = size, Grav = grav, Drag = drag,
            Spin = spin, Flutter = flutter, Trail = trail, Rot = _rng.RandfRange(0, Mathf.Tau),
            Seed = _rng.RandfRange(0, Mathf.Tau), Kind = kind, Additive = additive, Col = col,
        });
    }

    private void EmitAlong(bool rowLine, int index, Vector2 origin, float cell, int n, float budget,
        FxKind kind, int perCell, float spdMin, float spdMax, float lifeMin, float lifeMax,
        float sizeMinF, float sizeMaxF, float grav, float drag, float spinMin, float spinMax,
        float flutter, float trail, float delay, bool additive, Func<int, Color> colourFn,
        float angBias = 999f, float angSpread = Mathf.Pi, int stride = 1)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(perCell * budget));
        for (int k = 0; k < n; k++)
        {
            if (stride > 1 && k % stride != 0) continue;
            var center = rowLine
                ? origin + new Vector2((k + 0.5f) * cell, (index + 0.5f) * cell)
                : origin + new Vector2((index + 0.5f) * cell, (k + 0.5f) * cell);
            var col = colourFn(k);
            for (int s = 0; s < count; s++)
            {
                if (_fx.Count >= MaxFx) return;
                float ang = angBias > 900f ? _rng.RandfRange(0, Mathf.Tau) : angBias + _rng.RandfRange(-angSpread, angSpread);
                float spd = _rng.RandfRange(spdMin, spdMax);
                float size = cell * _rng.RandfRange(sizeMinF, sizeMaxF);
                Add(kind, center, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                    _rng.RandfRange(lifeMin, lifeMax), size, grav, drag,
                    _rng.RandfRange(spinMin, spinMax), flutter, trail, delay, additive, col);
            }
        }
    }

    private void Radial(Vector2 center, int count, FxKind kind, float spdMin, float spdMax,
        float lifeMin, float lifeMax, float sizeMin, float sizeMax, float grav, float drag,
        float spinMin, float spinMax, float flutter, float trail, float delay, bool additive, Func<int, Color> colourFn)
    {
        for (int s = 0; s < count; s++)
        {
            if (_fx.Count >= MaxFx) return;
            float ang = Mathf.Tau * s / Mathf.Max(1, count) + _rng.RandfRange(-0.2f, 0.2f);
            float spd = _rng.RandfRange(spdMin, spdMax);
            Add(kind, center, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                _rng.RandfRange(lifeMin, lifeMax), _rng.RandfRange(sizeMin, sizeMax), grav, drag,
                _rng.RandfRange(spinMin, spinMax), flutter, trail, delay, additive, colourFn(s));
        }
    }

    // ---- Draw: normal-alpha surface (paper, glass — no glow) ----------------

    /// <summary>
    /// The rigid-chunk pass on its own, so a host can choose its OWN z-order for it. Block Fit
    /// draws this UNDER the board cells: debris is the biggest, longest-lived, most opaque thing
    /// the engine makes, and folded into <see cref="DrawNormal"/> (which a host naturally calls
    /// last) it covered the cells, the line-clear preview and the piece in the player's hand.
    /// Hosts that don't care keep calling <see cref="DrawNormal"/> with its default.
    /// </summary>
    public void DrawDebrisNormal(CanvasItem ci) => Debris.DrawNormal(ci);

    /// <summary>
    /// The GLOWING half of the same chunk pass, split out for exactly the same reason as
    /// <see cref="DrawDebrisNormal"/>. Without it the z-order fix was only half a fix: a
    /// material whose chunks are additive (NeonTube — the VAPOR TUBE skin) is skipped by
    /// <see cref="DebrisField.DrawNormal"/> and appears ONLY on the additive surface, which is
    /// a child node and therefore draws after its host's own <c>_Draw</c>. So on that one skin
    /// the debris still covered the board and the piece in hand while every other skin was
    /// fixed. A host that wants debris under its cells now routes BOTH halves below them:
    /// this one onto an additive layer of its own, and passes <c>includeDebris: false</c> to
    /// <see cref="DrawAdditive"/> so the top layer doesn't draw the chunks a second time.
    /// "Don't cover the board" is a promise about debris, not about a material.
    /// </summary>
    public void DrawDebrisAdditive(CanvasItem ci) => Debris.DrawAdditive(ci);

    /// <param name="includeDebris">False when the host already drew the chunk pass itself via
    /// <see cref="DrawDebrisNormal"/> at a lower z-order.</param>
    public void DrawNormal(CanvasItem ci, float cell, Rect2 screen, bool includeDebris = true)
    {
        if (includeDebris) Debris.DrawNormal(ci);
        foreach (var s in _fx)
        {
            if (s.Age < 0f || s.Additive) continue;
            float t = s.Age / s.Life, a = 1f - t;
            switch (s.Kind)
            {
                case FxKind.Petal: DrawPetal(ci, s, a); break;
                case FxKind.Shard: DrawShard(ci, s, a); break;
                default: ci.DrawCircle(s.Pos, s.Size * a, new Color(s.Col.R, s.Col.G, s.Col.B, a)); break;
            }
        }
        // Supernova vignette (dark frame around the blast).
        if (_novaVignette && _novaAge < 0.6f && screen.Size.X > 0f)
        {
            float va = 0.5f * Mathf.Sin(Mathf.Pi * Mathf.Clamp(_novaAge / 0.6f, 0f, 1f));
            ci.DrawTextureRect(TextureFactory.Vignette(256), screen, false, new Color(0.02f, 0.02f, 0.06f, va));
        }
    }

    // ---- Draw: additive surface (glow, stars, rings, flash) -----------------

    /// <param name="includeDebris">False when the host already drew the glowing chunk pass
    /// itself via <see cref="DrawDebrisAdditive"/> at a lower z-order.</param>
    public void DrawAdditive(CanvasItem ci, float cell, Rect2 screen, bool includeDebris = true)
    {
        // Screen bleach (Supernova / Lightning).
        if (_novaPeak > 0f && _novaAge < 0.6f && screen.Size.X > 0f)
        {
            float fa = _novaPeak * Mathf.Min(1f, _novaAge / 0.06f) * Mathf.Exp(-Mathf.Max(0f, _novaAge - 0.06f) / 0.12f);
            fa = Mathf.Min(fa, MaxScreenAdditive);
            if (fa > 0.003f) ci.DrawRect(screen, new Color(1f, 0.98f, 0.9f, fa));
        }

        if (includeDebris) Debris.DrawAdditive(ci);

        foreach (var rg in _rings)
        {
            if (rg.Age < 0f) continue;
            float t = rg.Age / rg.Life;
            float rad = rg.MaxR * Mathf.Sqrt(Mathf.Clamp(t, 0f, 1f));
            float a = 1f - t;
            if (rg.Bloom)
            {
                float side = 2f * rad;
                var rect = new Rect2(rg.Pos - new Vector2(side / 2f, side / 2f), new Vector2(side, side));
                ci.DrawTextureRect(TextureFactory.GlowDisc(64), rect, false, new Color(rg.Col.R, rg.Col.G, rg.Col.B, a * 0.7f));
            }
            else if (rg.Rainbow)
            {
                // Kept as a polyline on purpose: the travelling hue cycle IS this artifact's
                // identity and a baked ring can't express it. Only the allocation is gone.
                for (int i = 0; i < 26; i++)
                {
                    float ang = Mathf.Tau * i / 25f;
                    S26[i] = rg.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    S26C[i] = Color.FromHsv((i / 25f + rg.Age * 0.1f) % 1f, 0.85f, 1f, a);
                }
                ci.DrawPolylineColors(S26, S26C, Mathf.Max(2f, rg.Width * (1f - t)));
            }
            else
            {
                // One baked quad instead of a 48-segment polyline — and the bake carries a
                // chromatic R/G/B split the DrawArc version could never afford.
                float side = 2f * rad / TextureFactory.ShockRingCrest;
                var rect = new Rect2(rg.Pos - new Vector2(side / 2f, side / 2f), new Vector2(side, side));
                ci.DrawTextureRect(ShockRingTex, rect, false,
                                   new Color(rg.Col.R, rg.Col.G, rg.Col.B, a * 0.9f));
            }
        }

        foreach (var rb in _ribbons) DrawRibbon(ci, rb);
        foreach (var b in _bolts) DrawBolt(ci, b);

        foreach (var s in _fx)
        {
            if (s.Age < 0f || !s.Additive) continue;
            float t = s.Age / s.Life, a = 1f - t;
            switch (s.Kind)
            {
                case FxKind.Star: DrawStar(ci, s, t); break;
                case FxKind.Streak: DrawComet(ci, s, a); break;
                case FxKind.Ember: DrawEmber(ci, s, t); break;
                case FxKind.Bubble: DrawBubble(ci, s, t); break;
                default: DrawGlowDot(ci, s, a); break;
            }
        }
    }

    /// <summary>The particle bloom kernel. A 4-point anisotropic star (tight core + wide skirt
    /// + diffraction streaks) rather than a plain disc — a lens-flare read for the same single
    /// draw call, so this is a pure quality win at zero runtime cost.</summary>
    private static readonly ImageTexture Glow = TextureFactory.BloomStar(128, 4);

    /// <summary>The baked shockwave annulus (chromatic R/G/B split). Held here so the ring
    /// draw doesn't rebuild TextureFactory's interpolated cache key every frame.</summary>
    private static readonly ImageTexture ShockRingTex = TextureFactory.ShockRing(128);

    private void DrawGlowDot(CanvasItem ci, FxSpark s, float a)
    {
        float r = s.Size * (0.6f + 0.4f * a);
        float side = r * 2.6f;
        ci.DrawTextureRect(Glow, new Rect2(s.Pos - new Vector2(side / 2f, side / 2f), new Vector2(side, side)), false,
            new Color(s.Col.R, s.Col.G, s.Col.B, a * 0.9f));
        ci.DrawCircle(s.Pos, r * 0.42f, new Color(Mathf.Min(1, s.Col.R + 0.4f), Mathf.Min(1, s.Col.G + 0.4f), Mathf.Min(1, s.Col.B + 0.4f), a));
    }

    private void DrawStar(CanvasItem ci, FxSpark s, float t)
    {
        float rp = s.Size * (0.8f + 0.2f * Mathf.Sin(s.Age * 18f + s.Seed));
        float tw = 0.6f + 0.4f * Mathf.Sin(s.Age * 22f + s.Seed);
        float a = (1f - t) * Mathf.Clamp(tw, 0f, 1f);
        for (int i = 0; i < 8; i++)
        {
            float rr = (i % 2 == 0) ? rp : rp * 0.34f;
            float ang = s.Rot + i * Mathf.Pi / 4f;
            S8[i] = s.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
        }
        ci.DrawColoredPolygon(S8, new Color(s.Col.R, s.Col.G, s.Col.B, a));
        var ax = new Vector2(Mathf.Cos(s.Rot), Mathf.Sin(s.Rot));
        var ay = new Vector2(-ax.Y, ax.X);
        var w = new Color(1, 1, 1, a * 0.8f);
        ci.DrawLine(s.Pos - ax * rp * 1.6f, s.Pos + ax * rp * 1.6f, w, 1.5f);
        ci.DrawLine(s.Pos - ay * rp * 1.6f, s.Pos + ay * rp * 1.6f, w, 1.5f);
    }

    private void DrawComet(CanvasItem ci, FxSpark s, float a)
    {
        float speed = s.Vel.Length();
        if (speed < 8f) { DrawGlowDot(ci, s, a); return; }
        var dir = s.Vel / speed;
        float tlen = Mathf.Min(s.Size * 3.2f, speed * Mathf.Max(0.03f, s.Trail));
        var tail = s.Pos - dir * tlen;
        var perp = new Vector2(-dir.Y, dir.X);
        float wH = s.Size, wT = s.Size * 0.15f;
        S4[0] = s.Pos + perp * wH; S4[1] = s.Pos - perp * wH; S4[2] = tail - perp * wT; S4[3] = tail + perp * wT;
        ci.DrawColoredPolygon(S4, new Color(s.Col.R, s.Col.G, s.Col.B, a));
        float side = wH * 3f;
        ci.DrawTextureRect(Glow, new Rect2(s.Pos - new Vector2(side / 2f, side / 2f), new Vector2(side, side)), false,
            new Color(Mathf.Min(1, s.Col.R + 0.4f), Mathf.Min(1, s.Col.G + 0.4f), Mathf.Min(1, s.Col.B + 0.4f), a));
    }

    private void DrawPetal(CanvasItem ci, FxSpark s, float a)
    {
        float e = Mathf.Abs(Mathf.Cos(s.Rot * 1.3f));
        float w = s.Size * (0.2f + 0.8f * e), hgt = s.Size * 1.6f;
        float cr = Mathf.Cos(s.Rot), sr = Mathf.Sin(s.Rot);
        Vector2 R(float x, float y) => s.Pos + new Vector2(x * cr - y * sr, x * sr + y * cr);
        var face = e > 0.5f ? s.Col : new Color(s.Col.R * 0.55f, s.Col.G * 0.55f, s.Col.B * 0.55f);
        S4[0] = R(-w, -hgt); S4[1] = R(w, -hgt); S4[2] = R(w, hgt); S4[3] = R(-w, hgt);
        ci.DrawColoredPolygon(S4, new Color(face.R, face.G, face.B, a));
    }

    private void DrawShard(CanvasItem ci, FxSpark s, float a)
    {
        var dir = new Vector2(Mathf.Cos(s.Rot), Mathf.Sin(s.Rot));
        var perp = new Vector2(-dir.Y, dir.X);
        float L = s.Size * 2.2f, W = s.Size * 0.7f;
        var A = s.Pos + dir * L * 0.65f;
        var B = s.Pos - dir * L * 0.35f + perp * W;
        var C = s.Pos - dir * L * 0.35f - perp * W;
        S3[0] = A; S3[1] = B; S3[2] = C;
        ci.DrawColoredPolygon(S3, new Color(s.Col.R, s.Col.G, s.Col.B, a));
        ci.DrawLine(A, B, new Color(1, 1, 1, a * 0.8f), 1.5f);
    }

    private void DrawEmber(CanvasItem ci, FxSpark s, float t)
    {
        float fl = 0.6f + 0.4f * Mathf.Sin(s.Age * 28f + s.Seed);
        float a = (1f - t) * fl;
        float r = s.Size * (0.7f + 0.1f * Mathf.Sin(s.Age * 3f + s.Seed));
        float side = r * 2.6f;
        ci.DrawTextureRect(Glow, new Rect2(s.Pos - new Vector2(side / 2f, side / 2f), new Vector2(side, side)), false,
            new Color(s.Col.R, s.Col.G, s.Col.B, a));
    }

    private void DrawBubble(CanvasItem ci, FxSpark s, float t)
    {
        float a = 1f - t;
        float r = s.Size * (1f + 0.6f * t);
        ci.DrawArc(s.Pos, r, 0f, Mathf.Tau, 20, new Color(s.Col.R, s.Col.G, s.Col.B, a), 2f);
        ci.DrawCircle(s.Pos + new Vector2(-0.3f * r, -0.3f * r), Mathf.Max(1f, r * 0.12f), new Color(1, 1, 1, a * 0.9f));
        if (t > 0.85f)
            ci.DrawArc(s.Pos, r * 1.3f, 0f, Mathf.Tau, 16, new Color(s.Col.R, s.Col.G, s.Col.B, a * 2f), 2f);
    }

    private void DrawRibbon(CanvasItem ci, FxRibbon rb)
    {
        float t = rb.Age / rb.Life;
        float baseA = (0.4f + 0.3f * Mathf.Sin(rb.Age * 4f)) * (1f - t);
        for (int i = 0; i < 12; i++)
        {
            S12[i] = rb.Base + new Vector2(Mathf.Sin(rb.Age * 2f + i * 0.6f + rb.Seed) * rb.Cell * 0.6f, -i * rb.Cell * 0.7f);
            float hue = (rb.Hue + i * 0.03f + rb.Age * 0.1f) % 1f;
            S12C[i] = Color.FromHsv(hue, 0.7f, 1f, baseA * (1f - i / 12f));
        }
        ci.DrawPolylineColors(S12, S12C, Mathf.Max(2f, rb.Cell * 0.35f));
    }

    private void DrawBolt(CanvasItem ci, FxBolt b)
    {
        float a = 1f - b.Age / b.Life;
        const int segs = BoltSegs;
        int jitter = (int)(b.Age * 40f);                          // re-jitter a few times → buzz
        var dir = b.B - b.A;
        var perp = new Vector2(-dir.Y, dir.X).Normalized();
        float cell = dir.Length() / segs;
        for (int i = 0; i <= segs; i++)
        {
            float f = i / (float)segs;
            float off = (i == 0 || i == segs) ? 0f : (Hash01(b.Seed + i * 7 + jitter) - 0.5f) * cell * 0.8f;
            SBolt[i] = b.A + dir * f + perp * off;
        }
        ci.DrawPolyline(SBolt, new Color(0.3f, 0.85f, 1f, a * 0.35f), cell * 0.30f);
        ci.DrawPolyline(SBolt, new Color(1f, 1f, 1f, a), cell * 0.10f);
    }

    private static float Hash01(float x)
    {
        float s = Mathf.Sin(x * 127.1f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}

/// <summary>A Node2D whose only job is to draw a <see cref="BurstEngine"/>'s additive (glow)
/// half with a BlendMode.Add material, so bright particles sum toward white (SDR bloom without
/// HDR). Shared by the Block Fit board and the store's ArtifactPreview.
///
/// Blending is a property of a CANVAS ITEM, never of a draw call, so "some glow under the board
/// cells and the rest over them" cannot be expressed on one node — it needs two layers at two
/// z-orders. <see cref="Pass"/> is how a host says which half this instance carries; a host that
/// doesn't care keeps the default and gets everything.</summary>
public sealed partial class AdditiveFxLayer : Node2D
{
    /// <summary>Which slice of the additive surface this layer draws.</summary>
    public enum Pass : byte
    {
        /// <summary>Everything (the default — one layer, drawn over the host).</summary>
        Full,
        /// <summary>Only the glowing rigid chunks, for a layer placed UNDER the board cells.</summary>
        DebrisOnly,
        /// <summary>Everything except the chunks, for the layer that stays on top.</summary>
        NoDebris,
    }

    private readonly BurstEngine _engine;
    private readonly Func<float> _cell;
    private readonly Func<Rect2> _screen;
    private readonly Pass _pass;

    public AdditiveFxLayer(BurstEngine engine, Func<float> cell, Func<Rect2> screen, Pass pass = Pass.Full)
    {
        _engine = engine; _cell = cell; _screen = screen; _pass = pass;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    public override void _Draw()
    {
        switch (_pass)
        {
            case Pass.DebrisOnly: _engine.DrawDebrisAdditive(this); break;
            case Pass.NoDebris: _engine.DrawAdditive(this, _cell(), _screen(), includeDebris: false); break;
            default: _engine.DrawAdditive(this, _cell(), _screen()); break;
        }
    }
}
