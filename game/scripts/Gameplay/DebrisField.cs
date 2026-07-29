using Godot;
using System;
using System.Collections.Generic;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// The physical-destruction half of a line clear: every cleared cell first CRACKS in place,
/// then bursts into rigid chunks that tumble, bounce off the board walls, land on a shelf
/// above the tray and fade. Particles say "sparkle"; this says "you broke something".
///
/// Design rules it is built to honour:
///   • HARD CAP. A fixed <see cref="MaxDebrisAbs"/> struct pool, never resized, never
///     allocated per frame. A five-line clear thins the outer cells instead of flooding.
///   • CONCENTRATION, NOT DILUTION. Cells nearest the strike get four chunks, the rest get
///     one — so a big clear reads as a denser blast core, not a wider haze.
///   • REDUCED MOTION. <see cref="Motion.Reduced"/> refuses every launch at the single gate
///     in <see cref="EmitLine"/>; <see cref="EmitReducedFade"/> is the calm substitute (a 140ms
///     flat fade in place — no travel, no spin, no shadow).
///   • COLOURBLIND SAFETY. "It shattered" is carried by SHAPE (crack lines, silhouette), by
///     the baked bevel LUMINANCE ramp, and by a drop shadow — never by hue alone. Fills come
///     from <c>Palette</c>, so the Okabe–Ito override applies for free.
///   • DETERMINISM. View-only, own <see cref="RandomNumberGenerator"/>, frame-rate-independent
///     damping (<c>Vel *= Exp(-Drag*dt)</c>), never reads the game clock or RNG.
///
/// Owned and pumped by <see cref="BurstEngine"/> — callers get it for free through the
/// existing Update/DrawNormal/DrawAdditive calls. A host that never calls
/// <see cref="SetBounds"/> gets the conservative derived box instead: walls and floor taken
/// from the board itself, so chunks physically cannot reach a tray slot or the exit button,
/// and landed chunks fade immediately rather than loitering over a drop target.
/// </summary>
public sealed class DebrisField
{
    /// <summary>Pool size — the absolute ceiling on live chunks (desktop). Mobile clamps
    /// lower at emit time. Chosen against the BurstEngine.MaxFx=260 precedent.</summary>
    public const int MaxDebrisAbs = 96;

    private const float CrackT = 0.090f;     // stuck-in-place crack stage
    private const float HardLife = 1.60f;    // hard cut, resting or not
    private const float Grav = 1400f;        // ≈0.31s to fall one 67px cell
    private const float Drag = 1.6f;
    private const float RestFadeAt = 0.50f;
    private const float RestFadeLen = 0.35f;
    private const float RestAlphaCap = 0.50f; // resting debris must not look grabbable
    private const int ShardPx = 40;

    private struct Chunk
    {
        public Vector2 Pos, Vel, Home;
        public float Rot, AngVel, Size, Age, RestT, CellSize, Bounce;
        public Color Col;
        public byte Variant, Bounces;
        public bool Additive, Resting, Launched, Lead, Glint, Flat;
    }

    /// <summary>Per-material rigid-body feel. This table is the payoff of the material axis:
    /// a wood skin splinters dully, a gem skin ricochets, a neon tube scatters glowing arcs.</summary>
    private readonly struct MatPhys
    {
        public readonly float Restitution, Spin;
        /// <summary>The silhouettes this material is allowed to shed — an explicit SET, not a
        /// lo..hi range. The range encoding this replaces silently swallowed whatever shape
        /// happened to sit between its ends, which is exactly how the fur board ended up
        /// throwing hard scale crescents (variant 11 sat inside Pelt's 10..11).</summary>
        public readonly byte[] Vars;
        public readonly bool Additive, Glint;
        public MatPhys(float e, float spin, byte[] vars, bool additive = false, bool glint = false)
        { Restitution = e; Spin = spin; Vars = vars; Additive = additive; Glint = glint; }
    }

    // Shared silhouette sets (see TextureFactory.CellShard for the shapes). Static, so the
    // emit path indexes an existing array and never allocates.
    private static readonly byte[] VarWedges = { 0, 1, 2, 3 };     // generic broken solid
    private static readonly byte[] VarSplinters = { 4, 5, 6, 7 };  // long grain fibres
    private static readonly byte[] VarArcs = { 8, 9 };             // hollow glass tube
    private static readonly byte[] VarFur = { 10, 12 };            // soft clump + soft wisp
    private static readonly byte[] VarScales = { 11 };             // one crescent plate
    private static readonly byte[] VarShell = { 13, 14 };          // ridged scute + sliver

