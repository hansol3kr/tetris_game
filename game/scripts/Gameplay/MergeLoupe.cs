using Godot;
using Blockfall.Core.BlockFit;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// The merge magnifier, shared verbatim by BOTH Block Fit screens (solo
/// <see cref="BlockFitController"/> and the duel <see cref="BlockFitVersusController"/>).
///
/// WHY IT EXISTS. Fusing two tray pieces is the one cell-precise act in Block Fit that the
/// player cannot see themselves perform: board placement is WYSIWYG (the piece is drawn on the
/// cells it will occupy), but a tray cell is at most 0.55x a board cell, and the whole verdict
/// — hot outline, green/red cells — lands under the fingertip that is causing it. A thumb pad
/// is about 9 mm across; the solo 1x1 merge rect is 5.4 mm and the duel one 4.1 mm. So the
/// confirmation is drawn AWAY from the hand, magnified, or it is not a confirmation at all.
///
/// WHY IT IS SHARED. The duel had no magnifier at all while carrying the SMALLER target — the
/// screen with the clock running had the least feedback. Two copies of this drawing would drift;
/// one function cannot. Every geometry input is a parameter, so each screen passes its own
/// board/tray metrics and gets the identical panel.
///
/// WHERE IT PARKS. Bottom-anchored 16px above <paramref name="trayTop"/> and grown UPWARDS, with
/// the vertical room capped at <see cref="MaxBoardFrac"/> of the board-to-tray gap. That cap is
/// the guarantee: the panel covers only the LOWER ~60% of the player's own board and leaves the
/// top 40% readable — never the tray it is describing, never the exit strip below it, and (in the
/// duel) never the opponent's board or the SENT/INCOMING callout above it. It is deliberately not
/// opaque either: the player still has to answer "does the fused shape fit down there?" while the
/// panel is up, and an earlier revision that ate 88% of the board opaquely made that impossible.
/// Static drawing only, so no <see cref="Motion.Reduced"/> gate is owed; the block cells still
/// forward <c>reduced</c> to <see cref="BlockRender"/> so materials stop breathing.
/// </summary>
internal static class MergeLoupe
{
    /// <summary>Padding, in loupe cells, around the union of the two pieces so the join reads
    /// against empty space instead of butting the panel edge.</summary>
    private const int Margin = 1;

    /// <summary>Below this multiple of a tray cell the panel is not a magnification, just a
    /// second small picture — skip it rather than add clutter.</summary>
    private const float MinZoom = 1.10f;

    /// <summary>Largest share of the board-to-tray gap the panel may occupy, measured across the
    /// four safe canvases we ship. At 0.62 the panel eats 60% of the gap in every union size and
    /// the top 40% of the board — where a fused shape usually has to land — stays readable.
    ///
    /// The pair (MaxBoardFrac, MinZoom) is load-bearing and must be tuned together: tightening to
    /// 0.56 while MinZoom stayed 1.25 pushed the worst union (9x9) down to 1.21x, under the guard,
    /// which made the loupe VANISH exactly when the shape is hardest to read. Losing the instrument
    /// is worse than covering the board, so the cap was relaxed and the guard lowered in step.
    /// Worst measured magnification after the change: 1.36x solo, 1.20x versus — still a real zoom.</summary>
    private const float MaxBoardFrac = 0.62f;

