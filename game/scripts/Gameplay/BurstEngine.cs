using Godot;
using System;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>The shape of one burst particle. One struct drives every kind so the
/// update loop stays branch-light and allocation-free. Append-only by habit: the value is
/// never persisted, but the recipes below read like a table and reordering churns the diff.
///
/// Slice/Pixel are the hard-edged (digital) family, Blossom/Blot/Drip the soft matte family,
/// Spike the only ROOTED kind — it grows in place instead of travelling.</summary>
public enum FxKind : byte
{
    Dot, Star, Streak, Petal, Shard, Ember, Bubble,
    Slice, Pixel, Blossom, Spike, Blot, Drip,
}

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
        /// <summary>The kind-specific SECOND dimension, in pixels. <see cref="Size"/> alone can
        /// only describe a blob; the newer kinds are anisotropic and need one more number:
        /// Slice = half-width (Size is half-height), Spike = blade length, Blot = final spread
        /// radius. Zero and unread for every other kind.</summary>
        public float Aux;
        /// <summary>Rotational field, rad/s, applied to the VELOCITY (not the sprite — that is
        /// <see cref="Spin"/>). A curl term is the difference between petals falling and petals
        /// swirling, and it is one branch in the update loop instead of a second integrator.</summary>
        public float Swirl;
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

    /// <summary>GLITCH's dust palette: the three video channels plus one blown-out white. It is
    /// deliberately NOT taken from the board — a datamoshed frame is a property of the signal,
    /// not of the blocks, so it has to stay legible on top of any skin's colours.</summary>
    private static readonly Color[] Channels =
    {
        new(1f, 0.20f, 0.35f), new(0.25f, 1f, 0.55f), new(0.35f, 0.55f, 1f), new(0.95f, 0.95f, 1f),
    };

    /// <summary>PETALS' blossom tint. Cleared cells are lerped most of the way toward it so a
    /// garish skin still drops soft petals, while the board's hue stays faintly readable.</summary>
    private static readonly Color Blush = new(1f, 0.72f, 0.82f);

    /// <summary>FROST's ice. Pale cyan rather than white: white snow over a white flash is a
    /// single flat blob, and the crystal stage needs to read as a MATERIAL.</summary>
    private static readonly Color Ice = new(0.78f, 0.94f, 1f);

    /// <summary>One petal outline in unit space (x = across, y = tip→base). Baked as two static
    /// ramps so <see cref="DrawBlossom"/> only multiplies and rotates — no per-frame shape
    /// authoring, no allocation, and every petal in the game is provably the same silhouette.</summary>
    private static readonly float[] PetalX = { 0f, 0.60f, 1.00f, 0.55f, 0f, -0.55f, -1.00f, -0.60f };
    private static readonly float[] PetalY = { -1.00f, -0.50f, 0.10f, 0.85f, 1.00f, 0.85f, 0.10f, -0.50f };

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
            // Curl AFTER gravity so the fall itself gets bent — that is what turns a straight
            // descent into the looping path a real petal takes. Same opt-in shape as Flutter:
            // zero for every kind that doesn't ask, so the loop stays branch-light.
            if (s.Swirl != 0f) s.Vel = s.Vel.Rotated(s.Swirl * dt);
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
            // ---- Animal-set signatures ------------------------------------------------
            // All three stay well inside MaxFx (30 / 25 / 30 at full budget) and all three
            // reach the board only through EmitLine, whose caller already refuses to fire under
            // reduced motion — so none of them needs a gate of its own.
            case BurstArtifact.Fluff:
            {
                // Deliberately no Flash and nothing additive: a matte coat must not bleach the
                // screen. Drifting tufts in the popped cells' own colours, then one soft ring.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Petal, 3, 40, 120, 0.55f, 0.95f,
                    0.06f, 0.11f, 30, 3.5f, 0.8f, 2.2f, 0.9f, 0f, 0f, false, preClear,
                    angBias: -Mathf.Pi / 2f, angSpread: 1.5f, stride: 1);
                Ring(lineCenter, cell * 1.8f, cell * 0.06f, 0.30f, new Color(1f, 0.86f, 0.70f, 0.28f));
                break;
            }
            case BurstArtifact.Splash:
            {
                // A column of water, two ripples at different speeds, then falling droplets.
                var water = new Color(0.55f, 0.95f, 1f);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 2, 180, 300, 0.30f, 0.45f,
                    0.05f, 0.09f, 520, 1.2f, 0, 0, 0, 0.25f, 0f, false, _ => water,
                    angBias: -Mathf.Pi / 2f, angSpread: 0.35f, stride: 1);
                Ring(lineCenter, cell * 2.6f, cell * 0.09f, 0.34f, water);
                Ring(lineCenter, cell * 4.2f, cell * 0.06f, 0.52f, new Color(water.R, water.G, water.B, 0.45f), delay: 0.10f);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 1, 60, 140, 0.55f, 0.85f,
                    0.04f, 0.07f, 420, 0.6f, 0, 0, 0, 0.35f, 0.12f, false, preClear,
                    angBias: -Mathf.Pi / 2f, angSpread: 1.1f, stride: 2);
                break;
            }
            case BurstArtifact.Swarm:
            {
                // The shell cracks along one ring, then the pieces scatter and are gone. No
                // Flash, no TriggerNova — the carapace set's whole pitch is restraint.
                Ring(lineCenter, cell * 2.2f, cell * 0.05f, 0.22f, new Color(0.75f, 1f, 0.90f, 0.55f));
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 3, 110, 260, 0.40f, 0.70f,
                    0.035f, 0.06f, -20, 2.8f, 0, 0, 2.6f, 0f, 0f, false, preClear,
                    angSpread: Mathf.Pi, stride: 1);
                break;
            }
            // ---- The texture set -------------------------------------------------------
            // Each of these four owns a MATERIAL, not a colour, which is how they stay distinct
            // from the eleven light-and-sparkle recipes above. Unlike the animal signatures they
            // each carry their OWN reduced-motion gate: both current hosts refuse to call
            // EmitLine when Motion.Reduced, but that is a promise about the CALLER, and GLITCH
            // (hard RGB strobing) and FROST (glint flicker) are exactly the recipes where
            // inheriting a photosensitivity guarantee from a host would be a bug waiting for a
            // third host. Peak counts at budget=1, n=8: 26 / 27 / 34 / 23 — the same envelope as
            // the animal set (30/25/30), well inside MaxFx.
            case BurstArtifact.Glitch:
            {
                if (Motion.Reduced) { ReducedStill(rowLine, index, origin, cell, n, preClear); break; }
                float span = n * cell;
                // First: the whole line drops out white for 75ms. Same Slice primitive as the tears
                // (so it splits into RGB fringes too), just line-wide — a signal dropout, not a
                // Flash, because nothing in this artifact is allowed to be soft. Kept off pure
                // white and short: it is a band-wide additive rect and five lines can fire at once.
                AddSlice(lineCenter, rowLine ? span * 0.5f : cell * 0.5f,
                                     rowLine ? cell * 0.5f : span * 0.5f, 0.075f, new Color(0.90f, 0.93f, 1f));
                // Then the line shears into bands. Bands are cut ACROSS the line and always tear
                // along screen-X: datamoshing is an artifact of scanline order, so a cleared
                // COLUMN must smear sideways as well — it must not look like a rotated row.
                int bands = Mathf.Max(2, Round(5, budget));
                for (int b = 0; b < bands; b++)
                {
                    float f = (b + 0.5f) / bands;
                    var c = rowLine
                        ? origin + new Vector2(f * span, (index + 0.5f) * cell)
                        : origin + new Vector2((index + 0.5f) * cell, f * span);
                    AddSlice(c, rowLine ? span / (2f * bands) : cell * 0.5f,
                                rowLine ? cell * 0.5f : span / (2f * bands),
                             _rng.RandfRange(0.26f, 0.40f), preClear(Mathf.Min(n - 1, (int)(f * n))));
                }
                // Channel dust: quantised squares, no gravity, snapping to their own pixel grid.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Pixel, 2, 40, 170, 0.26f, 0.46f,
                    0.07f, 0.12f, 0, 3.4f, 0, 0, 0, 0, 0, true,
                    _ => Channels[_rng.RandiRange(0, Channels.Length - 1)], angSpread: Mathf.Pi, stride: 1);
                // A few dead pixels fall out of the frame afterwards — the "it broke" tail.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Pixel, 1, 10, 60, 0.40f, 0.65f,
                    0.05f, 0.09f, 380, 1.6f, 0, 0, 0, 0, 0.07f, false, preClear, stride: 2);
                break;
            }
            case BurstArtifact.Petals:
            {
                if (Motion.Reduced) { ReducedStill(rowLine, index, origin, cell, n, preClear); break; }
                // No Flash, no TriggerNova, no shell burst, nothing additive. And only the COUNT
                // rides `budget` — speed, life, size and swirl are constants — so a five-line
                // combo is a thicker fall of the same gentle petal, never a louder one. That is
                // the whole product promise of this row.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Blossom, 3, 35, 110, 1.20f, 1.90f,
                    0.11f, 0.18f, 55f, 1.30f, -1.4f, 1.4f, 22f, 0f, 0f, false,
                    k => preClear(k).Lerp(Blush, 0.62f),
                    angBias: -Mathf.Pi / 2f, angSpread: 1.25f, stride: 1, swirl: 1.15f);
                // Pollen on the same field, so the air between the petals isn't empty. Terminal
                // velocity is Grav/Drag ≈ 25px/s: it hangs, it does not drop.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 1, 20, 60, 1.0f, 1.5f,
                    0.02f, 0.035f, 30f, 1.20f, 0f, 0f, 14f, 0f, 0f, false,
                    _ => new Color(1f, 0.93f, 0.80f),
                    angBias: -Mathf.Pi / 2f, angSpread: 1.6f, stride: 3, swirl: 0.8f);
                break;
            }
            case BurstArtifact.Frost:
            {
                if (Motion.Reduced) { ReducedStill(rowLine, index, origin, cell, n, preClear); break; }
                float span = n * cell;
                // Two acts with a beat between them, staged the way Fireworks stages its shells:
                // the crystal owns the first GrowBeat, then everything else is delayed past it.
                const float GrowBeat = 0.34f;
                int spikes = Mathf.Max(2, Round(6, budget));
                for (int i = 0; i < spikes; i++)
                {
                    float f = (i + 0.5f) / spikes;
                    var c = rowLine
                        ? origin + new Vector2(f * span, (index + 0.5f) * cell)
                        : origin + new Vector2((index + 0.5f) * cell, f * span);
                    // Alternate sides: a comb of opposed blades reads as a crystal growing OUT of
                    // the line. All-one-side reads as a fringe hanging off it.
                    bool up = i % 2 == 0;
                    float ang = rowLine ? (up ? -Mathf.Pi / 2f : Mathf.Pi / 2f) : (up ? Mathf.Pi : 0f);
                    AddSpike(c, ang + _rng.RandfRange(-0.34f, 0.34f), cell * _rng.RandfRange(0.9f, 1.7f),
                             cell * 0.26f, GrowBeat + 0.10f, Ice);
                }
                // The shatter tick, on the beat: a small cold flash and one thin ring, both
                // delayed. No TriggerNova — a screen bleach would erase the crystals it breaks.
                Flash(lineCenter, cell * 1.2f, 0.13f, Ice, delay: GrowBeat);
                Ring(lineCenter, cell * 3f, cell * 0.07f, 0.42f, Ice, delay: GrowBeat);
                // Then fine snow, and it must OUTLIVE the spikes or the effect ends mid-shatter.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 3, 25, 120, 0.90f, 1.50f,
                    0.025f, 0.05f, 70f, 1.6f, 0f, 0f, 16f, 0f, GrowBeat, true,
                    _ => Ice, angSpread: Mathf.Pi, stride: 1);
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Star, 1, 20, 70, 0.70f, 1.0f,
                    0.05f, 0.08f, 50f, 1.8f, 1.2f, 2.4f, 12f, 0f, GrowBeat + 0.05f, true,
                    _ => new Color(0.92f, 0.98f, 1f), stride: 3);
                break;
            }
            case BurstArtifact.Ink:
            {
                if (Motion.Reduced) { ReducedStill(rowLine, index, origin, cell, n, preClear); break; }
                float span = n * cell;
                // The only artifact with NOTHING on the additive surface: pigment absorbs light,
                // it does not emit it. Every particle here is normal-alpha and darker than the
                // cell it came from — which is also why it needs no photosensitivity budget.
                int blots = Mathf.Max(2, Round(3, budget));
                for (int i = 0; i < blots; i++)
                {
                    float f = Mathf.Clamp((i + 0.5f) / blots + _rng.RandfRange(-0.07f, 0.07f), 0.04f, 0.96f);
                    var c = rowLine
                        ? origin + new Vector2(f * span, (index + 0.5f) * cell)
                        : origin + new Vector2((index + 0.5f) * cell, f * span);
                    AddBlot(c, cell * _rng.RandfRange(1.1f, 1.7f), _rng.RandfRange(0.55f, 0.80f),
                            preClear(Mathf.Min(n - 1, (int)(f * n))));
                }
                // Splatter thrown by the strike — round droplets, heavy, short.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Dot, 2, 70, 220, 0.35f, 0.60f,
                    0.05f, 0.10f, 620f, 1.4f, 0f, 0f, 0f, 0f, 0f, false, k => Pigment(preClear(k), 0.34f),
                    angSpread: 2.2f, stride: 1);
                // And the runs: they start almost still, so gravity — not the blast — is visibly
                // what pulls them down, and they arrive after the bloom has opened. Terminal
                // velocity is Grav/Drag ≈ 133px/s ≈ 2 cells per second: heavy drag is what makes
                // this a RUN creeping down the board. The obvious tuning (520/0.9, matching the
                // splatter) terminates at 578px/s and reads as a bullet, not as ink.
                EmitAlong(rowLine, index, origin, cell, n, budget, FxKind.Drip, 1, 10, 45, 0.90f, 1.40f,
                    0.06f, 0.10f, 200f, 1.50f, 0f, 0f, 0f, 0f, 0.10f, false, k => Pigment(preClear(k), 0.24f),
                    angBias: Mathf.Pi / 2f, angSpread: 0.35f, stride: 2);
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

    /// <param name="delay">Seconds to hold the flash back, for recipes that stage in acts (FROST
    /// flashes on the shatter, not on the clear). Same negative-Age mechanism the shells use.</param>
    private void Flash(Vector2 pos, float size, float life, Color col, float delay = 0f)
        => Add(FxKind.Dot, pos, Vector2.Zero, life, size, 0, 0, 0, 0, 0, delay, true, col);

    /// <summary>GLITCH's tear band. Rooted (zero velocity): the whole point of a datamosh is that
    /// the block SNAPS BACK onto the pixels it came from, and a velocity-driven slice would drift
    /// so the "signal restored" beat could never land. The tear is a function of Age instead —
    /// see <see cref="DrawSlice"/>.</summary>
    private void AddSlice(Vector2 pos, float halfW, float halfH, float life, Color col)
        => Add(FxKind.Slice, pos, Vector2.Zero, life, halfH, 0, 0, 0, 0, 0, 0, true, col, aux: halfW);

    /// <summary>FROST's crystal blade: rooted at the line, grows along <paramref name="ang"/>.
    /// Rot carries the direction here, so it is passed explicitly rather than randomised.</summary>
    private void AddSpike(Vector2 pos, float ang, float len, float halfW, float life, Color col)
        => Add(FxKind.Spike, pos, Vector2.Zero, life, halfW, 0, 0, 0, 0, 0, 0, false, col, aux: len, rot: ang);

    /// <summary>INK's bloom. Rooted and never additive; <paramref name="maxR"/> is where the
    /// spread asymptotes, not where it starts.</summary>
    private void AddBlot(Vector2 pos, float maxR, float life, Color col)
        => Add(FxKind.Blot, pos, Vector2.Zero, life, maxR * 0.5f, 0, 0, 0, 0, 0, 0, false, col, aux: maxR);

    /// <summary>Push a cell colour down toward ink. Keeps the hue and kills the value, so the
    /// board's own palette still reads inside an effect that is nearly black.</summary>
    private static Color Pigment(Color c, float k) => new(c.R * k, c.G * k, c.B * k * 1.15f, 1f);

    /// <summary>
    /// The calm substitute for the four texture-set recipes when reduced motion is on: the cleared
    /// cells light up their own colour in place and shrink out over 160ms. Same information ("these
    /// cells, in these colours, popped"), zero travel, zero spin, zero strobe, nothing additive —
    /// deliberately the same bargain <see cref="DebrisField.EmitReducedFade"/> strikes for chunks.
    /// </summary>
    private void ReducedStill(bool rowLine, int index, Vector2 origin, float cell, int n, Func<int, Color> preClear)
    {
        for (int k = 0; k < n; k++)
        {
            var c = rowLine
                ? origin + new Vector2((k + 0.5f) * cell, (index + 0.5f) * cell)
                : origin + new Vector2((index + 0.5f) * cell, (k + 0.5f) * cell);
            Add(FxKind.Dot, c, Vector2.Zero, 0.16f, cell * 0.34f, 0, 0, 0, 0, 0, 0, false, preClear(k));
        }
    }

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

    /// <param name="aux">Kind-specific second dimension — see <see cref="FxSpark.Aux"/>.</param>
    /// <param name="swirl">Velocity curl, rad/s — see <see cref="FxSpark.Swirl"/>.</param>
    /// <param name="rot">Initial sprite angle. 999 (the default) means "random", the same
    /// sentinel <see cref="EmitAlong"/> uses for angBias; kinds whose Rot IS their direction
    /// (Spike) must pass a real angle or they would grow in a random direction.</param>
    private void Add(FxKind kind, Vector2 pos, Vector2 vel, float life, float size, float grav, float drag,
                     float spin, float flutter, float trail, float delay, bool additive, Color col,
                     float aux = 0f, float swirl = 0f, float rot = 999f)
    {
        if (_fx.Count >= MaxFx) return;
        _fx.Add(new FxSpark
        {
            Pos = pos, Vel = vel, Age = -delay, Life = life, Size = size, Grav = grav, Drag = drag,
            Spin = spin, Flutter = flutter, Trail = trail,
            Rot = rot > 900f ? _rng.RandfRange(0, Mathf.Tau) : rot,
            Seed = _rng.RandfRange(0, Mathf.Tau), Kind = kind, Additive = additive, Col = col,
            Aux = aux, Swirl = swirl,
        });
    }

    private void EmitAlong(bool rowLine, int index, Vector2 origin, float cell, int n, float budget,
        FxKind kind, int perCell, float spdMin, float spdMax, float lifeMin, float lifeMax,
        float sizeMinF, float sizeMaxF, float grav, float drag, float spinMin, float spinMax,
        float flutter, float trail, float delay, bool additive, Func<int, Color> colourFn,
        float angBias = 999f, float angSpread = Mathf.Pi, int stride = 1, float swirl = 0f)
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
                // Curl sign is randomised per particle: one shared sign would read as a conveyor
                // belt pushing everything the same way, which is the opposite of a swirl.
                float sw = swirl == 0f ? 0f : (_rng.Randf() < 0.5f ? swirl : -swirl);
                Add(kind, center, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                    _rng.RandfRange(lifeMin, lifeMax), size, grav, drag,
                    _rng.RandfRange(spinMin, spinMax), flutter, trail, delay, additive, col, swirl: sw);
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
                case FxKind.Blossom: DrawBlossom(ci, s, a); break;
                case FxKind.Shard: DrawShard(ci, s, a); break;
                case FxKind.Spike: DrawSpike(ci, s, t); break;
                case FxKind.Blot: DrawBlot(ci, s, t); break;
                case FxKind.Drip: DrawDrip(ci, s, a); break;
                case FxKind.Pixel: DrawPixel(ci, s, a); break;
                case FxKind.Slice: DrawSlice(ci, s, t); break;
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
                case FxKind.Slice: DrawSlice(ci, s, t); break;
                case FxKind.Pixel: DrawPixel(ci, s, a); break;
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

    /// <summary>
    /// PETALS' blossom petal. A real teardrop silhouette rather than <see cref="DrawPetal"/>'s
    /// quad, because the whole artifact is asking to be read as organic and a spinning rectangle
    /// never will be. The waist pinches with the tumble phase (edge-on → face-on) and the back
    /// face is shaded darker, so one polygon plus one line carries a surface catching the light.
    /// </summary>
    private void DrawBlossom(CanvasItem ci, FxSpark s, float a)
    {
        float e = Mathf.Abs(Mathf.Cos(s.Rot * 1.3f));         // 0 = edge-on, 1 = face-on
        float w = s.Size * (0.18f + 0.82f * e), len = s.Size * 1.5f;
        float cr = Mathf.Cos(s.Rot), sr = Mathf.Sin(s.Rot);
        for (int i = 0; i < 8; i++)
        {
            float x = PetalX[i] * w, y = PetalY[i] * len;
            S8[i] = s.Pos + new Vector2(x * cr - y * sr, x * sr + y * cr);
        }
        var face = e > 0.5f ? s.Col : new Color(s.Col.R * 0.62f, s.Col.G * 0.58f, s.Col.B * 0.62f);
        ci.DrawColoredPolygon(S8, new Color(face.R, face.G, face.B, a));
        // The midrib: tip (0) to base (4). Free structure — it also stops big petals reading flat.
        ci.DrawLine(S8[0], S8[4], new Color(1f, 1f, 1f, a * 0.22f), Mathf.Max(1f, s.Size * 0.10f));
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

    /// <summary>
    /// FROST's crystal blade. Rooted, so its whole animation is an envelope: it bites out of the
    /// line (cubic-out, no overshoot — ice does not bounce), holds, then SPLITS down its own spine
    /// as the snow takes over. The split is why this is two half-triangles instead of one: at
    /// gap = 0 they are exactly the blade they came from, so the break costs no extra state.
    /// </summary>
    private void DrawSpike(CanvasItem ci, FxSpark s, float t)
    {
        const float Grow = 0.42f, Split = 0.74f;
        float g = t < Grow ? 1f - Mathf.Pow(1f - t / Grow, 3f) : 1f;
        float a = Mathf.Min(1f, (1f - t) * 2.6f);              // holds bright, dies on the beat
        if (a <= 0.004f) return;
        var dir = new Vector2(Mathf.Cos(s.Rot), Mathf.Sin(s.Rot));
        var perp = new Vector2(-dir.Y, dir.X);
        float hw = s.Size * (0.55f + 0.45f * g);
        var tip = s.Pos + dir * Mathf.Max(1f, s.Aux * g);
        float gap = t > Split ? (t - Split) / (1f - Split) * s.Size * 1.6f : 0f;
        var core = new Color(s.Col.R, s.Col.G, s.Col.B, a * 0.85f);
        S3[0] = tip + perp * gap; S3[1] = s.Pos + perp * (hw + gap); S3[2] = s.Pos + perp * gap * 0.35f;
        ci.DrawColoredPolygon(S3, core);
        S3[0] = tip - perp * gap; S3[1] = s.Pos - perp * (hw + gap); S3[2] = s.Pos - perp * gap * 0.35f;
        ci.DrawColoredPolygon(S3, core);
        // The spine highlight — the only bright thing on a matte crystal, and the cue that reads
        // in grayscale (shape + luminance, never hue alone).
        ci.DrawLine(s.Pos, tip, new Color(1f, 1f, 1f, a * 0.6f), Mathf.Max(1f, s.Size * 0.16f));
    }

    /// <summary>
    /// INK's bloom. Spread is <c>1-e^-kt</c>: fast bite, asymptotic crawl — how pigment actually
    /// wicks into paper, and it means the blot never visibly "stops". Drawn twice: a lighter wet
    /// RIM (where a spreading blot concentrates its pigment) under an ink-dark core, which is what
    /// keeps the cleared cell's own hue readable inside an almost-black effect.
    /// </summary>
    private void DrawBlot(CanvasItem ci, FxSpark s, float t)
    {
        float r = s.Aux * (1f - Mathf.Exp(-4.2f * t));
        float a = Mathf.Min(1f, (1f - t) * 2.2f);
        if (a <= 0.004f || r <= 0.5f) return;
        for (int i = 0; i < 12; i++)
        {
            float ang = Mathf.Tau * i / 12f;
            // Ragged, but only just: kept near-convex so the ear-clip stays trivial.
            float rr = r * (0.86f + 0.28f * Hash01(s.Seed + i * 2.7f));
            S12[i] = s.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
        }
        ci.DrawColoredPolygon(S12, new Color(s.Col.R * 0.78f, s.Col.G * 0.78f, s.Col.B * 0.82f, a * 0.42f));
        for (int i = 0; i < 12; i++) S12[i] = s.Pos + (S12[i] - s.Pos) * 0.74f;
        ci.DrawColoredPolygon(S12, new Color(s.Col.R * 0.16f, s.Col.G * 0.16f, s.Col.B * 0.20f, a * 0.92f));
    }

    /// <summary>INK's run: the matte twin of <see cref="DrawComet"/>. Same speed-stretched tail,
    /// but normal-alpha and with a round head, because ink hanging off an edge is a teardrop —
    /// and because a glowing drip would contradict the one rule this artifact has (no emission).</summary>
    private void DrawDrip(CanvasItem ci, FxSpark s, float a)
    {
        float speed = s.Vel.Length();
        var dir = speed > 1f ? s.Vel / speed : Vector2.Down;
        var perp = new Vector2(-dir.Y, dir.X);
        var tail = s.Pos - dir * Mathf.Min(s.Size * 5f, speed * 0.09f);
        var col = new Color(s.Col.R, s.Col.G, s.Col.B, a);
        S4[0] = s.Pos + perp * s.Size * 0.9f; S4[1] = s.Pos - perp * s.Size * 0.9f;
        S4[2] = tail - perp * s.Size * 0.12f; S4[3] = tail + perp * s.Size * 0.12f;
        ci.DrawColoredPolygon(S4, col);
        ci.DrawCircle(s.Pos, s.Size * 0.9f, col);
    }

    /// <summary>
    /// GLITCH's tear band, and the only draw in the engine whose MOTION lives here rather than in
    /// the update loop (the slice is rooted — see <see cref="AddSlice"/>). Three channel copies at
    /// separate offsets on the additive surface sum back to the source colour where they overlap
    /// and leave pure R/G/B fringes where they don't: that IS chromatic aberration, for three
    /// DrawRect calls and no shader. Past the snap-back the block stops being a block and burns
    /// down into dropping scanlines.
    /// </summary>
    private void DrawSlice(CanvasItem ci, FxSpark s, float t)
    {
        float a = 1f - t;
        float halfW = s.Aux, halfH = s.Size;
        int step = (int)(s.Age * 45f);            // 45Hz re-roll — digital chatter, not a smooth ease
        float jit = Hash01(s.Seed + step) - 0.5f;
        var c = s.Col;

        if (t < 0.55f)
        {
            // Out over the first 12%, then drag back to exactly zero at 55%: the snap-back is the
            // beat that makes it read as a RECOVERED frame instead of a slab flying off.
            float slide = t < 0.12f ? t / 0.12f : 1f - (t - 0.12f) / 0.43f;
            float dir = Hash01(s.Seed) < 0.5f ? -1f : 1f;
            // BOTH displacements are measured in the band's THICKNESS (its short axis), never its
            // length. Every slice this engine emits is one cell thick that way, so a narrow band
            // and the line-wide dropout shear and separate by the same ~1.6 cells — which is both
            // how real datamoshing looks (rows all skip by similar amounts) and the only version
            // that stays on the board. Keyed off the long axis instead, a cleared COLUMN (a tall
            // thin band) threw its dropout half a board sideways and its R/B copies eight cells apart.
            float thick = Mathf.Min(halfW, halfH);
            float off = (dir * thick * 3.2f + jit * halfW * 0.12f) * slide;
            float split = thick * (0.5f + 1.7f * slide);
            float ca = a * 0.9f;
            Chan(ci, s.Pos + new Vector2(off - split, 0f), halfW, halfH, new Color(c.R, 0f, 0f, ca));
            Chan(ci, s.Pos + new Vector2(off, 0f), halfW, halfH, new Color(0f, c.G, 0f, ca));
            Chan(ci, s.Pos + new Vector2(off + split, 0f), halfW, halfH, new Color(0f, 0f, c.B, ca));
        }
        else
        {
            float f = (t - 0.55f) / 0.45f;
            for (int i = 0; i < 4; i++)
            {
                float h = Hash01(s.Seed + i * 3.7f);
                if (h < f) continue;                          // lines drop out as the signal dies
                float w = halfW * (0.25f + 0.75f * Hash01(s.Seed + i * 11.3f));
                float x = s.Pos.X + (Hash01(s.Seed + i * 5.1f + step) * 2f - 1f) * Mathf.Max(0f, halfW - w);
                float y = s.Pos.Y + (h * 2f - 1f) * halfH;
                ci.DrawRect(new Rect2(x - w, y - 1f, w * 2f, 2f), new Color(c.R, c.G, c.B, a * 0.8f));
            }
        }
    }

    private static void Chan(CanvasItem ci, Vector2 centre, float halfW, float halfH, Color col)
        => ci.DrawRect(new Rect2(centre.X - halfW, centre.Y - halfH, halfW * 2f, halfH * 2f), col);

    /// <summary>GLITCH's channel dust. Snapped to its own size grid and stepped to four alpha
    /// levels, because a dead pixel does not move smoothly and does not fade smoothly — the
    /// quantisation is the entire difference between this and a spark.</summary>
    private void DrawPixel(CanvasItem ci, FxSpark s, float a)
    {
        float q = Mathf.Max(2f, s.Size);
        float x = Mathf.Floor(s.Pos.X / q) * q, y = Mathf.Floor(s.Pos.Y / q) * q;
        float aq = Mathf.Ceil(a * 4f) / 4f;
        ci.DrawRect(new Rect2(x, y, q, q), new Color(s.Col.R, s.Col.G, s.Col.B, aq));
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
