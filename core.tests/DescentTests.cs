using System.Collections.Generic;
using System.Linq;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class RunDirectorTests
{
    [Fact]
    public void Plan_SameSeed_ProducesIdenticalStagePlansAndDraftCandidates()
    {
        var a = new RunDirector(123456789UL);
        var b = new RunDirector(123456789UL);

        Assert.Equal(RunDirector.StageCount, a.Stages.Count);
        for (int i = 0; i < RunDirector.StageCount; i++)
        {
            Assert.Equal(a.Stages[i].Stratum, b.Stages[i].Stratum);
            Assert.Equal(a.Stages[i].Kind, b.Stages[i].Kind);
            Assert.Equal(a.Stages[i].StageSeed, b.Stages[i].StageSeed);
            Assert.Equal(a.Stages[i].DraftPool, b.Stages[i].DraftPool);
        }
    }

    [Fact]
    public void Plan_DifferentSeeds_ProduceDifferentPlans()
    {
        var a = new RunDirector(1UL);
        var b = new RunDirector(2UL);
        Assert.NotEqual(a.Stages[0].StageSeed, b.Stages[0].StageSeed);
    }

    [Fact]
    public void Plan_StratumParameters_MatchTuningPresets()
    {
        var d = new RunDirector(7UL);

        Assert.Equal(StageKind.Dig, d.Stages[0].Kind);
        Assert.Equal(StageKind.Burst, d.Stages[1].Kind);
        Assert.Equal(StageKind.Siege, d.Stages[2].Kind);
        Assert.Equal(StageKind.Dig, d.Stages[3].Kind);
        Assert.Equal(StageKind.Siege, d.Stages[4].Kind);

        var s1 = RunDirector.BaseStageMode(d.Stages[0]);
        Assert.Equal(6, s1.InitialGarbage);
        Assert.True(s1.DigGoal);

        var s2 = RunDirector.BaseStageMode(d.Stages[1]);
        Assert.Equal(60.0, s2.TimeLimit);

        var s3 = RunDirector.BaseStageMode(d.Stages[2]);
        Assert.Equal(10, s3.AttackGoal);
        Assert.Equal(9.0, s3.GarbageRiseInterval);

        var s4 = RunDirector.BaseStageMode(d.Stages[3]);
        Assert.Equal(9, s4.InitialGarbage);
        Assert.Equal(0.8, s4.Config.BaseGravity);

        var s5 = RunDirector.BaseStageMode(d.Stages[4]);
        Assert.Equal(15, s5.AttackGoal);
        Assert.Equal(7.0, s5.GarbageRiseInterval);
        Assert.Equal(0.25, s5.GarbageRiseAccel);

        // Every stage keys records to the Descent mode id.
        foreach (var spec in d.Stages)
            Assert.Equal(GameModeId.Descent, RunDirector.BaseStageMode(spec).Id);
    }

    [Fact]
    public void Plan_DraftPools_AreFullPermutations_ExceptFinalStage()
    {
        var d = new RunDirector(42UL);
        for (int i = 0; i < RunDirector.StageCount - 1; i++)
        {
            var pool = d.Stages[i].DraftPool;
            Assert.Equal(CharmSet.All.Length, pool.Length);
            Assert.Equal(CharmSet.All.Length, pool.Distinct().Count());
        }
        Assert.Empty(d.Stages[RunDirector.StageCount - 1].DraftPool);
    }

    [Fact]
    public void DraftOffer_OwnedCharmExcluded_PromotesNextCandidate()
    {
        var d = new RunDirector(42UL);
        var pool = d.Stages[0].DraftPool;

        var fresh = RunDirector.DraftOffer(d.Stages[0], System.Array.Empty<Charm>());
        Assert.Equal(new[] { pool[0], pool[1], pool[2] }, fresh);

        var owned = new List<Charm> { pool[0] };
        var offer = RunDirector.DraftOffer(d.Stages[0], owned);
        Assert.Equal(new[] { pool[1], pool[2], pool[3] }, offer);
    }

    [Fact]
    public void DraftOffer_SameSeedDifferentPickHistory_CandidateListsIdentical()
    {
        // The pre-planned pools must not depend on what was picked — two players on
        // the same seed argue about the SAME offers, and re-simulation from
        // (seed, picks) never consumes RNG differently.
        var a = new RunDirector(777UL);
        var b = new RunDirector(777UL);

        var runA = new DescentRun(777UL);
        var runB = new DescentRun(777UL);
        runA.CompleteStage(1000);
        runB.CompleteStage(1000);
        runA.PickCharm(runA.DraftOffer()[0]);
        runB.SkipDraft();

        for (int i = 0; i < RunDirector.StageCount; i++)
            Assert.Equal(a.Stages[i].DraftPool, b.Stages[i].DraftPool);
        Assert.Equal(
            runA.Director.Stages[1].DraftPool,
            runB.Director.Stages[1].DraftPool);
    }

    [Fact]
    public void StageSeedDerivation_DoesNotPerturbPieceGeneratorStream()
    {
        // Isolation regression gate: building a RunDirector must never consume from
        // any stream an existing mode's Game depends on.
        var before = Game.Create(GameModeId.Marathon, 42UL);
        before.Start();
        var seqBefore = before.Preview().ToArray();

        _ = new RunDirector(42UL);

        var after = Game.Create(GameModeId.Marathon, 42UL);
        after.Start();
        Assert.Equal(seqBefore, after.Preview().ToArray());
    }

    [Fact]
    public void CreateStageGame_SameSeedSamePicks_DealsIdenticalPieces()
    {
        var runA = new DescentRun(31337UL);
        var runB = new DescentRun(31337UL);

        var gA = runA.CreateStageGame(); gA.Start();
        var gB = runB.CreateStageGame(); gB.Start();
        Assert.Equal(gA.Preview().ToArray(), gB.Preview().ToArray());
        Assert.Equal(gA.Current!.Type, gB.Current!.Type);

        // Same picks -> the NEXT stage's bag is identical too.
        runA.CompleteStage(0); runB.CompleteStage(0);
        var pick = runA.DraftOffer()[1];
        runA.PickCharm(pick); runB.PickCharm(pick);
        var g2A = runA.CreateStageGame(); g2A.Start();
        var g2B = runB.CreateStageGame(); g2B.Start();
        Assert.Equal(g2A.Preview().ToArray(), g2B.Preview().ToArray());
    }

    [Fact]
    public void CreateStageGame_GarbageStream_UsesTheDocumentedSaltDerivation()
    {
        // Pins the factory's seed->garbage-stream contract itself: if the salt in
        // RunDirector.CreateStageGame ever drifts from Game.Create's scheme, piece
        // previews would still match but the dig prefill's hole columns would not.
        var director = new RunDirector(4242UL);
        var spec = director.Stages[0]; // stratum 1: Dig, prefilled garbage rows

        var viaFactory = RunDirector.CreateStageGame(spec, System.Array.Empty<Charm>());
        var byHand = new Game(
            RunDirector.BuildStageMode(spec, System.Array.Empty<Charm>()),
            new SevenBagGenerator(new XorShiftRandom(spec.StageSeed)),
            new XorShiftRandom(spec.StageSeed ^ 0x9E3779B97F4A7C15UL));
        viaFactory.Start();
        byHand.Start();

        Assert.True(viaFactory.Board.GarbageRowCount() > 0); // the prefill actually ran
        for (int r = 0; r < viaFactory.Board.TotalRows; r++)
            for (int c = 0; c < viaFactory.Board.Width; c++)
                Assert.Equal(byHand.Board[r, c], viaFactory.Board[r, c]);
    }

    [Fact]
    public void CreateStageGame_HandlingOverlay_CannotUnsealCharmedGhost()
    {
        var run = new DescentRun(5UL);
        run.CompleteStage(0);
        // Force-own Blindfold via the offer if present; otherwise apply directly.
        var mode = RunDirector.BuildStageMode(run.Director.Stages[1],
            new[] { Charm.Blindfold },
            handling: GameConfig.Default.With(ghost: true, das: 0.1, arr: 0.01));
        Assert.False(mode.Config.GhostEnabled); // charm seal survives player preference
        Assert.Equal(0.1, mode.Config.Das, 12); // handling still applied
    }
}

