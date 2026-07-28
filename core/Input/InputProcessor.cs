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
    private double _dasTimer;  // V1 only: float accumulation (kept for replay fidelity)
    private bool _dasCharged;
    private double _arrTimer;  // V1 only
    private int _dasTicksElapsed; // V2: whole ticks since the direction was pressed
    private int _arrTicksElapsed; // V2: whole ticks since the last auto-shift step
    private bool _softHeld;
    private int _lastSpawnCount = -1;

    // V2 timing, resolved to whole ticks ONCE (see Sim.TicksFor for why).
    private readonly bool _legacyTiming;
    private readonly int _dasTicks;
    private readonly int _arrTicks;

    public InputProcessor(GameConfig config, FinesseTracker? finesse = null)
    {
        _config = config;
        _finesse = finesse;
        _legacyTiming = config.Rules == RulesVersion.V1;
        _dasTicks = Sim.TicksFor(config.Das);
        // A positive-but-sub-tick ARR still has to advance at least one tick per step,
        // otherwise the repeat loop would have no denominator. ARR <= 0 is the separate
        // "teleport to the wall" mode and never reaches here.
        _arrTicks = Math.Max(1, Sim.TicksFor(config.Arr));
    }

    public void Reset()
    {
        _dir = 0;
        _dasTimer = 0;
        _dasCharged = false;
        _arrTimer = 0;
        _dasTicksElapsed = 0;
        _arrTicksElapsed = 0;
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
            _dasTicksElapsed = 0; _arrTicksElapsed = 0;
            _finesse?.OnTapMove(held);
            // The press itself is the action that spends a lock reset; every auto-shift
            // step below is a continuation of this same press.
            if (held < 0) game.MoveLeft(isNewAction: true); else game.MoveRight(isNewAction: true);
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
                bool charged;
                if (_legacyTiming)
                {
                    // V1: float accumulation. 1/60 summed six times is 0.09999999999999999,
                    // so a 100 ms DAS actually charged at 116.7 ms. Frozen for old replays.
                    _dasTimer += Sim.TickDt;
                    charged = _dasTimer >= _config.Das;
                }
                else
                {
                    _dasTicksElapsed++;
                    charged = _dasTicksElapsed >= _dasTicks;
                }
                if (charged)
                {
                    _dasCharged = true;
                    _arrTimer = 0;
                    _arrTicksElapsed = 0;
                    _finesse?.OnDasMove(_dir); // exactly one DAS engagement per hold
                }
            }
            else if (_config.Arr <= 0)
            {
                // Instant ARR: slide to the wall inside this tick. The whole slide is one
                // continuation of the held press, not 10 separate lock resets.
                for (int i = 0; i < 32; i++)
                    if (!Shift(game)) break;
            }
            else if (_legacyTiming)
            {
                _arrTimer += Sim.TickDt;
                while (_arrTimer >= _config.Arr)
                {
                    _arrTimer -= _config.Arr;
                    if (!(_dir < 0 ? game.MoveLeft() : game.MoveRight())) break;
                }
            }
            else
            {
                // V2: whole-tick repeat. A 20 ms ARR resolves to 1 tick and steps every
                // tick, instead of V1's ragged 1-1-1-1-1-pause pattern from float drift.
                _arrTicksElapsed++;
                while (_arrTicksElapsed >= _arrTicks)
                {
                    _arrTicksElapsed -= _arrTicks;
                    if (!Shift(game)) break;
                }
            }
        }

        // --- Soft drop (held) ----------------------------------------------
        bool soft = (b & Buttons.Soft) != 0;
        if (soft && !_softHeld) _finesse?.OnSoftDrop(); // rising edge arms the tuck rule
        _softHeld = soft;
        game.SetSoftDrop(soft);
    }

    /// <summary>
    /// One auto-shift step in the currently held direction. Flagged as a CONTINUATION
    /// (not a new action) so a held slide costs the lock-delay budget exactly once,
    /// no matter how many cells it covers or how fast the player's ARR is.
    /// </summary>
    private bool Shift(Game game)
        => _dir < 0 ? game.MoveLeft(isNewAction: false) : game.MoveRight(isNewAction: false);
}
