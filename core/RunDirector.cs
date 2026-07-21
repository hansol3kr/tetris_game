using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>What a Descent stratum asks of the player.</summary>
public enum StageKind
{
    Dig,   // clear the pre-filled garbage
    Burst, // score attack on a short clock
    Siege, // send enough attack lines while garbage rises — break the shield
}

/// <summary>
/// One pre-planned stratum of a Descent run. The draft pool is the FULL shuffled
/// charm list for the draft held after this stage (empty for the final stratum) —
/// the visible offer is derived from it by skipping already-owned charms, so the
/// pool never depends on pick history and any (seed, picks) pair re-simulates
/// without consuming RNG differently.
/// </summary>
public readonly struct StageSpec
{
    /// <summary>1-based depth of this stratum.</summary>
    public int Stratum { get; init; }
    public StageKind Kind { get; init; }
    /// <summary>Seed for this stage's piece bag + garbage stream (same derivation as Game.Create).</summary>
    public ulong StageSeed { get; init; }
    /// <summary>Shuffled full charm pool for the post-stage draft (empty on the last stratum).</summary>
    public Charm[] DraftPool { get; init; }
}

/// <summary>
/// Deterministically pre-plans an entire Descent run from a single seed: stage
/// order, stage seeds, and every draft's candidate list. Uses its OWN xorshift
/// stream (salted run seed) and never touches a stage Game's piece or garbage
/// streams, so adding Descent perturbs nothing that existing replays, dailies,
/// or netcode depend on.
///
/// Same run seed => same strata, same stage piece bags, same charm offers —
/// the contract that makes Daily Descent a shared build-crafting argument.
/// </summary>
public sealed class RunDirector
{
    public const int StageCount = 5;
    public const int DraftOfferSize = 3;
    /// <summary>Bank credit for declining a draft ("none of these" is a real choice).</summary>
    public const long SkipBonus = 1500;
    /// <summary>Bank credit per stratum cleared, scaled by depth — descending IS the value.</summary>
    public const long StageClearBonusPerStratum = 2000;

    /// <summary>Salt for the director's private RNG stream (mirrors Game.Create's garbage salt pattern).</summary>
    private const ulong DirectorSalt = 0xD15C_E27D_0B5E_55EDUL;

    public ulong RunSeed { get; }
    public IReadOnlyList<StageSpec> Stages { get; }

    public RunDirector(ulong runSeed)
    {
        RunSeed = runSeed;
        Stages = Plan(runSeed);
    }

    private static StageSpec[] Plan(ulong runSeed)
    {
        var rng = new XorShiftRandom(runSeed ^ DirectorSalt);
        var stages = new StageSpec[StageCount];
        for (int i = 0; i < StageCount; i++)
        {
            // Two draws, high word first — evaluation order is fixed by the language.
            ulong hi = rng.NextUInt();
            ulong lo = rng.NextUInt();
            stages[i] = new StageSpec
            {
                Stratum = i + 1,
                Kind = KindForStratum(i + 1),
                StageSeed = (hi << 32) | lo,
                DraftPool = i < StageCount - 1 ? ShuffledPool(rng) : Array.Empty<Charm>(),
            };
        }
        return stages;
    }

    /// <summary>The fixed descent arc: warm-up dig, hot burst, first siege, deep dig, final siege.</summary>
    private static StageKind KindForStratum(int stratum) => stratum switch
    {
        1 => StageKind.Dig,
        2 => StageKind.Burst,
        3 => StageKind.Siege,
        4 => StageKind.Dig,
        5 => StageKind.Siege,
        _ => StageKind.Burst,
    };

    private static Charm[] ShuffledPool(XorShiftRandom rng)
    {
        var pool = (Charm[])CharmSet.All.Clone();
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }

    /// <summary>
    /// The visible one-of-three offer for the draft after <paramref name="spec"/>:
    /// the first unowned charms of the pre-planned pool, in pool order.
    /// </summary>
    public static Charm[] DraftOffer(StageSpec spec, IReadOnlyCollection<Charm> owned)
    {
        var offer = new List<Charm>(DraftOfferSize);
        foreach (var c in spec.DraftPool)
        {
            if (Owns(owned, c)) continue;
            offer.Add(c);
            if (offer.Count == DraftOfferSize) break;
        }
        return offer.ToArray();
    }

    private static bool Owns(IReadOnlyCollection<Charm> owned, Charm charm)
    {
        foreach (var c in owned)
            if (c == charm) return true;
        return false;
    }

    /// <summary>Tuning-table base rules for each stratum (data, not code).</summary>
    public static GameMode BaseStageMode(StageSpec spec) => spec.Stratum switch
    {
        1 => new GameMode
        {
            Id = GameModeId.Descent, Name = "Stratum 1", InitialGarbage = 6, DigGoal = true,
            StartLevel = 1, Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
        },
        2 => new GameMode
        {
            Id = GameModeId.Descent, Name = "Stratum 2", TimeLimit = 60.0,
            StartLevel = 1, Config = GameConfig.Default,
        },
        3 => new GameMode
        {
            Id = GameModeId.Descent, Name = "Stratum 3", AttackGoal = 10,
            GarbageRiseInterval = 9.0, GarbageRiseAmount = 1,
            StartLevel = 1, Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
        },
        4 => new GameMode
        {
            Id = GameModeId.Descent, Name = "Stratum 4", InitialGarbage = 9, DigGoal = true,
            StartLevel = 1, Config = new GameConfig { BaseGravity = 0.8, MaxGravityLevel = 0 },
        },
        5 => new GameMode
        {
            Id = GameModeId.Descent, Name = "Stratum 5", AttackGoal = 15,
            GarbageRiseInterval = 7.0, GarbageRiseAccel = 0.25, GarbageRiseAmount = 1,
            StartLevel = 1, Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
        },
        _ => GameMode.Descent,
    };