public class DescentRunTests
{
    [Fact]
    public void BankedScore_DeathAtStratumThree_RetainsCompletedStageScores()
    {
        var run = new DescentRun(99UL);

        Assert.True(run.CompleteStage(1000) > 0);  // stratum 1 cleared (+2000 bonus)
        run.SkipDraft();                           // +1500
        Assert.True(run.CompleteStage(2500) > 0);  // stratum 2 cleared (+4000 bonus)
        run.PickCharm(run.DraftOffer()[0]);

        long bankedBeforeDeath = run.Bank;
        Assert.Equal(1000 + 2000 + 1500 + 2500 + 4000, bankedBeforeDeath);

        run.FailStage(); // topped out in stratum 3

        Assert.True(run.Finished);
        Assert.False(run.Victory);
        Assert.Equal(2, run.StrataCleared);
        Assert.Equal(bankedBeforeDeath, run.Bank); // death never robs the bank
    }

    [Fact]
    public void SkipDraft_AddsSkipBonusToBank()
    {
        var run = new DescentRun(1UL);
        run.CompleteStage(0);
        long before = run.Bank;
        Assert.True(run.SkipDraft());
        Assert.Equal(before + RunDirector.SkipBonus, run.Bank);
        Assert.Equal(1, run.StageIndex); // descended to stratum 2
    }

