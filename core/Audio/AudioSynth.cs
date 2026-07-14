namespace Blockfall.Core.Audio;

/// <summary>
/// Engine-agnostic procedural audio. Renders every game sound as 16-bit mono PCM
/// at runtime, so the game ships with a full soundtrack and SFX set without any
/// binary audio assets (nothing to license, localize, or lazy-load). The Godot
/// layer wraps these samples in an <c>AudioStreamWAV</c>; the core stays pure C#
/// and is unit-tested for shape and signal.
///
/// The palette is a deliberate neon-chiptune: bright triangle/square leads, a
/// little noise for impacts, and short click-free envelopes on everything.
/// </summary>
public static class AudioSynth
{
    public const int SampleRate = 44100;
    private const double TwoPi = System.Math.PI * 2;

    /// <summary>Oscillator shapes used by the voices.</summary>
    public enum Wave { Sine, Square, Triangle, Saw }

    // ---- Note helpers (equal temperament, A4 = 440Hz) -----------------------
    private static double Hz(int semitonesFromA4) => 440.0 * System.Math.Pow(2.0, semitonesFromA4 / 12.0);
    private static readonly double C4 = Hz(-9), Ds4 = Hz(-6), F4 = Hz(-4), G4 = Hz(-2), A4 = Hz(0);
    private static readonly double C5 = Hz(3), D5 = Hz(5), E5 = Hz(7), F5 = Hz(8), G5 = Hz(10), A5 = Hz(12), B5 = Hz(14);
    private static readonly double C6 = Hz(15), E6 = Hz(19), G6 = Hz(22);

    /// <summary>The names the game asks for; unknown names fall back to a soft blip.</summary>
    public static readonly string[] SfxNames =
    {
        "move", "rotate", "lock", "hard_drop", "hold", "line_clear", "tetris",
        "tspin", "b2b", "combo", "garbage", "level_up", "perfect_clear",
        "game_over", "win",
    };

    /// <summary>Renders a named SFX to 16-bit PCM. Never returns null or empty.</summary>
    public static short[] Render(string name)
    {
        switch (name)
        {
            case "move":
                return new Buf(0.035)
                    .Tone(220, 0, 0.03, 0.16, Wave.Square, release: 0.02).To16();

            case "rotate":
                return new Buf(0.06)
                    .Tone(300, 0, 0.05, 0.18, Wave.Triangle, glideTo: 520, release: 0.03).To16();

            case "lock":
                return new Buf(0.06)
                    .Tone(150, 0, 0.045, 0.22, Wave.Square, release: 0.03)
                    .Noise(0, 0.02, 0.10).To16();

            case "hard_drop":
                return new Buf(0.14)
                    .Tone(320, 0, 0.11, 0.26, Wave.Saw, glideTo: 70, release: 0.05)
                    .Noise(0, 0.06, 0.16).To16();

            case "hold":
                return new Buf(0.11)
                    .Tone(A4, 0, 0.04, 0.18, Wave.Triangle, release: 0.02)
                    .Tone(E5, 0.05, 0.05, 0.18, Wave.Triangle, release: 0.03).To16();

            case "line_clear":
                return new Buf(0.26)
                    .Tone(C5, 0, 0.24, 0.11, Wave.Triangle, glideTo: C5 * 1.03, release: 0.10)
                    .Tone(E5, 0, 0.24, 0.10, Wave.Triangle, release: 0.10)
                    .Tone(G5, 0, 0.24, 0.09, Wave.Sine, release: 0.10).To16();

            case "tetris":
                return new Buf(0.42)
                    .Tone(C5, 0.00, 0.10, 0.20, Wave.Square, release: 0.04)
                    .Tone(E5, 0.09, 0.10, 0.20, Wave.Square, release: 0.04)
                    .Tone(G5, 0.18, 0.10, 0.20, Wave.Square, release: 0.04)
                    .Tone(C6, 0.27, 0.15, 0.22, Wave.Triangle, release: 0.08)
                    .Tone(G5, 0.27, 0.15, 0.10, Wave.Sine, release: 0.08).To16();

            case "tspin":
                return new Buf(0.34)
                    .Tone(A5, 0.00, 0.32, 0.14, Wave.Sine, glideTo: A5 * 1.5, vibrato: 6, release: 0.14)
                    .Tone(E6, 0.05, 0.26, 0.10, Wave.Triangle, glideTo: G6, release: 0.14).To16();

            case "b2b":
                return new Buf(0.22)
                    .Tone(C6, 0, 0.20, 0.13, Wave.Sine, release: 0.12)
                    .Tone(G6, 0.03, 0.17, 0.10, Wave.Triangle, release: 0.12).To16();

            case "combo":
                return new Buf(0.12)
                    .Tone(600, 0, 0.10, 0.18, Wave.Triangle, glideTo: 1000, release: 0.05).To16();

            case "garbage":
                return new Buf(0.24)
                    .Tone(60, 0, 0.22, 0.24, Wave.Saw, release: 0.10)
                    .Noise(0, 0.20, 0.14).To16();

            case "level_up":
                return new Buf(0.5)
                    .Tone(C5, 0.00, 0.10, 0.18, Wave.Square, release: 0.04)
                    .Tone(E5, 0.09, 0.10, 0.18, Wave.Square, release: 0.04)
                    .Tone(G5, 0.18, 0.10, 0.18, Wave.Square, release: 0.04)
                    .Tone(C6, 0.27, 0.10, 0.18, Wave.Square, release: 0.04)
                    .Tone(E6, 0.36, 0.14, 0.20, Wave.Triangle, release: 0.08).To16();

            case "perfect_clear":
                return new Buf(0.65)
                    .Tone(C5, 0, 0.6, 0.11, Wave.Triangle, release: 0.30)
                    .Tone(E5, 0, 0.6, 0.10, Wave.Triangle, release: 0.30)
                    .Tone(G5, 0, 0.6, 0.10, Wave.Sine, release: 0.30)
                    .Tone(C6, 0, 0.6, 0.09, Wave.Sine, release: 0.30)
                    .Tone(G6, 0.30, 0.30, 0.08, Wave.Triangle, vibrato: 5, release: 0.20).To16();

            case "game_over":
                return new Buf(0.75)
                    .Tone(G4, 0.00, 0.22, 0.20, Wave.Triangle, release: 0.10)
                    .Tone(Ds4, 0.20, 0.22, 0.20, Wave.Triangle, release: 0.10)
                    .Tone(C4, 0.40, 0.34, 0.22, Wave.Triangle, glideTo: C4 * 0.94, release: 0.24).To16();

            case "win":
                return new Buf(0.85)
                    .Tone(C5, 0.00, 0.12, 0.19, Wave.Square, release: 0.05)
                    .Tone(E5, 0.12, 0.12, 0.19, Wave.Square, release: 0.05)
                    .Tone(G5, 0.24, 0.12, 0.19, Wave.Square, release: 0.05)
                    .Tone(C6, 0.36, 0.40, 0.22, Wave.Triangle, release: 0.24)
                    .Tone(E6, 0.36, 0.40, 0.12, Wave.Sine, release: 0.24)
                    .Tone(G6, 0.36, 0.40, 0.09, Wave.Sine, release: 0.24).To16();

            default:
                return new Buf(0.03).Tone(440, 0, 0.025, 0.12, Wave.Sine, release: 0.02).To16();
        }
    }