    /// <summary>
    /// Compose the playable rules for a stage: stratum base + player handling
    /// overlay (DAS/ARR; ghost can only be restricted) + owned charms LAST, so a
    /// charm's seal (no hold, no ghost) can never be undone by a preference.
    /// </summary>
    public static GameMode BuildStageMode(StageSpec spec, IReadOnlyCollection<Charm> owned,
        GameConfig? handling = null)
    {
        var mode = BaseStageMode(spec);
        if (handling is not null)
        {
            mode = mode.WithConfig(mode.Config.With(
                das: handling.Das, arr: handling.Arr,
                ghost: mode.Config.GhostEnabled && handling.GhostEnabled));
        }
        return CharmSet.Apply(mode, owned);
    }

    /// <summary>
    /// Seeded stage factory — the Descent counterpart of <see cref="Game.Create"/>,
    /// with the identical seed-to-stream derivation (bag from the stage seed, garbage
    /// holes from the salted stage seed).
    /// </summary>
    public static Game CreateStageGame(StageSpec spec, IReadOnlyCollection<Charm> owned,
        GameConfig? handling = null)
    {
        var mode = BuildStageMode(spec, owned, handling);
        var rng = new XorShiftRandom(spec.StageSeed);
        var gen = new SevenBagGenerator(rng);
        var garbageRng = new XorShiftRandom(spec.StageSeed ^ 0x9E3779B97F4A7C15UL);
        return new Game(mode, gen, garbageRng);
    }
}

/// <summary>
/// The mutable ledger of one Descent run: which stratum, which charms, and how
/// much score is safely banked. Stage scores bank only on CLEARING a stratum
/// (death keeps everything banked so far — failure stings, it never robs), and
/// the run advances stage -> draft -> stage until victory or a top-out.
/// </summary>
public sealed class DescentRun
{
    public RunDirector Director { get; }
    public ulong RunSeed => Director.RunSeed;

    /// <summary>0-based index of the current (or just-finished) stage.</summary>
    public int StageIndex { get; private set; }
    /// <summary>Strata fully cleared — the run's headline "depth" stat.</summary>
    public int StrataCleared { get; private set; }
    /// <summary>Score safely banked: cleared stages + clear bonuses + skip bonuses.</summary>
    public long Bank { get; private set; }
    /// <summary>True between a stage clear and the charm pick/skip.</summary>
    public bool AwaitingDraft { get; private set; }
    public bool Finished { get; private set; }
    /// <summary>True when all strata were cleared (as opposed to dying en route).</summary>
    public bool Victory { get; private set; }

    private readonly List<Charm> _owned = new();
    public IReadOnlyList<Charm> Owned => _owned;

    public DescentRun(ulong runSeed)
    {
        Director = new RunDirector(runSeed);
    }

    public StageSpec CurrentStage => Director.Stages[StageIndex];

    /// <summary>Build the playable Game for the current stage with the owned charms baked in.</summary>
    public Game CreateStageGame(GameConfig? handling = null)
        => RunDirector.CreateStageGame(CurrentStage, _owned, handling);

    /// <summary>
    /// Bank a cleared stage's score plus the depth bonus. Returns the bonus, or -1
    /// if the run isn't in a playable stage state. Clearing the last stratum wins.
    /// </summary>
    public long CompleteStage(long stageScore)
    {
        if (Finished || AwaitingDraft) return -1;
        long bonus = RunDirector.StageClearBonusPerStratum * CurrentStage.Stratum;
        Bank += stageScore + bonus;
        StrataCleared++;
        if (StageIndex == RunDirector.StageCount - 1)
        {
            Finished = true;
            Victory = true;
        }
        else
        {
            AwaitingDraft = true;
        }
        return bonus;
    }

    /// <summary>
    /// A top-out (or quit) ends the run; the bank keeps everything already earned.
    /// Any open draft closes with it — a finished run can never pick or skip.
    /// </summary>
    public void FailStage()
    {
        if (Finished) return;
        Finished = true;
        AwaitingDraft = false;
    }

    /// <summary>The one-of-three charms offered at the current draft (empty if not drafting).</summary>
    public Charm[] DraftOffer()
        => AwaitingDraft ? RunDirector.DraftOffer(CurrentStage, _owned) : Array.Empty<Charm>();

    /// <summary>Take a charm from the current offer and descend. False if not offered.</summary>
    public bool PickCharm(Charm charm)
    {
        if (!AwaitingDraft) return false;
        if (!Array.Exists(DraftOffer(), c => c == charm)) return false;
        _owned.Add(charm);
        Descend();
        return true;
    }

    /// <summary>Decline the draft for a small bank bonus and descend.</summary>
    public bool SkipDraft()
    {
        if (!AwaitingDraft) return false;
        Bank += RunDirector.SkipBonus;
        Descend();
        return true;
    }

    private void Descend()
    {
        AwaitingDraft = false;
        StageIndex++;
    }
}