    [Fact]
    public void StratumClearBonus_ScalesWithDepth()
    {
        var run = new DescentRun(2UL);
        Assert.Equal(RunDirector.StageClearBonusPerStratum * 1, run.CompleteStage(0));
        run.SkipDraft();
        Assert.Equal(RunDirector.StageClearBonusPerStratum * 2, run.CompleteStage(0));
    }

    [Fact]
    public void PickCharm_OutsideOffer_IsRejected()
    {
        var run = new DescentRun(3UL);
        run.CompleteStage(0);
        var offer = run.DraftOffer();
        Assert.Equal(RunDirector.DraftOfferSize, offer.Length);
        var notOffered = CharmSet.All.First(c => !offer.Contains(c));

        Assert.False(run.PickCharm(notOffered));
        Assert.True(run.AwaitingDraft); // still at the draft
        Assert.Empty(run.Owned);

        Assert.True(run.PickCharm(offer[2]));
        Assert.Equal(new[] { offer[2] }, run.Owned);
    }

    [Fact]
    public void CompleteStage_AllFiveStrata_IsVictory()
    {
        var run = new DescentRun(4UL);
        for (int i = 0; i < RunDirector.StageCount; i++)
        {
            Assert.True(run.CompleteStage(100) >= 0);
            if (i < RunDirector.StageCount - 1) Assert.True(run.SkipDraft());
        }
        Assert.True(run.Finished);
        Assert.True(run.Victory);
        Assert.Equal(RunDirector.StageCount, run.StrataCleared);
        Assert.False(run.SkipDraft());            // nothing left to draft
        Assert.Equal(-1, run.CompleteStage(1));   // nothing left to complete
    }

    [Fact]
    public void CreateStageGame_AppliesOwnedCharmsToConfig()
    {
        var run = new DescentRun(6UL);
        run.CompleteStage(0);
        // Walk drafts until Prophet shows up (deterministic per seed; guard on pool).
        while (!run.DraftOffer().Contains(Charm.Prophet) && run.StageIndex < RunDirector.StageCount - 2)
        {
            run.SkipDraft();
            run.CompleteStage(0);
        }
        if (run.DraftOffer().Contains(Charm.Prophet))
        {
            run.PickCharm(Charm.Prophet);
            var g = run.CreateStageGame();
            Assert.False(g.Config.HoldEnabled);
            Assert.Equal(GameConfig.Default.PreviewCount + 2, g.Config.PreviewCount);
        }
        else
        {
            // Fallback (seed-dependent): the composition itself is still exercised.
            var mode = RunDirector.BuildStageMode(run.CurrentStage, new[] { Charm.Prophet });
            Assert.False(mode.Config.HoldEnabled);
        }
    }

