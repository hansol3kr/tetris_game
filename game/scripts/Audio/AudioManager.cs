using Godot;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Core.Audio;
using Blockfall.Platform;

namespace Blockfall.Audio;

/// <summary>
/// Music + SFX playback. Every sound is synthesized at runtime by
/// <see cref="AudioSynth"/> (pure C#), so the game ships with a full soundtrack
/// and no binary audio assets to license or load. Streams are built once and
/// cached. A small voice pool prevents SFX from cutting each other off, and
/// line-clear feedback escalates with the size/quality of the clear for juice.
/// </summary>
public partial class AudioManager : Node
{
    private readonly List<AudioStreamPlayer> _sfxPool = new();
    private readonly Dictionary<string, AudioStream> _sfxCache = new();
    private int _poolIndex;
    private AudioStreamPlayer _music = null!;
    private AudioStream? _musicStream;
    private string _currentMusic = "";

    private float _sfxVolumeDb;
    private float _musicVolumeDb = -6f;
    private bool _muted;

    public override void _Ready()
    {
        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master" };
            AddChild(p);
            _sfxPool.Add(p);
        }
        _music = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_music);

        // Restore volume prefs from settings.
        ApplySettings(Bootstrap.Instance.Save.Settings);
    }

    public void ApplySettings(GameSettings s)
    {
        bool wasMuted = _muted;
        _muted = s.Muted;
        _sfxVolumeDb = LinearToDb(s.SfxVolume);
        _musicVolumeDb = LinearToDb(s.MusicVolume) - 6f;
        _music.VolumeDb = _musicVolumeDb;
        // React to a mute toggle immediately rather than only on the next call.
        if (_muted && !wasMuted) _music.Stop();
        else if (!_muted && wasMuted && _currentMusic.Length > 0) PlayMusic(_currentMusic);
    }

    public void PlaySfx(string name) => PlaySfx(name, 1f);

    /// <summary>
    /// Play a cue re-pitched at playback time. This is how a block skin's MATERIAL gets a
    /// voice — wood locks lower, glass tube higher — without touching the synth: one cached
    /// waveform, only <c>PitchScale</c> changes, so <c>core/Audio/AudioSynth.cs</c> and its
    /// determinism-adjacent render path stay untouched. Pitch is clamped to a musical range
    /// so a bad caller can't produce a click or a sub-bass thud.
    /// </summary>
    public void PlaySfx(string name, float pitch, float dbOffset = 0f)
    {
        if (_muted) return;
        var stream = SfxStream(name);
        var player = _sfxPool[_poolIndex];
        _poolIndex = (_poolIndex + 1) % _sfxPool.Count;
        player.Stream = stream;
        player.PitchScale = Mathf.Clamp(pitch, 0.5f, 2f);
        player.VolumeDb = _sfxVolumeDb + dbOffset;
        player.Play();
    }

    /// <summary>The lock-sound pitch a block finish speaks at — the audio half of the material
    /// axis. Composed with the player's pack inside <see cref="PlayPlace"/>, which is the only
    /// caller: the two axes are orthogonal (the PACK picks the waveform, the MATERIAL transposes
    /// it), and routing every placement through one method is what stopped the packs from being
    /// audible in the settings screen and nowhere else.</summary>
    public static float MaterialPitch(Blockfall.Theme.CellMaterial m) => m switch
    {
        Blockfall.Theme.CellMaterial.Wood => 0.78f,
        Blockfall.Theme.CellMaterial.Frosted => 1.10f,
        Blockfall.Theme.CellMaterial.Metallic => 1.18f,
        Blockfall.Theme.CellMaterial.Gemstone => 1.22f,
        Blockfall.Theme.CellMaterial.NeonTube => 1.30f,
        Blockfall.Theme.CellMaterial.Pelt => 0.86f,      // muffled, absorbent
        Blockfall.Theme.CellMaterial.Scale => 1.16f,     // wet click
        Blockfall.Theme.CellMaterial.Chitin => 1.26f,    // dry shell tick
        _ => 1.00f,
    };

    // ---- Placement sound packs ------------------------------------------------
    // A pack changes only the TIMBRE of "a piece landed". It never changes the
    // game's meaning map: merge still speaks with "hold", a snap-back with "move",
    // a clear with "line_clear"/"combo". Mechanical layer = player taste, musical
    // layer = the game's own voice. Index 0 is the shipped "lock" cue, so an
    // untouched save sounds exactly as it did before this feature existed.

    /// <summary>Display names for the placement packs, indexed by <c>GameSettings.SfxPack</c>.</summary>
    public static readonly string[] PackNames = { "NEON", "TAP", "CLICK", "TYPEBAR", "WOOD" };

    /// <summary>Synth cue behind each pack — same order as <see cref="PackNames"/>.</summary>
    private static readonly string[] PackCues = { "lock", "place_tap", "place_click", "place_typebar", "place_wood" };

    // Deterministic 4-step detune rotation (0, +18, -22, +9 cents). Repeating one
    // sample verbatim several times a second sounds like a machine gun; a tiny
    // pitch walk sounds like a hand. A counter, not RNG and not wall-clock, so the
    // determinism firewall (XorShiftRandom + fixed tick) is untouched — this state
    // is view-local and never reaches the sim.
    private static readonly float[] Detune = { 1.000f, 1.0105f, 0.9874f, 1.0052f };
    private int _detune;

    private float DetuneNext()
    {
        var v = Detune[_detune];
        _detune = (_detune + 1) % Detune.Length;
        return v;
    }

    /// <summary>
    /// The one call every "piece went into the field" moment should make. Picks the
    /// player's pack, composes its timbre with the equipped skin's material pitch
    /// (wood skin + WOOD pack = the thickest knock), and walks the detune rotation.
    /// <paramref name="lift"/> is the pickup (upstroke) cue: real keys sound quieter
    /// and higher going up than coming down, and today pickup and snap-back are both
    /// "move", so the player can't hear the difference between grabbing a piece and
    /// being refused one.
    /// </summary>
    public void PlayPlace(bool lift = false)
    {
        int idx = Mathf.Clamp(Bootstrap.Instance.Save.Settings.SfxPack, 0, PackCues.Length - 1);
        if (lift)
        {
            // Deliberately NOT multiplied by MaterialPitch. NeonTube (1.30) x 1.5 = 1.95
            // would push the CLICK pack's 2.1kHz square edge to 4.1kHz — the exact band a
            // phone speaker turns into an ice pick. Making this "consistent" with the
            // placement branch later is the realistic regression path; don't.
            PlaySfx(PackCues[idx], 1.5f, -7f);
            return;
        }
        PlaySfx(PackCues[idx], MaterialPitch(Blockfall.Theme.Palette.EquippedMaterial) * DetuneNext());
    }

    // ---- Block Fit: the fuse verb ------------------------------------------------
    // Both Block Fit screens route through these two so the meaning map can't drift between
    // solo and the duel. Deliberately NOT pack-dependent: a pack is the mechanical layer
    // ("a piece landed"), and fusing is a rule event — it speaks with the game's own voice.

    /// <summary>A fuse committed. Its own cue, not the "hold" stash cue it used to borrow:
    /// stashing is free and reversible, fusing spends a finite charge and consumes two pieces.</summary>
    public void PlayFuse() => PlaySfx("fuse");

    /// <summary>A fuse refused because the budget is spent — the same cue an octave down and
    /// quieter, i.e. the verb itself sagging. It must NOT be the generic "move" snap-back: that
    /// is what made "you are out of fuses" sound exactly like "these two pieces don't fit".</summary>
    public void PlayFuseDenied() => PlaySfx("fuse", 0.55f, -4f);

    private AudioStream SfxStream(string name)
    {
        if (_sfxCache.TryGetValue(name, out var cached)) return cached;
        var stream = MakeWav(AudioSynth.Render(name), loop: false);
        _sfxCache[name] = stream;
        return stream;
    }

    /// <summary>Chooses an escalating clear sound: bigger + spin + B2B => punchier.</summary>
    public void PlayLineClear(ClearResult r)
    {
        if (r.Spin != SpinType.None && r.LinesCleared > 0) PlaySfx("tspin");
        else if (r.LinesCleared >= 4) PlaySfx("quad");
        else PlaySfx("line_clear");

        if (r.BackToBack) PlaySfx("b2b");
        if (r.ComboCount >= 2) PlaySfx($"combo");
    }

    public void PlayMusic(string name)
    {
        if (_muted) { _music.Stop(); return; }
        // Rebuild only when the track changes; otherwise let it keep looping.
        if (_musicStream is null || _currentMusic != name)
        {
            _musicStream = MakeWav(AudioSynth.RenderMusic(), loop: true);
            _currentMusic = name;
        }
        _music.Stream = _musicStream;
        _music.VolumeDb = _musicVolumeDb;
        if (!_music.Playing) _music.Play();
    }

    public void StopMusic() => _music.Stop();

    /// <summary>Wraps 16-bit mono PCM samples in a Godot stream (looping optional).</summary>
    private static AudioStreamWav MakeWav(short[] samples, bool loop)
    {
        var bytes = new byte[samples.Length * 2];
        System.Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = AudioSynth.SampleRate,
            Stereo = false,
            Data = bytes,
        };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = samples.Length;
        }
        return wav;
    }

    private static float LinearToDb(float linear)
        => linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear);
}
