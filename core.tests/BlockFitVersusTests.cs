using System;
using Blockfall.Core;
using Blockfall.Core.BlockFit;
using Xunit;

namespace Blockfall.Core.Tests;

public class BlockFitVersusTests
{
    private const int N = BlockFitGame.Size;
    private static PieceType[] EmptyGrid() => new PieceType[N * N];
    private static int Idx(int r, int c) => r * N + c;
    private static BlockPiece Single(PieceType c = PieceType.I) => new(new[] { (0, 0) }, c);

    [Fact]
    public void AddGarbage_FillsThatManyEmptyCells()
    {
        var g = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { Single(), null, null });
        g.AddGarbage(15);
        int garbage = 0;
        for (int r = 0; r < N; r++) for (int c = 0; c < N; c++) if (g.At(r, c) == PieceType.Garbage) garbage++;
        Assert.Equal(15, garbage);
        Assert.False(g.GameOver); // a single still fits among the 85 remaining empties
    }

    [Fact]
    public void AddGarbage_FillingTheLastEmptyCell_EndsTheGame()
    {
        // Board full of Z except one empty cell; tray holds a single that fits it.
        var grid = EmptyGrid();
        for (int i = 0; i < grid.Length; i++) grid[i] = PieceType.Z;
        grid[Idx(4, 4)] = PieceType.Empty;
        var g = new BlockFitGame(grid, new BlockPiece?[] { Single(), null, null });
        Assert.True(g.AnyMovePossible());

        g.AddGarbage(1);            // fills the last empty → nothing can be placed
        Assert.True(g.GameOver);
    }

    [Fact]
    public void Evaluator_PrefersLineClearingPlacement()
    {
        var grid = EmptyGrid();
        for (int c = 0; c < N - 1; c++) grid[Idx(0, c)] = PieceType.O; // row 0 one cell short
        var g = new BlockFitGame(grid, new BlockPiece?[] { Single(PieceType.T), null, null });

        Assert.True(BlockFitEvaluator.BestPlacement(g, new Random(1), BotDifficulty.Master, out int ti, out int br, out int bc));
        Assert.Equal(0, ti);
        Assert.Equal(0, br);
        Assert.Equal(N - 1, bc);    // the single spot that completes row 0
    }

    [Fact]
    public void Bot_PlacesOnlyAfterItsThinkInterval()
    {
        var g = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { Single(), null, null });
        var bot = new BlockFitBot(BotDifficulty.Normal, 5);

        Assert.Equal(0, bot.Update(0.05, g)); // too soon — no placement
        Assert.Equal(0, g.Score);

        bot.Update(2.0, g);                    // well past the interval → it places
        Assert.True(g.Score >= 1);
    }

    [Fact]
    public void Versus_PlayerClear_GarbagesBot_AndWinsWhenBotTopsOut()
    {
        // Player: row 0 one cell short + a single → placing it completes the row (an attack).
        var pGrid = EmptyGrid();
        for (int c = 0; c < N - 1; c++) pGrid[Idx(0, c)] = PieceType.O;
        var player = new BlockFitGame(pGrid, new BlockPiece?[] { Single(PieceType.T), null, null });

        // Bot: full of Z except one empty + a single → any incoming garbage tops it out.
        var bGrid = EmptyGrid();
        for (int i = 0; i < bGrid.Length; i++) bGrid[i] = PieceType.Z;
        bGrid[Idx(5, 5)] = PieceType.Empty;
        var botBoard = new BlockFitGame(bGrid, new BlockPiece?[] { Single(), null, null });

        VersusSide? winner = null;
        var match = new BlockFitVersus(player, botBoard, BotDifficulty.Easy, attackPerLine: 2);
        match.MatchEnded += w => winner = w;

        int lines = match.PlayerPlace(0, 0, N - 1);
        Assert.True(lines >= 1);
        Assert.True(match.BotLastHit >= 1);
        Assert.True(botBoard.GameOver);                 // garbage filled its last empty
        Assert.Equal(VersusSide.Player, winner);
        Assert.Equal(VersusSide.Player, match.Winner);
    }

    [Fact]
    public void Versus_Merge_ClearsNothing_SendsNoGarbage()
    {
        var a = Single(PieceType.I);
        var b = Single(PieceType.O);
        var player = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { a, b, null });
        var botBoard = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { Single(), null, null });
        var match = new BlockFitVersus(player, botBoard, BotDifficulty.Easy);

        Assert.True(match.PlayerMerge(1, 0));
        Assert.Equal(0, match.BotLastHit);              // merging is not an attack
        Assert.False(match.IsOver);
    }
}
