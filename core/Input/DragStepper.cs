namespace Blockfall.Core;

/// <summary>
/// Turns a touch DRAG into the deterministic button stream. Direct-manipulation
/// touch ("grab the piece and slide it into place") wants precise, one-cell-at-a-time
/// horizontal movement — not the held-key auto-shift (DAS/ARR) that a d-pad produces.
///
/// A drag reports "the finger crossed N cell boundaries"; each of those must land as
/// a SEPARATE single-cell tap. But <see cref="InputProcessor"/> reads DAS from the
/// held Left/Right bits, so two Left bits on adjacent ticks look like ONE held press
/// (the second charges DAS instead of stepping). To keep every step a distinct tap,
/// this emits <c>move, gap, move, gap …</c>: the empty gap tick lets the processor
/// release the direction so the next bit is a fresh press. That caps drag stepping at
/// 30 cells/sec — plenty for a finger — and, crucially, keeps the whole thing inside
/// the recorded <see cref="Buttons"/> stream, so replays and netcode reproduce a
/// drag-played run exactly, bit-for-bit.
/// </summary>
public sealed class DragStepper
{
    private int _pending;   // signed net cells still to emit (- left, + right)
    private bool _gap;      // the next tick is the release gap between two steps

    /// <summary>Queue a horizontal step request (usually ±1 per cell the finger crossed).</summary>
    public void Queue(int cells) => _pending += cells;

    /// <summary>Drop any queued steps (call when the drag ends or the piece locks).</summary>
    public void Clear() { _pending = 0; _gap = false; }

    /// <summary>
    /// Force the next emitted step to be preceded by a release gap. The sampler calls
    /// this on any tick another source already holds a horizontal direction, so when
    /// that hold ends the first drag step is still seen as a fresh tap (not a DAS
    /// continuation of the released direction) — it costs one tick, never a lost step.
    /// </summary>
    public void ForceGap() => _gap = true;

    public bool HasPending => _pending != 0;

    /// <summary>
    /// The horizontal bit to OR into this tick's mask: exactly one of
    /// <see cref="Buttons.None"/> / <see cref="Buttons.Left"/> / <see cref="Buttons.Right"/>.
    /// Advances the internal step/gap state.
    /// </summary>
    public Buttons Next()
    {
        if (_pending == 0) { _gap = false; return Buttons.None; }
        if (_gap) { _gap = false; return Buttons.None; } // release tick — makes the next step a fresh tap
        _gap = true;
        if (_pending > 0) { _pending--; return Buttons.Right; }
        _pending++; return Buttons.Left;
    }
}
