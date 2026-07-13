namespace Blockfall.Core;

public enum VersusSide { Player, Bot }

/// <summary>
/// A 1-versus-1 garbage battle: two <see cref="Game"/> instances that feed their
/// net attack into each other. One side is driven by player input, the other by a
/// <see cref="BotPlayer"/>. Both get the SAME piece sequence (fairness), while
/// garbage holes use independent streams. The match ends when a side tops out.
///
/// The design is engine-agnostic and deterministic, so bot-vs-bot matches can be
/// simulated headlessly in tests. Swapping the bot for a network input source is
/// the whole of turning this into online play later.
/// </summary>
public sealed class VersusMatch
{
    public Game PlayerGame { get; }
    public Game BotGame { get; }
    public BotPlayer Bot { get; }

    public VersusSide? Winner { get; private set; }
    public bool IsOver => Winner.HasValue;
    public double Elapsed { get; private set; }

    public int PlayerIncoming => PlayerGame.PendingGarbage;
    public int BotIncoming => BotGame.PendingGarbage;

    /// <summary>Fires once when the match resolves, with the winning side.</summary>
    public event Action<VersusSide>? MatchEnded;

    public VersusMatch(BotDifficulty difficulty, ulong seed)
    {
        var mode = GameMode.Versus;

        // Identical piece seed for both players → same sequence → fair duel.
        PlayerGame = new Game(mode,
            new SevenBagGenerator(new XorShiftRandom(seed)),
            new XorShiftRandom(seed ^ 0x1111_2222_3333_4444UL));
        BotGame = new Game(mode,
            new SevenBagGenerator(new XorShiftRandom(seed)),
            new XorShiftRandom(seed ^ 0x5555_6666_7777_8888UL));

        PlayerGame.CommitPendingGarbageOnLock = true;
        BotGame.CommitPendingGarbageOnLock = true;

        Bot = new BotPlayer(difficulty, new XorShiftRandom(seed ^ 0x9999_AAAA_BBBB_CCCCUL));

        // Route net attack (post-cancellation) to the opponent's incoming queue.
        PlayerGame.GarbageSentToOpponent += n => BotGame.ReceiveGarbage(n);
        BotGame.GarbageSentToOpponent += n => PlayerGame.ReceiveGarbage(n);

        // Whoever tops out loses.
        PlayerGame.GameOver += () => End(VersusSide.Bot);
        BotGame.GameOver += () => End(VersusSide.Player);
    }

    public void Start()
    {
        PlayerGame.Start();
        BotGame.Start();
    }

    /// <summary>Advance both sides one frame. Player input is applied externally on <see cref="PlayerGame"/>.</summary>
    public void Update(double dt)
    {
        if (IsOver) return;
        Elapsed += dt;
        PlayerGame.Update(dt);
        Bot.Update(dt, BotGame);
        BotGame.Update(dt);
    }

    private void End(VersusSide winner)
    {
        if (Winner.HasValue) return;
        Winner = winner;
        MatchEnded?.Invoke(winner);
    }
}
