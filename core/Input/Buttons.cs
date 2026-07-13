namespace Blockfall.Core;

/// <summary>
/// The raw per-tick button state — one bit per control. This is the atom of the
/// deterministic input model: replays store a stream of these, and lockstep
/// netcode exchanges them. Because DAS/ARR live in <see cref="InputProcessor"/>
/// (fixed-tick), feeding the same Buttons stream to the same seed reproduces the
/// exact same game, bit-for-bit, on any platform.
/// </summary>
[System.Flags]
public enum Buttons : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Soft = 1 << 2,   // soft drop (held)
    Hard = 1 << 3,   // hard drop (edge)
    RotateCw = 1 << 4,
    RotateCcw = 1 << 5,
    Rotate180 = 1 << 6,
    Hold = 1 << 7,
}
