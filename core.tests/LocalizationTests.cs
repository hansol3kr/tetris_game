using Blockfall.Core.Localization;
using Xunit;

namespace Blockfall.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void English_ReturnsSourceUnchanged()
    {
        Loc.Current = Language.English;
        Assert.Equal("SETTINGS", Loc.T("SETTINGS"));
        Assert.Equal("Anything at all", Loc.T("Anything at all"));
        Loc.Current = Language.English; // leave global state clean
    }

    [Fact]
    public void Korean_TranslatesKnownKeys()
    {
        Loc.Current = Language.Korean;
        Assert.Equal("설정", Loc.T("SETTINGS"));
        Assert.Equal("플레이", Loc.T("PLAY"));
        Loc.Current = Language.English;
    }

    [Fact]
    public void Korean_UnknownKey_FallsBackToEnglishNotBlank()
    {
        Loc.Current = Language.Korean;
        Assert.Equal("NOT A REAL KEY", Loc.T("NOT A REAL KEY"));
        Loc.Current = Language.English;
    }

    [Fact]
    public void Format_FillsPlaceholders_InBothLanguages()
    {
        Loc.Current = Language.English;
        Assert.Equal("Achievement: 5", "Achievement: " + 5); // sanity
        Loc.Current = Language.Korean;
        Assert.Equal("업적: WIZARD", Loc.T("ACHIEVEMENT: {0}", "WIZARD"));
        Loc.Current = Language.English;
        Assert.Equal("ACHIEVEMENT: WIZARD", Loc.T("ACHIEVEMENT: {0}", "WIZARD"));
    }

    [Fact]
    public void ChangedEvent_FiresOnlyOnActualChange()
    {
        Loc.Current = Language.English;
        int fired = 0;
        void Handler() => fired++;
        Loc.Changed += Handler;
        Loc.Current = Language.English; // no change
        Assert.Equal(0, fired);
        Loc.Current = Language.Korean;  // change
        Loc.Current = Language.Korean;  // no change
        Assert.Equal(1, fired);
        Loc.Changed -= Handler;
        Loc.Current = Language.English;
    }

    [Fact]
    public void Translations_PreservePlaceholders()
    {
        // Every Korean value must keep the same {0}/{1} slots as its English key,
        // or string.Format at the call site would throw / drop data.
        foreach (var kv in LocData.Korean)
        {
            for (int i = 0; i < 3; i++)
            {
                string slot = "{" + i + "}";
                Assert.True(kv.Key.Contains(slot) == kv.Value.Contains(slot),
                    $"placeholder {slot} mismatch: \"{kv.Key}\" -> \"{kv.Value}\"");
            }
            Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"blank translation for \"{kv.Key}\"");
        }
    }

    [Fact]
    public void DisplayName_And_Available_CoverAllLanguages()
    {
        Assert.Contains(Language.English, Loc.Available);
        Assert.Contains(Language.Korean, Loc.Available);
        Assert.Equal("English", Loc.DisplayName(Language.English));
        Assert.Equal("한국어", Loc.DisplayName(Language.Korean));
    }
}