    // Indexed by (int)CellMaterial — keep in lockstep with the enum (append-only).
    private static readonly MatPhys[] Phys =
    {
        new(0.28f, 6f, VarWedges),                       // Gel
        new(0.30f, 7f, VarWedges),                       // Pearl
        new(0.42f, 12f, VarWedges, glint: true),         // Metallic
        new(0.35f, 9f, VarWedges),                       // Holographic
        new(0.18f, 4f, VarSplinters),                    // Frosted
        new(0.50f, 14f, VarWedges, glint: true),         // Gemstone
        new(0.30f, 7f, VarWedges),                       // Starfield
        new(0.22f, 5f, VarSplinters),                    // Wood
        new(0.45f, 10f, VarArcs, additive: true),        // NeonTube
        new(0.10f, 3f, VarFur),                          // Pelt   — barely bounces, it lies down
        new(0.55f, 11f, VarScales),                      // Scale  — flat plates skid off the walls
        new(0.62f, 14f, VarShell, glint: true),          // Chitin — hard shell, it pings away
    };

    // Resolved once per variant. TextureFactory's cache is keyed by an interpolated string,
    // so calling it per chunk per frame would allocate ~96 strings a frame — exactly the GC
    // pressure this pass was built to avoid.
    private static readonly ImageTexture[] ShardTex = new ImageTexture[15];
    private static ImageTexture Shard(int variant)
        => ShardTex[variant] ??= TextureFactory.CellShard(ShardPx, variant);

    private readonly Chunk[] _pool = new Chunk[MaxDebrisAbs];
    private int _live;
    private readonly RandomNumberGenerator _rng = new();

    private Rect2 _bounds;
    private float _floorY;
    private bool _hostArmed;                 // true once a host called SetBounds
    private Vector2 _strike;
    private bool _hasStrike;

    // Scratch for the distance sort — never resized, never allocated per emit.
    private readonly int[] _order = new int[256];
    private readonly float[] _dist = new float[256];
    private readonly int[] _cellR = new int[256];
    private readonly int[] _cellC = new int[256];

    public bool Active => _live > 0;

    /// <summary>
    /// Arm the field with a HOST-OWNED play box. <paramref name="bounds"/> is the wall box and
    /// <paramref name="floorY"/> is the shelf chunks come to rest on — set it ABOVE the tray
    /// slots and the exit button so settled debris can never sit on a drag target. Arming also
    /// unlocks the full rest dwell (chunks linger, then fade); see <see cref="DeriveBounds"/>
    /// for the conservative default used when nobody arms the field.
    /// </summary>
    public void SetBounds(Rect2 bounds, float floorY)
    {
        if (bounds.Size.X <= 1f || bounds.Size.Y <= 1f) { _hostArmed = false; return; }
        _bounds = bounds; _floorY = floorY; _hostArmed = true;
    }

    /// <summary>
    /// The safe fallback when no host armed the field: walls are the board box grown 40px
    /// sideways, and the floor is the board panel's bottom lip. Chunks therefore never travel
    /// below the board — they cannot reach the tray slots or the exit button — and, because
    /// this box is not a host-chosen shelf, landed chunks skip the rest dwell and fade out
    /// immediately instead of loitering over a drop target.
    /// </summary>
    private void DeriveBounds(Vector2 origin, float cell, int n)
    {
        if (_hostArmed) return;
        float span = cell * n;
        _bounds = new Rect2(origin.X - 40f, origin.Y - cell, span + 80f, span + cell + 12f);
        _floorY = origin.Y + span + 6f;
    }

    /// <summary>
    /// Bias the blast core toward a point (the piece the player just placed) instead of the
    /// line centre. It is NOT consumed by the first line: a multi-line clear fires one
    /// <see cref="EmitLine"/> per line inside the same frame, and consuming would leave every
    /// line after the first blasting from its own centre — the cross-shaped double clear, the
    /// most satisfying placement in the mode, would visibly disagree with itself. The strike is
    /// re-set on every clearing placement, so it can never go stale; only <see cref="Clear"/>
    /// (a new run) drops it.
    /// </summary>
    public void SetStrike(Vector2 p) { _strike = p; _hasStrike = true; }

