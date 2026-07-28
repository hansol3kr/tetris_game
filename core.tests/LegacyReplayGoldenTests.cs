using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// The determinism contract's regression net: real <see cref="ReplayData"/> blobs that
/// must re-simulate to the exact same score, lines, piece count and board bytes forever.
///
/// Why this file exists: v1.5 fixed three shipped rule bugs — lock-delay resets were
/// charged per CELL moved (so ARR=0 burned the budget ~5x faster than tapping), the
/// reset budget was compared with '&gt;' (granting N+1), DAS/ARR were compared against a
/// float-accumulated timer (100ms actually charged at 116.7ms), and the 180 kick table
/// could displace a piece two columns. Every one of those changes the outcome of a given
/// (seed, button-stream) pair. Without <see cref="RulesVersion"/> they would have silently
/// rewritten every saved replay, every ghost, and every verified score. These blobs are
/// the proof that they did not.
///
/// Fixtures A–C were recorded on the pre-fix v1.4 binary itself; that they still replay
/// bit-identically is what anchors the V1 branch to the code that actually shipped.
/// D–G were minted through that anchored branch with a HELD-direction button stream, so
/// DAS charges and ARR repeats — the twitchy per-tick random streams used elsewhere in
/// the suite never charge DAS and therefore cannot observe the lock-reset rules at all.
///
/// NEVER regenerate these constants. If one fails, the V1 branch is broken and player
/// history is at risk — fix the code, not the fixture.
/// </summary>
public class LegacyReplayGoldenTests
{
    // FNV-1a over the raw board cells: a compact, order-sensitive bit-identity check.
    private static ulong BoardHash(PieceType[] cells)
    {
        ulong h = 1469598103934665603UL;
        foreach (var c in cells) { h ^= (byte)c; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>Marathon, seed 4242, DAS 133ms / ARR 20ms — 996 ticks, 21 pieces, 0 line(s).</summary>
    private const string V1CodeA =
        "H4sIAAAAAAAAA1VTO24VQRCsbmZGz5aDmZWFENG+J0TgBMMFPDuyjEPbgtzmGJCsLYQILQtycwEycuAeBEgchKrZJyH2fXZnurequrqnXRiAu4z/rvvHz349ST+O3u1/ffn77aejO/sX+/aa1+fD+ucBkGDxnjHno7kH30sNbsEzuDwwL2bW+IghGLOMX2ZzI3IPKA0/gQlMY8ANA3yjMFeF6Li2woUzPGPkGwxl0ZnQYK2GMGhByuh7ggz1KgJxwRgk+MSVXEXelVqwqkStE94Y2gg/Kx6wG1zYCYGMtTg2vuOsDN4SCBhQ99HRxtMFy6d5DHGFC+BYEV0DyYvS+UnrebJ6qrqt+yR+eDDzlJgyW3c+uiRdtd3bkXV70p4vcIFZJ1rEyaiZGgS2YWyQ03LTVk6PWTME63M1rKRtCIvbVSiBFil7DGgFyETPcSTBWJnAmh9dhm50IUeQ11BPBp/4UpBEEkcKyb0IUfVqvTpucTyDrn+0zDIf9jC3cYgw0uLNq4wXREJ3yAUuZWoQZZCy4Uxrjk5oMVb1Lq++MJ9NZ7w7Th7WhVyEUta2eKl/D4kVruXXqKYeRKwzp2XATq8h1+e601j6UJQ2BZpXJKCmTIM3c1RT2BVOAqbvvhR9Ey8/nHMaLS4j3vvh67i63npAburf20gEsorv3R1WGrclgfrPHfuaYWWtZ2wDVm8wWBLsaDmztY3dtAk+Eyx2r4o8t4HI8o6/48IJ4KELSRZSOWcC8wR7Pw86HGidZmv/QjUNpiFzdZMDrFPnnA8OKbJ2n2ps0NutY6P7pBebjQLb+QuYdjaxIgQAAA==";

    /// <summary>Marathon, seed 909, DAS 100ms / ARR 0 (instant) — 385 ticks, 15 pieces, 0 line(s).</summary>
    private const string V1CodeB =
        "H4sIAAAAAAAAA1WQwUoEMRBEq9okjLDKzDAe1oNk5uDdLzAzoKCnRfyg2WXxJngS9E/8O6sjIuYQ0tXVXY8sTwTweoJ/5/PDz9ftb33Dv95L7W3LvmrmF9GjtGZGDukaOEh78MYqyRJn5A0YFxlhbKX55DLHZEkCG0NAWY1HCwaWgmkhO9nJVkuaFm/AnLEdNdcotG891tf5hK9j27i9RzfShVDZKA+RlIliXaLZD7IoHBsJtHCKo4xrHCpfn8+NQc8zUzkA0WLUrYUl8E58Ee+i1/yVr4rGblVR08CMe0EpJEjcWM7h2XZZX7C/gEVHVvjOvyBizKonC/VbKpt35dLVPaJPteahyD3Z7CkRvHT2JETLzTd9nrwxvwEAAA==";

    /// <summary>Master 20G, seed 7, DAS 200ms / ARR 50ms — 360 ticks, 19 pieces, 0 line(s).</summary>
    private const string V1CodeC =
        "H4sIAAAAAAAAAz1Qu03EQBB9bxib9emCtRMkRGC7kRtbmAAJyXcVEBBcGbaly0mhA7ogpQhaoAZmbcELduf33nz6EwFcY8Pf//6W8HXY/o/D3RX+8f2TcGNnJ4oUrAWgGHsR0GQGL+IJxIyoidnmZYeFUVFHoNHhCaiggo6Fxmx2q6RoAPeINBUFG8yaxGFAq+hdmW6bvy05CQbvyRrqIxiVHkd3i67x+dSME2RaxzOormQfUkcJIa9wTBu7L59EI6W56/nLiLUwkFUq8JhqBTohVauIZBmfoW0u976gKeOr8iVlXaw/PybrVIJBlRk31S5P11nv5ksAu/3axLWdNaJc2VRh8PtoIUnKUUruNQ+MWIYRCL+wT951pgEAAA==";

    /// <summary>Marathon, seed 4242, DAS 133ms / ARR 20ms — 778 ticks, 19 pieces, 0 line(s).</summary>
    private const string V1CodeD =
        "H4sIAAAAAAAAA1VSzUrDQBCejNmBCi1taHLoQbRo0JPoCzS7mHgrVaR3PfQh9GIDDZ7FBxAfwHfwQTwI4nM4M5tN9YP87O7sN9/MN+46AoDnIfzD6+T085A+Zg/j98uv+5fZXbQ9+14xfk6K3R2ASDDSd2T1H97QIRJjQMgYLLFGWhJVpMAAjkKhewxLoptYYIzxhExszFjWgjL1317U4YAj7B8J63bfcOiGQzNlJpK15K5WOXlthAnl+hOS9Uyb14oy1uUDJcMtq3PoI1iQL2Gqm9Qoby6q9P5av2ajlUBb6vm25qoiU3JAyuJtm1ErSk3bm+lUEldNJlLjrl3UAimT60yguOKHd/cob1iGNnUL4O6oZarpyARoJzem9WwkMlhDAfsAT+qpGse0hd7GQKv2OVd7G2sMB7XsAi9qTGRLBsD3HlMpETvfuWUuQVV+FmmiBHEYLLS+eQyr7rJFGSU0l1FyYYCIILjP6PN6HgYrUZEcFwbICisspIhF4eU6nDgO5kMAP1qjSAwpTTedMhim7zkgpLJKZ4S9qUKkAziO4wsZ9l+QCDPKSAMAAA==";

    /// <summary>Marathon, seed 909, DAS 100ms / ARR 0 (instant) — 752 ticks, 23 pieces, 0 line(s).</summary>
    private const string V1CodeE =
        "H4sIAAAAAAAAA1VSMU4DMRC0jW0RxEUk4twhJSh3EgVFCmpyET5RkAIKahryDoIEouEB0PMIevgPNRUza18SJjkn9u7OjmdvfqOVUq876h/e34jP827/pTex3xdg92T2Y7AxHXyGtdb7WxzM8VyqFMIyZPbuSEXHjxY4AjXGByS1Xp5G69LF6CqHNaU4/ISlYVSYBNjUCFUxVqf3SBn0NpxWIGqzsj51tH6D1icaYdNZT4HayH4dxjlQcvGtEFzZidAf2l4zSPFCuj4hq9GzGVdI8KH2rTEBoo+7hrUYZK1jzhp0oV7rek4+yIkxI6azful9kPubx+Ecl2LHklIH8iW6WYREhatMO0vK6JqCvkwsr8gK5z5cRtQHyl7EvGMNGcewD8Mcmj1w4vw7iZ5qGZooTJ1c6kJyu+0bSVBxnTeNXuk0zFwfQp5If6HdQrzP5SID7R/oFf+PWQrL0WNfjmKVpIK0gA/xKE2+5LM4Q7BJokhVuKqo6d3aZbFx692ljiC+195eoYB6Vwqj1CN1ByFNw379P4af5xcuAwAA";

    /// <summary>Master 20G, seed 7, DAS 200ms / ARR 50ms — 872 ticks, 28 pieces, 0 line(s).</summary>
    private const string V1CodeF =
        "H4sIAAAAAAAAA0VTzW4TMRC2B8/ARkrFWmQlKqVqIjY3LrxAs1a8ogcOjZQH6KFVH4NUahUQF5BAgjfgMXrkrZgfO/0irx3vNzPffPamrXfOvXSGOv/5Lfh3YfPfi4/eHfHhy+FwePV+fffCOYAJndBIBABplKfgHvwcDX3GEHie5Yz9p8b71jOGgR/t8P2al/LfZ34/djFGgM9K4IGYOU4iFpzAeBrdFMZCZlHEC+yZPuPxgA0iMYAMHUjYvkgjeqQQvolyllk4nfLvYQtpElPZBS3FHXBmnFo3GbMUEXDJHVZFUt9DSlZjaYWIbnmMS/9WWdLNz6BmWMjwtJdEYR1BfsCDioNsKFWat2rzbDM3bEa0ZpCY38GPZOHPKP7j1PvLG7Oyx+bKez6o4gXPyYXwLoQUpfJkUrdFNxtZBHBIFN9GWklmkQpfmTQGp2LUgr3yOrMOtJNoyZYSTRSkgcvX6oOCT/AhhHPn1pqfzp5jtakT6pkznd3gTg6WHW6hipdnYvLWlqzj3MV6lI+rW03l/VUxMFvNRb0DVEql44q7WckdMM0Dd2PnvMvm+oCbULBxev3Q3tSM3TFrtM41+9iVLVe9lIs7tJa0sa+q3BjnTrk00S9tn2jJY0Vh88aF+X8w0l1EpgMAAA==";

    /// <summary>Zen, seed 1234, DAS 150ms / ARR 33ms — 691 ticks, 28 pieces, 1 line(s).</summary>
    private const string V1CodeG =
        "H4sIAAAAAAAAAz1SW2obQRCcafc0kokX7SLtt3bBygEMyad3F2nBxhjbYH/nJwdRiAP6Cz5BcoYcIeQCuUPukaqelQrm2a/qmhmeYgjhrwbHWV7CleP39d3HQ//p34/rn5Lv6fvsu/fdL9z1fewjkNLnVYr7iFPfYE6Osom715SeuVdUeIBnibFv4tyDOFYw7ny5cK/FYiYi1SCVOWqTdvxmpl23Vt1HR5P+eMgl5/HoCEiLYLN16EhSmEmyxST7FHNyLsnjQO6POaOcD1LAXnPjWZgH/lhVze7Nr54GGSQjK7JQHevCisJCmOG8/dq4IlNniU0C81ylT7sLFjsVmMAWxo0HljEL8gpBd3yW2XKrVE9u6VV/MF1/zyLECVQOEZdy/sVsw2RIXXMUxwJe6sV4eyLPto7mqp1SenEka9Fn5RxHZ7fhZfK+6EJC3fRZbt/gNjCJa1ORrmo9vhRL1NAlXPERELWKN3wxpPFOMSEgbJVSTa30B9qdEtAOEwlBDHXNRjm9NnaVe3CMBiE886PzRKnS5VmBLX+EvlMr/gO76fg08QIAAA==";

    [Theory]
    [InlineData(V1CodeA, 402L, 0, 21, 6555730359260128691UL)]
    [InlineData(V1CodeB, 305L, 0, 15, 1316490327781571515UL)]
    [InlineData(V1CodeC, 796L, 0, 19, 10476814732338447103UL)]
    [InlineData(V1CodeD, 354L, 0, 19, 9047390808262297981UL)]
    [InlineData(V1CodeE, 452L, 0, 23, 3720440367031446997UL)]
    [InlineData(V1CodeF, 328L, 0, 28, 5844826411669426719UL)]
    [InlineData(V1CodeG, 673L, 1, 28, 15970503973300502731UL)]
    public void V1Replay_ResimulatesBitIdentically_UnderV1Rules(
        string code, long score, int lines, int pieces, ulong boardHash)
    {
        Assert.True(ReplayData.TryFromShareCode(code, out var data));
        Assert.NotNull(data);
        Assert.Equal(1, data!.Version);
        Assert.Equal(RulesVersion.V1, data.Rules); // the version branch actually engaged

        var g = new ReplayPlayer(data).PlayToEnd();

        Assert.Equal(score, g.Scoring.Score);
        Assert.Equal(lines, g.Scoring.LinesCleared);
        Assert.Equal(pieces, g.Stats.PiecesPlaced);
        Assert.Equal(GameStatus.GameOver, g.Status);
        Assert.Equal(boardHash, BoardHash(g.Board.Snapshot()));
    }

    [Theory]
    [InlineData(V1CodeD, 354L, 0, 19, 9047390808262297981UL)]
    [InlineData(V1CodeE, 452L, 0, 23, 3720440367031446997UL)]
    [InlineData(V1CodeF, 328L, 0, 28, 5844826411669426719UL)]
    public void V1Replay_ReplayedUnderV2Rules_Diverges(
        string code, long score, int lines, int pieces, ulong boardHash)
    {
        // Guards the guard. A green bit-identity test only means something if the fixtures
        // are SENSITIVE to the rule change; otherwise the version branch could be a no-op
        // and nobody would notice. Re-running the identical button stream under v2 rules
        // must NOT land on the same board.
        //
        // Fixture G is deliberately absent: its handling (DAS 150ms = exactly 9 ticks
        // under both timings) and its gentle Zen gravity mean that run never reaches the
        // reset cap nor a 180 that needs a kick, so both rule sets legitimately agree.
        // That is a feature — the v2 changes are surgical, not a wholesale physics
        // rewrite — but it makes G useless as a sensitivity probe.
        Assert.True(ReplayData.TryFromShareCode(code, out var data));
        var v1 = data!;
        var v2 = new ReplayData
        {
            Version = ReplayData.CurrentVersion,
            Seed = v1.Seed, Mode = v1.Mode, Modifiers = v1.Modifiers,
            Das = v1.Das, Arr = v1.Arr, Inputs = v1.Inputs,
        };
        Assert.Equal(RulesVersion.V2, v2.Rules);

        var g = new ReplayPlayer(v2).PlayToEnd();
        bool identical = g.Scoring.Score == score && g.Scoring.LinesCleared == lines
            && g.Stats.PiecesPlaced == pieces && BoardHash(g.Board.Snapshot()) == boardHash;
        Assert.False(identical, "v1 and v2 rules produced the same run - the fixture is not sensitive");
    }

    [Fact]
    public void ReplayValidator_StillAcceptsV1Recordings()
    {
        // A rules fix must never retroactively void an honest player's verified score:
        // old submissions stay verifiable under the rules they were played on.
        Assert.True(ReplayData.TryFromShareCode(V1CodeD, out var data));
        var r = ReplayValidator.Validate(data!);
        Assert.True(r.Valid, r.Reason);
        Assert.Equal(RulesVersion.V1, r.Rules);
    }

    [Fact]
    public void ReplayValidator_RejectsFutureVersions()
    {
        // A share code from a build we do not have the rules for cannot be honestly
        // verified, so it is refused rather than mis-simulated into a false negative.
        Assert.True(ReplayData.TryFromShareCode(V1CodeD, out var data));
        var fromTheFuture = new ReplayData
        {
            Version = ReplayData.CurrentVersion + 1,
            Seed = data!.Seed, Mode = data.Mode, Das = data.Das, Arr = data.Arr,
            Inputs = data.Inputs, FinalScore = data.FinalScore, FinalLines = data.FinalLines,
        };
        Assert.False(ReplayValidator.Validate(fromTheFuture).Valid);
    }

    [Fact]
    public void NewRecordings_StampTheCurrentRulesVersion()
    {
        // Regression: a run recorded today must be labelled v2, or tomorrow's playback
        // would re-simulate it under the legacy rules and silently disagree with itself.
        var g = Game.Create(GameModeId.Marathon, 1);
        var rec = new ReplayRecorder(1, GameModeId.Marathon, System.Array.Empty<GameModifier>(),
            g.Config.Das, g.Config.Arr);
        rec.Record(Buttons.None);
        var data = rec.Build(g);
        Assert.Equal(ReplayData.CurrentVersion, data.Version);
        Assert.Equal(SimRules.Current, data.Rules);
        Assert.Equal(RulesVersion.V2, new ReplayPlayer(data).Game.Config.Rules);
    }
}