    /// <summary>
    /// Draws the magnifier for a live merge drag.
    /// </summary>
    /// <param name="ci">Canvas to draw on (the controller itself).</param>
    /// <param name="src">Piece in hand.</param>
    /// <param name="dst">Piece being fused into, anchored at (0,0).</param>
    /// <param name="mr">Row offset of <paramref name="src"/> relative to <paramref name="dst"/>.</param>
    /// <param name="mc">Column offset of <paramref name="src"/> relative to <paramref name="dst"/>.</param>
    /// <param name="verdict">Why the fuse is (not) legal. Three states, not two: OK is green,
    /// WontFit is red ("move your finger"), and NoCharges is amber PLUS a slash struck across the
    /// panel ("no finger movement will help — the budget is spent"). The state is carried by
    /// colour, by cell-fill opacity AND by that slash, so it is never a hue-only signal.</param>
    /// <param name="safe">Safe canvas size (the panel is centred in it).</param>
    /// <param name="boardTop">Top edge of the player's board — the ceiling the panel may not cross.</param>
    /// <param name="trayTop">Top edge of the tray band — the panel's floor.</param>
    /// <param name="trayCell">Tray mini-cell size, the thing being magnified.</param>
    /// <param name="shimmer">Accumulated dt driving the shared material breathe.</param>
    internal static void Draw(CanvasItem ci, BlockPiece src, BlockPiece dst, int mr, int mc, MergeVerdict verdict,
                              Vector2 safe, float boardTop, float trayTop, float trayCell, float shimmer)
    {
        if (trayCell <= 0f) return;
        bool ok = verdict == MergeVerdict.Ok;
        bool spent = verdict == MergeVerdict.NoCharges;

        // Union bounding box of dst (at 0,0) + src (at the finger-chosen offset), padded by Margin.
        int r0 = Mathf.Min(0, mr) - Margin, c0 = Mathf.Min(0, mc) - Margin;
        int rows = Mathf.Max(dst.Height, mr + src.Height) - r0 + Margin;
        int cols = Mathf.Max(dst.Width, mc + src.Width) - c0 + Margin;
        if (rows <= 0 || cols <= 0) return;

        // Coverage budget. The loupe is an INSTRUMENT, not an overlay: while it is up the player
        // still has to read the board to answer "does the fused shape actually fit down there?".
        // Letting it grow into the whole board (measured: 88% of height / 85% of width on a 9x9
        // union) makes that impossible, so cap the footprint and let the board read through the
        // backing. MaxBoardFrac is the ceiling on how much board height it may eat.
        float roomY = (trayTop - boardTop) * MaxBoardFrac - 28f;
        float lc = Mathf.Floor(Mathf.Min(Mathf.Min(safe.X * 0.66f / cols, roomY / rows), trayCell * 3.2f));
        if (lc <= trayCell * MinZoom) return;   // not enough room to be a meaningful magnification

        float w = cols * lc, h = rows * lc;
        var origin = new Vector2((safe.X - w) / 2f, trayTop - 16f - h);

        // Backing panel — border colour doubles as the legal/illegal verdict at a glance.
        // Alpha is deliberately short of opaque: the cells and lattice below are drawn on top and
        // stay legible, while the board behind still shows through enough to judge the fit.
        var pad = new Vector2(10, 10);
        var panel = new Rect2(origin - pad, new Vector2(w, h) + pad * 2f);
        var accent = ok ? new Color(0.35f, 1f, 0.60f)
                   : spent ? Palette.AccentGold
                   : Palette.AccentRed;
        ci.DrawRect(panel, new Color(0.04f, 0.05f, 0.10f, 0.62f), filled: true);
        ci.DrawRect(panel, new Color(accent.R, accent.G, accent.B, 0.75f), filled: false, width: 2.5f);

        var glyph = Palette.EquippedGlyph;
        var mat = Palette.EquippedMaterial;
        bool reduced = Motion.Reduced;

        // Faint lattice so the gaps around the join are countable.
        for (int rr = 0; rr < rows; rr++)
            for (int cc = 0; cc < cols; cc++)
                ci.DrawRect(new Rect2(origin + new Vector2(cc * lc, rr * lc) + new Vector2(1, 1), new Vector2(lc - 2, lc - 2)),
                            new Color(1, 1, 1, 0.05f), filled: false, width: 1f);

        // Destination piece, rendered with the equipped skin so the preview matches the board.
        foreach (var (dr, dc) in dst.Cells)
        {
            var rect = new Rect2(origin + new Vector2((dc - c0) * lc, (dr - r0) * lc) + new Vector2(1, 1), new Vector2(lc - 2, lc - 2));
            BlockRender.DrawCell(ci, rect, lc, dst.Color, 1f, mat, glyph, shimmer, dr + dc, reduced: reduced);
        }

        // Source piece at the finger-chosen offset.
        foreach (var (dr, dc) in src.Cells)
        {
            var rect = new Rect2(origin + new Vector2((dc + mc - c0) * lc, (dr + mr - r0) * lc) + new Vector2(1, 1), new Vector2(lc - 2, lc - 2));
            ci.DrawRect(rect, new Color(accent.R, accent.G, accent.B, ok ? 0.90f : 0.45f), filled: true);
            ci.DrawRect(rect, accent, filled: false, width: 2f);
        }

        // Budget spent: strike the whole panel through. This is the SHAPE channel of the "no
        // charges" answer — the one refusal the player cannot fix by aiming better, so it has to
        // look categorically different from the red "won't fit" and not merely a different hue.
        // Static geometry, no Motion.Reduced gate owed.
        if (spent)
        {
            var a = panel.Position + new Vector2(6f, 6f);
            var b = panel.Position + panel.Size - new Vector2(6f, 6f);
            ci.DrawLine(a, b, new Color(accent.R, accent.G, accent.B, 0.85f), width: 4f);
        }
    }
}