    public void Clear() { _live = 0; _hasStrike = false; }

    /// <summary>Sweep settled debris immediately — called when a drag starts so nothing
    /// competes with the piece the finger is carrying.</summary>
    public void ClearResting()
    {
        for (int i = _live - 1; i >= 0; i--)
            if (_pool[i].Resting) RemoveAt(i);
    }

    private void RemoveAt(int i) { _pool[i] = _pool[--_live]; }

    // ---- Emit ---------------------------------------------------------------

    /// <summary>One cleared line's worth of debris, addressed by line index — the path
    /// <see cref="BurstEngine.EmitLine"/> drives automatically.</summary>
    public void EmitLine(bool rowLine, int index, Vector2 origin, float cell, int n,
                         float budget, Func<int, Color> preClear, CellMaterial mat)
    {
        if (Motion.Reduced || cell <= 0f || n <= 0) return;
        DeriveBounds(origin, cell, n);
        int count = Math.Min(n, _cellR.Length);
        for (int k = 0; k < count; k++)
        {
            _cellR[k] = rowLine ? index : k;
            _cellC[k] = rowLine ? k : index;
        }
        var lineCentre = rowLine
            ? origin + new Vector2(n * 0.5f * cell, (index + 0.5f) * cell)
            : origin + new Vector2((index + 0.5f) * cell, n * 0.5f * cell);
        var axis = rowLine ? new Vector2(1, 0) : new Vector2(0, 1);
        EmitCore(count, origin, cell, _hasStrike ? _strike : lineCentre, axis, mat, budget,
                 k => preClear(rowLine ? _cellC[k] : _cellR[k]));
    }

