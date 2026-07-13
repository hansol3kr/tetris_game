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

    public void PlaySfx(string name)
    {
        if (_muted) return;
        var stream = SfxStream(name);
        var player = _sfxPool[_poolIndex];
        _poolIndex = (_poolIndex + 1) % _sfxPool.Count;
        player.Stream = stream;
        player.VolumeDb = _sfxVolumeDb;
        player.Play();
    }

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
        else if (r.LinesCleared >= 4) PlaySfx("tetris");
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