    // ---- Music -------------------------------------------------------------
    // A loopable neon chiptune bed: an Am–F–C–G progression with an arpeggiated
    // triangle lead over a square bass. Rendered once and looped by the player.

    /// <summary>Renders a seamless ~8s looping music bed as 16-bit PCM.</summary>
    public static short[] RenderMusic()
    {
        // Retrowave loop in A minor (i–VI–III–VII = Am F C G): a sub + a driving
        // 8th-note pulse bass, a soft sustained pad, a 16th-note arpeggio shimmer,
        // a light four-on-the-floor groove, and a lead riff on the repeat — a fuller,
        // more modern take than a lone arpeggio. Every voice uses the current chord's
        // own tones, so the loop stays consonant across the progression.
        int[] roots = { 0, -4, 3, -2 };   // A F C G (semitones from A4)
        int[] third = { 3, 4, 4, 3 };     // 3rd above the root: Am / F / C / G

        const double bpm = 110.0;
        double six = 60.0 / bpm / 4.0;    // sixteenth-note length (seconds)
        double barLen = 16 * six;         // one chord per bar
        int bars = roots.Length * 2;      // 8-bar loop (progression twice)
        var buf = new Buf(barLen * bars + 0.05);

        for (int bar = 0; bar < bars; bar++)
        {
            double t0 = bar * barLen;
            int r = roots[bar % roots.Length];
            int t3 = third[bar % roots.Length];
            bool repeat = bar >= roots.Length;

            // Sub bass (foundation) + driving 8th-note pulse bass.
            buf.Tone(Hz(r - 24), t0, barLen - 0.01, 0.10, Wave.Sine, release: 0.20);
            for (int e = 0; e < 8; e++)
                buf.Tone(Hz(r - 12), t0 + e * 2 * six, six * 1.5, 0.062, Wave.Square, release: 0.05);

            // Soft sustained pad (root / 3rd / 5th).
            buf.Tone(Hz(r),      t0, barLen - 0.02, 0.032, Wave.Triangle, release: 0.28);
            buf.Tone(Hz(r + t3), t0, barLen - 0.02, 0.028, Wave.Triangle, release: 0.28);
            buf.Tone(Hz(r + 7),  t0, barLen - 0.02, 0.028, Wave.Sine, release: 0.28);

            // Arpeggio shimmer, up an octave, cycling the chord's own tones.
            int[] arp = { 0, t3, 7, 12, 7, t3, 12, 7 };
            for (int s = 0; s < 16; s++)
                buf.Tone(Hz(r + 12 + arp[s % arp.Length]), t0 + s * six, six * 0.9, 0.036, Wave.Saw, release: 0.04);

            // Lead riff on the repeat (chord tones, up high, gentle vibrato).
            if (repeat)
            {
                int[] ln = { 12, 12 + t3, 19, 24 };
                double[] lt = { 0, 4, 8, 12 };
                for (int i = 0; i < 4; i++)
                    buf.Tone(Hz(r + ln[i]), t0 + lt[i] * six, six * 3.2, 0.055, Wave.Triangle, vibrato: 4, release: 0.14);
            }

            // Groove: kick on beats 1 & 3, snare on 2 & 4, hats on the off-eighths.
            for (int b = 0; b < 4; b++)
            {
                double bt = t0 + b * 4 * six;
                if (b % 2 == 0) buf.Tone(60, bt, 0.12, 0.15, Wave.Sine, glideTo: 40, release: 0.06);
                else buf.Noise(bt, 0.09, 0.075);
            }
            for (int h = 0; h < 8; h++)
                buf.Noise(t0 + (h * 2 + 1) * six, 0.018, 0.026);
        }
        return buf.To16();
    }

