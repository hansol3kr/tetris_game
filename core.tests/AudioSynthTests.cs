using Blockfall.Core.Audio;
using Xunit;

namespace Blockfall.Core.Tests;

public class AudioSynthTests
{
    [Theory]
    [InlineData("move")]
    [InlineData("rotate")]
    [InlineData("lock")]
    [InlineData("hard_drop")]
    [InlineData("hold")]
    [InlineData("line_clear")]
    [InlineData("tetris")]
    [InlineData("tspin")]
    [InlineData("b2b")]
    [InlineData("combo")]
    [InlineData("garbage")]
    [InlineData("level_up")]
    [InlineData("perfect_clear")]
    [InlineData("game_over")]
    [InlineData("win")]
    public void Render_ProducesAudibleSignal(string name)
    {
        var pcm = AudioSynth.Render(name);

        Assert.NotNull(pcm);
        Assert.NotEmpty(pcm);

        // Must carry real signal, not silence.
        int nonZero = 0;
        short peak = 0;
        foreach (var s in pcm)
        {
            if (s != 0) nonZero++;
            int mag = s < 0 ? -s : s;
            if (mag > peak) peak = (short)mag;
        }
        Assert.True(nonZero > pcm.Length / 10, $"{name} is mostly silent");
        Assert.True(peak > 1000, $"{name} is too quiet (peak {peak})");
    }

    [Fact]
    public void SfxNames_AllRenderable()
    {
        foreach (var name in AudioSynth.SfxNames)
            Assert.NotEmpty(AudioSynth.Render(name));
    }

    [Fact]
    public void Render_UnknownName_FallsBackToBlipNotCrash()
    {
        var pcm = AudioSynth.Render("does_not_exist");
        Assert.NotEmpty(pcm);
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        // Noise voices use a position-seeded LCG, so repeated renders are identical.
        Assert.Equal(AudioSynth.Render("garbage"), AudioSynth.Render("garbage"));
        Assert.Equal(AudioSynth.Render("hard_drop"), AudioSynth.Render("hard_drop"));
    }

    [Fact]
    public void Render_NeverClips()
    {
        // Soft saturation caps output below full-scale; nothing should hit the rail.
        foreach (var name in AudioSynth.SfxNames)
            foreach (var s in AudioSynth.Render(name))
                Assert.True(s > short.MinValue && s < short.MaxValue, $"{name} clipped");
    }

    [Fact]
    public void RenderMusic_IsLongLoopableAndAudible()
    {
        var music = AudioSynth.RenderMusic();

        // A multi-second bed at 44.1kHz is well over 100k samples.
        Assert.True(music.Length > 100_000, $"music too short: {music.Length}");

        // Ends near silence so the loop seam doesn't click.
        int tail = music.Length - 1;
        Assert.True(System.Math.Abs(music[tail]) < 4000, $"loud loop seam: {music[tail]}");

        int nonZero = 0;
        foreach (var s in music) if (s != 0) nonZero++;
        Assert.True(nonZero > music.Length / 4, "music is mostly silent");
    }
}
