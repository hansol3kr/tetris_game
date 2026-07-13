namespace Blockfall.Core;

/// <summary>
/// Turns a per-tick <see cref="Buttons"/> state into engine commands, applying
/// DAS (delayed auto shift) and ARR (auto repeat rate) deterministically in
/// fixed ticks. This is the SAME logic the live game, replays, and netcode all
/// run, so a given (seed, Buttons-stream) always yields an identical game.
///
/// Call <see cref="Step"/> once per fixed tick BEFORE <c>game.Update(Sim.TickDt)</c>.
/// Edge-triggered actions (rotate / hard drop / hold) fire on the tick the bit
/// turns on; horizontal movement auto-shifts; soft drop is a held state.
///
/// Optionally feeds a <see cref="FinesseTracker"/> the semantic tap/DAS/soft
/// events it needs (this is the one place tap-vs-hold is distinguishable). The
/// finesse feed is a pure side-channel — it never changes the commands, so a run
/// recorded with finesse replays identically without it.
/// </summary>
public sealed class InputProcessor
{
    private readonly GameConfig _config;
    private readonly FinesseTracker? _finesse;

    private int _dir;          // -1 left, +1 right, 0 none
    private double _dasTimer;
    private bool _dasCharged;
    private double _arrTimer;
    private bool _softHeld;
    private int _lastSpawnCount = -1;

    public InputProcessor(GameConfig config, FinesseTracker? finesse = null)
    {
        _config = config;
        _finesse = finesse;
    }

    public void Reset()
    {
        _dir = 0;
        _dasTimer = 0;
        _dasCharged = false;
        _arrTimer = 0;
        _softHeld = false;
        _lastSpawnCount = -1;
    }

    public void Step(Buttons b, Game game)
    {
        // At a piece boundary, credit inputs held across the lock (a carried DAS slide
        // or soft drop leaves no fresh press/charge/edge on the new piece).
        if (_finesse is not null && game.SpawnCount != _lastSpawnCount)
        {
            if (_lastSpawnCount >= 0)
            {
                if (_dir != 0 && _dasCharged) _finesse.OnDasMove(_dir);
                if (_softHeld) _finesse.OnSoftDrop();
            }
            _lastSpawnCount = game.SpawnCount;
        }

        // --- Edge-triggered actions (one-tick pulses from ButtonSampler) ----
        // These bits are guaranteed to be single-tick pulses (each press latches one
        // tick, never held), so firing on presence == one action per press and can
        // never span two ticks. Rising-edge would DROP two presses on adjacent ticks.
        if ((b & Buttons.Hold) != 0) game.HoldPiece();
        if ((b & Buttons.RotateCw) != 0) game.RotateCw();
        if ((b & Buttons.RotateCcw) != 0) game.RotateCcw();
        if ((b & Buttons.Rotate180) != 0) game.Rotate180();
        if ((b & Buttons.Hard) != 0) game.HardDrop();

        // --- Horizontal auto-shift (DAS/ARR) --------------------------------
        int held = (((b & Buttons.Right) != 0) ? 1 : 0) - (((b & Buttons.Left) != 0) ? 1 : 0);
        if (held != 0 && held != _dir)
        {
            _dir = held;
            _dasTimer = 0; _arrTimer = 0; _dasCharged = false;
            _finesse?.OnTapMove(held);
            if (held < 0) game.MoveLeft(); else game.MoveRight();
        }
        else if (held == 0)
        {
            _dir = 0;
            _dasCharged = false;
        }

        if (_dir != 0)
        {
            if (!_dasCharged)
            {
                _dasTimer += Sim.TickDt;
                if (_dasTimer >= _config.Das)
                {
                    _dasCharged = true;
                    _arrTimer = 0;
                    _finesse?.OnDasMove(_dir); // exactly one DAS engagement per hold
                }
            }
            else
            {
                _arrTimer += Sim.TickDt;
                if (_config.Arr <= 0)
                {
                    for (int i = 0; i < 32; i++)
                        if (!(_dir < 0 ? game.MoveLeft() : game.MoveRight())) break;
                }
                else
                {
                    while (_arrTimer >= _config.Arr)
                    {
                        _arrTimer -= _config.Arr;
                        if (!(_dir < 0 ? game.MoveLeft() : game.MoveRight())) break;
                    }
                }
            }
        }

        // --- Soft drop (held) ----------------------------------------------
        bool soft = (b & Buttons.Soft) != 0;
        if (soft && !_softHeld) _finesse?.OnSoftDrop(); // rising edge arms the tuck rule
        _softHeld = soft;
        game.SetSoftDrop(soft);
    }
}