    // ---- Synthesis engine --------------------------------------------------
    private sealed class Buf
    {
        private readonly double[] _s;

        public Buf(double seconds) => _s = new double[System.Math.Max(1, (int)(seconds * SampleRate))];

        /// <summary>Adds one enveloped, optionally gliding/vibrato voice into the mix.</summary>
        public Buf Tone(double freq, double start, double dur, double amp, Wave wave = Wave.Sine,
                        double glideTo = -1, double vibrato = 0, double attack = 0.004, double release = 0.04)
        {
            int startI = (int)(start * SampleRate);
            int n = (int)(dur * SampleRate);
            if (n <= 0) return this;
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = startI + i;
                if (idx < 0) continue;
                if (idx >= _s.Length) break;
                double frac = (double)i / n;
                double f = glideTo < 0 ? freq : freq + (glideTo - freq) * frac;
                if (vibrato > 0) f *= 1.0 + 0.02 * System.Math.Sin(TwoPi * vibrato * i / SampleRate);
                phase += TwoPi * f / SampleRate;
                _s[idx] += amp * Env(i, n, attack, release) * Osc(wave, phase);
            }
            return this;
        }

        /// <summary>Adds a deterministic white-noise burst (impacts, garbage).</summary>
        public Buf Noise(double start, double dur, double amp, double attack = 0.002, double release = 0.05)
        {
            int startI = (int)(start * SampleRate);
            int n = (int)(dur * SampleRate);
            if (n <= 0) return this;
            uint rng = 0x9E3779B9u ^ (uint)startI; // deterministic per position
            for (int i = 0; i < n; i++)
            {
                int idx = startI + i;
                if (idx < 0) continue;
                if (idx >= _s.Length) break;
                rng = rng * 1664525u + 1013904223u;
                double white = (rng / (double)uint.MaxValue) * 2.0 - 1.0;
                _s[idx] += amp * Env(i, n, attack, release) * white;
            }
            return this;
        }

        public short[] To16()
        {
            var outp = new short[_s.Length];
            for (int i = 0; i < _s.Length; i++)
            {
                double v = _s[i];
                // Soft saturation keeps sums musical and guarantees no hard clip.
                v = System.Math.Tanh(v * 1.1);
                if (v > 1) v = 1; else if (v < -1) v = -1;
                outp[i] = (short)(v * 32000);
            }
            return outp;
        }

        private static double Env(int i, int n, double attack, double release)
        {
            double tSec = (double)i / SampleRate;
            double durSec = (double)n / SampleRate;
            double a = attack <= 0 ? 1.0 : System.Math.Min(1.0, tSec / attack);
            double relStart = durSec - release;
            double r = release <= 0 ? 1.0 : (tSec < relStart ? 1.0 : System.Math.Max(0.0, (durSec - tSec) / release));
            return a * r;
        }

        private static double Osc(Wave wave, double phase)
        {
            switch (wave)
            {
                case Wave.Square: return System.Math.Sin(phase) >= 0 ? 1.0 : -1.0;
                case Wave.Triangle: return 2.0 / System.Math.PI * System.Math.Asin(System.Math.Sin(phase));
                case Wave.Saw:
                    double p = phase / TwoPi;
                    return 2.0 * (p - System.Math.Floor(p + 0.5));
                default: return System.Math.Sin(phase);
            }
        }
    }
}
