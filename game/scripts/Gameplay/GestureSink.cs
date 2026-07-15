using System;
using Blockfall.Core;

namespace Blockfall.Gameplay;

/// <summary>
/// Where a <see cref="GestureBoardControls"/> delivers the actions it recognises.
/// Two backends share one gesture recogniser:
///  • <see cref="SamplerGestureSink"/> — the solo game funnels every action through the
///    deterministic <see cref="ButtonSampler"/>, so a drag reproduces exactly on replay.
///  • <see cref="GameGestureSink"/> — CPU versus drives its live <see cref="Game"/> directly,
///    exactly the path its classic d-pad already uses (g.HardDrop() / g.RotateCw()).
/// </summary>
public interface IGestureSink
{
    /// <summary>Move the active piece one cell: -1 left, +1 right.</summary>
    void StepHorizontal(int dir);
    void SetSoftDrop(bool on);
    void RotateCw();
    void RotateCcw();
    void Hold();
    /// <summary>Lift-to-place: hard-drop the piece into the column the finger chose.</summary>
    void HardDrop();
    /// <summary>True only while the run is actually playable (not paused / over / finished).</summary>
    bool CanPlay { get; }
}

/// <summary>Solo path: every action becomes a per-tick <see cref="Buttons"/> bit, so the
/// gesture is replay-exact just like the keyboard.</summary>
public sealed class SamplerGestureSink : IGestureSink
{
    private readonly ButtonSampler _sampler;
    private readonly Func<bool> _canPlay;

    public SamplerGestureSink(ButtonSampler sampler, Func<bool> canPlay)
    {
        _sampler = sampler;
        _canPlay = canPlay;
    }

    public void StepHorizontal(int dir) => _sampler.QueueDragMove(dir);
    public void SetSoftDrop(bool on) => _sampler.SetTouchSoft(on);
    public void RotateCw() => _sampler.LatchRotateCw();
    public void RotateCcw() => _sampler.LatchRotateCcw();
    public void Hold() => _sampler.LatchHold();
    public void HardDrop() => _sampler.LatchHardDrop();
    public bool CanPlay => _canPlay();
}

/// <summary>CPU-versus path: the player's board is driven live, exactly like the classic
/// d-pad (g.HardDrop() / g.RotateCw()). Soft drop rides the shared <see cref="InputController"/>
/// so its held-state timing stays identical to the keyboard/d-pad.</summary>
public sealed class GameGestureSink : IGestureSink
{
    private readonly Game _game;
    private readonly InputController _input;
    private readonly Func<bool> _canPlay;

    public GameGestureSink(Game game, InputController input, Func<bool> canPlay)
    {
        _game = game;
        _input = input;
        _canPlay = canPlay;
    }

    public void StepHorizontal(int dir) { if (dir < 0) _game.MoveLeft(); else _game.MoveRight(); }
    public void SetSoftDrop(bool on) { if (on) _input.PressSoftDrop(_game); else _input.ReleaseSoftDrop(_game); }
    public void RotateCw() => _game.RotateCw();
    public void RotateCcw() => _game.RotateCcw();
    public void Hold() => _game.HoldPiece();
    public void HardDrop() => _game.HardDrop();
    public bool CanPlay => _canPlay();
}
