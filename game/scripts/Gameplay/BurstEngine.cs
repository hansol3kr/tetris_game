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

    private const int MaxFx = 260;
    private const float FlutterFreq = 9f;

    private readonly List<FxSpark> _fx = new();
    private readonly List<FxRing> _rings = new();
    private readonly List<FxRibbon> _ribbons = new();
    private readonly List<FxBolt> _bolts = new();
    private readonly RandomNumberGenerator _rng = new();

    // Screen-space envelope (Supernova bleach + vignette; Lightning short flash).
    private float _novaAge = 9f, _novaPeak;
    private bool _novaVignette;

    private static readonly Color[] Party =
    {
        new(1f, 0.35f, 0.45f), new(1f, 0.8f, 0.3f), new(0.4f, 0.95f, 0.6f), new(0.35f, 0.8f, 1f),
        new(0.75f, 0.5f, 1f), new(1f, 0.55f, 0.85f), new(0.5f, 1f, 0.9f),
    };

    public bool Active => _fx.Count > 0 || _rings.Count > 0 || _ribbons.Count > 0 || _bolts.Count > 0 || _novaAge < 0.6f;

    public void Clear()
    {
        _fx.Clear(); _rings.Clear(); _ribbons.Clear(); _bolts.Clear();
        _novaAge = 9f; _novaPeak = 0f;
    }

    // ---- Integration --------------------------------------------------------

    public void Update(float dt)
    {
        if (_novaAge < 2f) _novaAge += dt;

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

    private void TriggerNova(float peak, bool vignette) { _novaAge = 0f; _novaPeak = peak; _novaVignette = vignette; }

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

    public void DrawNormal(CanvasItem ci, float cell, Rect2 screen)
    {
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

    public void DrawAdditive(CanvasItem ci, float cell, Rect2 screen)
    {
        // Screen bleach (Supernova / Lightning).
        if (_novaPeak > 0f && _novaAge < 0.6f && screen.Size.X > 0f)
        {
            float fa = _novaPeak * Mathf.Min(1f, _novaAge / 0.06f) * Mathf.Exp(-Mathf.Max(0f, _novaAge - 0.06f) / 0.12f);
            if (fa > 0.003f) ci.DrawRect(screen, new Color(1f, 0.98f, 0.9f, fa));
        }

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
                var pts = new Vector2[26]; var cols = new Color[26];
                for (int i = 0; i < 26; i++)
                {
                    float ang = Mathf.Tau * i / 25f;
                    pts[i] = rg.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    cols[i] = Color.FromHsv((i / 25f + rg.Age * 0.1f) % 1f, 0.85f, 1f, a);
                }
                ci.DrawPolylineColors(pts, cols, Mathf.Max(2f, rg.Width * (1f - t)));
            }
            else
            {
                ci.DrawArc(rg.Pos, rad, 0f, Mathf.Tau, 48, new Color(rg.Col.R, rg.Col.G, rg.Col.B, a * 0.9f), Mathf.Max(2f, rg.Width * (1f - t)));
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

    private static readonly ImageTexture Glow = TextureFactory.GlowDisc(48);

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
        var pts = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float rr = (i % 2 == 0) ? rp : rp * 0.34f;
            float ang = s.Rot + i * Mathf.Pi / 4f;
            pts[i] = s.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
        }
        ci.DrawColoredPolygon(pts, new Color(s.Col.R, s.Col.G, s.Col.B, a));
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
        var quad = new[] { s.Pos + perp * wH, s.Pos - perp * wH, tail - perp * wT, tail + perp * wT };
        ci.DrawColoredPolygon(quad, new Color(s.Col.R, s.Col.G, s.Col.B, a));
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
        ci.DrawColoredPolygon(new[] { R(-w, -hgt), R(w, -hgt), R(w, hgt), R(-w, hgt) }, new Color(face.R, face.G, face.B, a));
    }

    private void DrawShard(CanvasItem ci, FxSpark s, float a)
    {
        var dir = new Vector2(Mathf.Cos(s.Rot), Mathf.Sin(s.Rot));
        var perp = new Vector2(-dir.Y, dir.X);
        float L = s.Size * 2.2f, W = s.Size * 0.7f;
        var A = s.Pos + dir * L * 0.65f;
        var B = s.Pos - dir * L * 0.35f + perp * W;
        var C = s.Pos - dir * L * 0.35f - perp * W;
        ci.DrawColoredPolygon(new[] { A, B, C }, new Color(s.Col.R, s.Col.G, s.Col.B, a));
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
        var pts = new Vector2[12]; var cols = new Color[12];
        for (int i = 0; i < 12; i++)
        {
            pts[i] = rb.Base + new Vector2(Mathf.Sin(rb.Age * 2f + i * 0.6f + rb.Seed) * rb.Cell * 0.6f, -i * rb.Cell * 0.7f);
            float hue = (rb.Hue + i * 0.03f + rb.Age * 0.1f) % 1f;
            cols[i] = Color.FromHsv(hue, 0.7f, 1f, baseA * (1f - i / 12f));
        }
        ci.DrawPolylineColors(pts, cols, Mathf.Max(2f, rb.Cell * 0.35f));
    }

    private void DrawBolt(CanvasItem ci, FxBolt b)
    {
        float a = 1f - b.Age / b.Life;
        int segs = 8;
        var pts = new Vector2[segs + 1];
        int jitter = (int)(b.Age * 40f);                          // re-jitter a few times → buzz
        var dir = b.B - b.A;
        var perp = new Vector2(-dir.Y, dir.X).Normalized();
        float cell = dir.Length() / segs;
        for (int i = 0; i <= segs; i++)
        {
            float f = i / (float)segs;
            float off = (i == 0 || i == segs) ? 0f : (Hash01(b.Seed + i * 7 + jitter) - 0.5f) * cell * 0.8f;
            pts[i] = b.A + dir * f + perp * off;
        }
        ci.DrawPolyline(pts, new Color(0.3f, 0.85f, 1f, a * 0.35f), cell * 0.30f);
        ci.DrawPolyline(pts, new Color(1f, 1f, 1f, a), cell * 0.10f);
    }

    private static float Hash01(float x)
    {
        float s = Mathf.Sin(x * 127.1f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}

/// <summary>A Node2D whose only job is to draw a <see cref="BurstEngine"/>'s additive (glow)
/// half with a BlendMode.Add material, so bright particles sum toward white (SDR bloom without
/// HDR). Shared by the Block Fit board and the store's ArtifactPreview.</summary>
public sealed partial class AdditiveFxLayer : Node2D
{
    private readonly BurstEngine _engine;
    private readonly Func<float> _cell;
    private readonly Func<Rect2> _screen;

    public AdditiveFxLayer(BurstEngine engine, Func<float> cell, Func<Rect2> screen)
    {
        _engine = engine; _cell = cell; _screen = screen;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    public override void _Draw() => _engine.DrawAdditive(this, _cell(), _screen());
}