    /// <summary>
    /// The reduced-motion substitute: the cleared cells stay put and fade out over 140ms.
    /// Same information ("these cells popped, in these colours"), zero travel/spin/scale.
    /// </summary>
    public void EmitReducedFade(IReadOnlyList<(int R, int C)> cells, Vector2 boardOrigin, float cell,
                                Func<int, int, Color> preClear)
    {
        if (cell <= 0f) return;
        foreach (var (r, c) in cells)
        {
            if (_live >= MaxDebrisAbs) return;
            var centre = boardOrigin + new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell);
            _pool[_live++] = new Chunk
            {
                Pos = centre, Home = centre, CellSize = cell, Size = cell, Col = preClear(r, c),
                Flat = true, Lead = true, Launched = true, Resting = false,
            };
        }
    }

    private void EmitCore(int count, Vector2 origin, float cell, Vector2 strike, Vector2 axis,
                          CellMaterial mat, float budget, Func<int, Color> colourAt)
    {
        var ph = Phys[Math.Clamp((int)mat, 0, Phys.Length - 1)];
        float juice = JuiceScale();
        if (juice <= 0.01f) return;
        int cap = Mathf.Min(MaxDebrisAbs, Mathf.RoundToInt((OS.HasFeature("mobile") ? 64 : 96) * juice));

        // Sort cells by distance to the strike (insertion sort — count is a board span).
        for (int k = 0; k < count; k++)
        {
            var centre = origin + new Vector2((_cellC[k] + 0.5f) * cell, (_cellR[k] + 0.5f) * cell);
            _dist[k] = (centre - strike).LengthSquared();
            _order[k] = k;
        }
        for (int i = 1; i < count; i++)
        {
            int key = _order[i]; float d = _dist[key]; int j = i - 1;
            while (j >= 0 && _dist[_order[j]] > d) { _order[j + 1] = _order[j]; j--; }
            _order[j + 1] = key;
        }

        int tierA = Mathf.Max(1, Mathf.RoundToInt(4f * budget * juice));   // blast core
        var perp = axis.LengthSquared() > 0.01f ? new Vector2(-axis.Y, axis.X).Normalized() : Vector2.Zero;

        for (int i = 0; i < count; i++)
        {
            int k = _order[i];
            int n = i < 16 ? tierA : 1;
            var centre = origin + new Vector2((_cellC[k] + 0.5f) * cell, (_cellR[k] + 0.5f) * cell);
            var col = colourAt(k);
            var impulse = (centre - strike);
            impulse = impulse.LengthSquared() > 1f ? impulse.Normalized() * 90f : Vector2.Zero;

            for (int s = 0; s < n; s++)
            {
                if (_live >= cap) return;
                float ang;
                if (perp == Vector2.Zero) ang = _rng.RandfRange(0f, Mathf.Tau);
                else
                {
                    var baseDir = _rng.Randf() < 0.5f ? perp : -perp;
                    ang = Mathf.Atan2(baseDir.Y, baseDir.X) + _rng.RandfRange(-0.55f, 0.55f);
                }
                float spd = _rng.RandfRange(120f, 320f);
                _pool[_live++] = new Chunk
                {
                    Pos = centre,
                    Home = centre,
                    Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd + impulse,
                    Rot = _rng.RandfRange(0f, Mathf.Tau),
                    AngVel = _rng.RandfRange(-ph.Spin, ph.Spin),
                    Size = cell * _rng.RandfRange(0.34f, 0.50f),
                    CellSize = cell,
                    Bounce = ph.Restitution,
                    Col = col,
                    Variant = ph.Vars[_rng.RandiRange(0, ph.Vars.Length - 1)],
                    Additive = ph.Additive,
                    Glint = ph.Glint,
                    Lead = s == 0,
                };
            }
        }
    }

    private static float JuiceScale()
    {
        var b = Bootstrap.Instance;
        return GodotObject.IsInstanceValid(b) ? Mathf.Clamp(b.Save.Settings.JuiceIntensity, 0f, 1f) : 1f;
    }

    // ---- Integration --------------------------------------------------------

    public void Integrate(float dt)
    {
        if (_live == 0) return;
        for (int i = _live - 1; i >= 0; i--)
        {
            ref var ch = ref _pool[i];
            ch.Age += dt;

            if (ch.Flat) { if (ch.Age >= 0.14f) RemoveAt(i); continue; }
            if (ch.Age >= HardLife) { RemoveAt(i); continue; }
            if (ch.Age < CrackT) continue;                 // still cracking in place
            ch.Launched = true;

            if (!ch.Resting)
            {
                ch.Vel.Y += Grav * dt;
                ch.Vel *= Mathf.Exp(-Drag * dt);           // frame-rate-independent damping
                ch.Pos += ch.Vel * dt;
                ch.Rot += ch.AngVel * dt;

                float half = ch.Size * 0.5f;
                if (ch.Pos.X - half < _bounds.Position.X || ch.Pos.X + half > _bounds.End.X)
                {
                    ch.Pos.X = Mathf.Clamp(ch.Pos.X, _bounds.Position.X + half, _bounds.End.X - half);
                    ch.Vel.X = -ch.Vel.X * ch.Bounce;
                    ch.Vel.Y *= 0.65f;
                    ch.AngVel = -ch.AngVel;
                    ch.Bounces++;
                }
                if (ch.Pos.Y + half >= _floorY)
                {
                    ch.Pos.Y = _floorY - half;
                    ch.Vel.Y = -Mathf.Abs(ch.Vel.Y) * ch.Bounce;
                    ch.Vel.X *= 0.65f;
                    ch.AngVel = -ch.AngVel;
                    ch.Bounces++;
                }
                if (ch.Bounces >= 2 || ch.Vel.Length() < 40f)
                {
                    ch.Resting = true;
                    ch.Pos.Y = _floorY - ch.Size * 0.5f;
                    ch.Vel = Vector2.Zero;
                    // Only a host-chosen shelf earns the dwell. On the derived (in-board)
                    // floor a chunk starts fading the instant it lands, so nothing loiters
                    // on top of a cell the player is about to drop into.
                    if (!_hostArmed) ch.RestT = RestFadeAt;
                }
            }
            else
            {
                ch.RestT += dt;
                // Spin bleeds off over 150ms rather than snapping to zero.
                ch.AngVel = Mathf.Lerp(ch.AngVel, 0f, Mathf.Min(1f, dt / 0.15f));
                ch.Rot += ch.AngVel * dt;
                if (ch.RestT > RestFadeAt) ch.Pos.Y += 6f * dt / RestFadeLen;
                if (ch.RestT >= RestFadeAt + RestFadeLen) { RemoveAt(i); continue; }
            }
        }
    }

    private static float AlphaOf(in Chunk ch)
    {
        if (ch.Flat) return 0.45f * Mathf.Clamp(1f - ch.Age / 0.14f, 0f, 1f);
        float a = Mathf.Clamp((HardLife - ch.Age) / 0.25f, 0f, 1f);
        if (ch.Resting)
        {
            a = Mathf.Min(a, RestAlphaCap);
            if (ch.RestT > RestFadeAt) a *= Mathf.Clamp(1f - (ch.RestT - RestFadeAt) / RestFadeLen, 0f, 1f);
        }
        return a;
    }

    // ---- Draw ---------------------------------------------------------------

    /// <summary>Normal-alpha pass: crack stage, solid chunks, drop shadows.</summary>
    public void DrawNormal(CanvasItem ci)
    {
        for (int i = 0; i < _live; i++)
        {
            ref readonly var ch = ref _pool[i];
            if (ch.Flat) { DrawFlat(ci, in ch); continue; }
            if (!ch.Launched) { if (ch.Lead) DrawCrack(ci, in ch); continue; }
            if (ch.Additive) continue;
            DrawChunk(ci, in ch);
        }
    }

    /// <summary>Additive pass — glowing debris (NeonTube arcs) sum toward white.</summary>
    public void DrawAdditive(CanvasItem ci)
    {
        for (int i = 0; i < _live; i++)
        {
            ref readonly var ch = ref _pool[i];
            if (ch.Flat || !ch.Launched || !ch.Additive) continue;
            DrawChunk(ci, in ch);
        }
    }

    private static void DrawFlat(CanvasItem ci, in Chunk ch)
    {
        float a = AlphaOf(in ch);
        if (a <= 0.002f) return;
        float h = ch.CellSize * 0.5f - 1f;
        ci.DrawRect(new Rect2(ch.Home - new Vector2(h, h), new Vector2(h * 2f, h * 2f)),
                    new Color(ch.Col.R, ch.Col.G, ch.Col.B, a));
    }

    /// <summary>Stage one: the cell swells 1.00→1.05 in place while white fracture lines open
    /// across it. Shape-only information, so it survives a grayscale read.</summary>
    private static void DrawCrack(CanvasItem ci, in Chunk ch)
    {
        float t = Mathf.Clamp(ch.Age / CrackT, 0f, 1f);
        float h = ch.CellSize * 0.5f * Mathf.Lerp(1.00f, 1.05f, t) - 1f;
        ci.DrawRect(new Rect2(ch.Home - new Vector2(h, h), new Vector2(h * 2f, h * 2f)),
                    new Color(ch.Col.R, ch.Col.G, ch.Col.B, 1f));
        var white = new Color(1, 1, 1, 0.55f * t);
        for (int k = 0; k < 3; k++)
        {
            float ang = ch.Variant * 0.55f + k * 1.1f;
            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * h;
            var off = new Vector2(-d.Y, d.X) * (k - 1) * 0.22f;
            ci.DrawLine(ch.Home + off - d, ch.Home + off + d, white, 1.5f);
        }
    }

    private static void DrawChunk(CanvasItem ci, in Chunk ch)
    {
        float a = AlphaOf(in ch);
        if (a <= 0.002f) return;
        // Tumble lighting: rotation modulates brightness for Metallic/Gemstone. No extra draw.
        if (ch.Glint) a *= 0.72f + 0.28f * Mathf.Abs(Mathf.Cos(ch.Rot));

        var tex = Shard(ch.Variant);
        float h = ch.Size * 0.5f;
        var quad = new Rect2(-h, -h, ch.Size, ch.Size);

        // Silhouette shadow while airborne — the third (contrast) channel that makes the
        // break readable with the hue stripped out. Skipped once resting (1 draw call).
        if (!ch.Resting && ch.Vel.Length() > 60f)
        {
            float o = ch.CellSize * 0.09f;
            ci.DrawSetTransform(ch.Pos + new Vector2(o, o), ch.Rot, Vector2.One);
            ci.DrawTextureRect(tex, quad, false, new Color(0, 0, 0, 0.30f * a));
        }

        ci.DrawSetTransform(ch.Pos, ch.Rot, Vector2.One);
        ci.DrawTextureRect(tex, quad, false, new Color(ch.Col.R, ch.Col.G, ch.Col.B, a));
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