    [Fact]
    public void FailStage_DuringDraft_ClosesTheDraftForGood()
    {
        // A run that ends while a draft is open must reject every later pick or
        // skip — a finished run could otherwise still bank the skip bonus or
        // grow its charm list.
        var run = new DescentRun(77UL);
        run.CompleteStage(1000);
        Assert.True(run.AwaitingDraft);
        var offer = run.DraftOffer();

        run.FailStage();

        Assert.True(run.Finished);
        Assert.False(run.AwaitingDraft);
        Assert.Empty(run.DraftOffer());
        long bank = run.Bank;
        Assert.False(run.SkipDraft());
        Assert.False(run.PickCharm(offer[0]));
        Assert.Equal(bank, run.Bank);
        Assert.Empty(run.Owned);
    }

    [Fact]
    public void CompleteStage_WhileAwaitingDraft_IsRejected()
    {
        var run = new DescentRun(78UL);
        run.CompleteStage(500);
        long bank = run.Bank;
        Assert.Equal(-1, run.CompleteStage(9999));
        Assert.Equal(bank, run.Bank);
        Assert.Equal(1, run.StrataCleared);
    }

    [Fact]
    public void PickCharm_WhenNotDrafting_IsRejected()
    {
        var run = new DescentRun(79UL);
        Assert.False(run.PickCharm(CharmSet.All[0]));
        Assert.Empty(run.Owned);
        Assert.Equal(0, run.StageIndex);
    }
}

public class DescentLeaderboardTests
{
    private static LeaderboardEntry Entry(long score, int depth)
        => new(score, 0, 0, 0, 0) { Depth = depth };

    [Fact]
    public void Insert_DepthEntries_RankDepthFirstThenBank()
    {
        var board = new List<LeaderboardEntry>();
        LeaderboardLogic.Insert(board, Entry(9000, 2), timeAttack: false);
        LeaderboardLogic.Insert(board, Entry(3000, 5), timeAttack: false);
        LeaderboardLogic.Insert(board, Entry(4000, 5), timeAttack: false);

        Assert.Equal(5, board[0].Depth);
        Assert.Equal(4000, board[0].Score); // deeper first, richer bank breaks the tie
        Assert.Equal(5, board[1].Depth);
        Assert.Equal(3000, board[1].Score);
        Assert.Equal(2, board[2].Depth);    // shallow high-bank run never outranks depth
    }

    [Fact]
    public void Insert_LegacyBoardsWithoutDepth_SortUnchanged()
    {
        var board = new List<LeaderboardEntry>();
        LeaderboardLogic.Insert(board, Entry(100, 0), timeAttack: false);
        LeaderboardLogic.Insert(board, Entry(300, 0), timeAttack: false);
        Assert.Equal(300, board[0].Score);

        var sprint = new List<LeaderboardEntry>();
        LeaderboardLogic.Insert(sprint, new LeaderboardEntry(0, 61.5, 40, 0, 0), timeAttack: true);
        LeaderboardLogic.Insert(sprint, new LeaderboardEntry(0, 48.2, 40, 0, 0), timeAttack: true);
        Assert.Equal(48.2, sprint[0].TimeSeconds);
    }
}

public class RunStatsAccumulateTests
{
    [Fact]
    public void Accumulate_SumsCountersAndKeepsPeaks()
    {
        var total = new RunStats();
        var s1 = new RunStats { PiecesPlaced = 40, Quads = 2, MaxCombo = 3, TotalLines = 18, FinishTime = 60 };
        var s2 = new RunStats { PiecesPlaced = 25, Quads = 1, MaxCombo = 5, TotalLines = 12, FinishTime = 45 };
        total.Accumulate(s1);
        total.Accumulate(s2);

        Assert.Equal(65, total.PiecesPlaced);
        Assert.Equal(3, total.Quads);
        Assert.Equal(5, total.MaxCombo);   // peak, not sum
        Assert.Equal(30, total.TotalLines);
        Assert.Equal(105.0, total.FinishTime);
    }
}

public class DescentAttackGoalTests
{
    private static Game CreateSiege(int attackGoal, ulong seed = 9UL)
    {
        var mode = new GameMode
        {
            Id = GameModeId.Descent,
            Name = "Siege Test",
            AttackGoal = attackGoal,
            CanTopOut = true,
            StartLevel = 1,
            Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
        };
        var rng = new XorShiftRandom(seed);
        return new Game(mode, new SevenBagGenerator(rng), new XorShiftRandom(seed ^ 0x9E3779B97F4A7C15UL));
    }

    private static void FillBottomRows(Game g, int rows)
    {
        for (int r = g.Board.TotalRows - rows; r < g.Board.TotalRows; r++)
            for (int c = 0; c < g.Board.Width; c++)
                g.Board[r, c] = PieceType.Garbage;
    }

    [Fact]
    public void AttackGoal_TotalGarbageSentReachesGoal_FinishesCompleted()
    {
        var g = CreateSiege(attackGoal: 1);
        g.Start();
        // Two pre-filled full rows clear as a double on the next lock -> sends 1.
        FillBottomRows(g, 2);
        g.HardDrop();

        Assert.True(g.TotalGarbageSent >= 1);
        Assert.Equal(GameStatus.Completed, g.Status);
    }

    [Fact]
    public void AttackGoal_Zero_NeverCompletesViaAttackPath()
    {
        var g = CreateSiege(attackGoal: 0);
        g.Start();
        FillBottomRows(g, 2);
        g.HardDrop(); // double clear, attack readout 1, but no goal to meet

        Assert.True(g.TotalGarbageSent >= 1);
        Assert.Equal(GameStatus.Running, g.Status);
    }

    [Fact]
    public void SiegeStage_SameSeedSameCommands_ResimulatesBitIdentical()
    {
        var a = CreateSiege(attackGoal: 10, seed: 1234UL);
        var b = CreateSiege(attackGoal: 10, seed: 1234UL);
        a.Start();
        b.Start();
        foreach (var g in new[] { a, b })
        {
            for (int i = 0; i < 40 && g.Status == GameStatus.Running; i++)
            {
                g.MoveLeft();
                g.RotateCw();
                g.HardDrop();
                for (int t = 0; t < 30; t++) g.Update(1.0 / 60.0);
            }
        }
        Assert.Equal(a.Scoring.Score, b.Scoring.Score);
        Assert.Equal(a.TotalGarbageSent, b.TotalGarbageSent);
        Assert.Equal(a.Board.GarbageRowCount(), b.Board.GarbageRowCount());
        Assert.Equal(a.Status, b.Status);
    }

    [Fact]
    public void AttackGoal_MetWithPendingGarbage_CompletesBeforeTheCommit()
    {
        // The escape contract: reaching the goal on a lock completes the stage
        // BEFORE the pending-garbage commit, so the leftover garbage never lands
        // (a lethal commit on the winning lock would contradict the "meeting the
        // goal is an escape" rule in Game.ApplyGarbageMechanics).
        var mode = new GameMode
        {
            Id = GameModeId.Descent, Name = "Siege Test", AttackGoal = 1,
            GarbageRiseInterval = 1.0, GarbageRiseAmount = 1, CanTopOut = true,
            StartLevel = 1, Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
        };
        var g = new Game(mode, new SevenBagGenerator(new XorShiftRandom(9UL)),
            new XorShiftRandom(9UL ^ 0x9E3779B97F4A7C15UL));
        g.Start();

        // Bank up incoming garbage via the rise clock (it commits only on lock).
        for (int t = 0; t < 130; t++) g.Update(1.0 / 60.0);
        Assert.True(g.PendingGarbage >= 2);

        // A double sends 1: cancels one pending, reaches the goal with some left.
        FillBottomRows(g, 2);
        g.HardDrop();

        Assert.Equal(GameStatus.Completed, g.Status);
        Assert.True(g.PendingGarbage > 0);           // the leftover...
        Assert.Equal(0, g.Board.GarbageRowCount()); // ...never landed
    }
}
