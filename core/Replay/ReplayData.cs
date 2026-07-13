using System;
using System.IO;
using System.IO.Compression;

namespace Blockfall.Core;

/// <summary>
/// A complete, replayable record of one run: the seed + mode + handling that
/// define the simulation, plus the per-tick <see cref="Buttons"/> stream. Because
/// the engine is deterministic under a fixed timestep, re-running this stream
/// reproduces the run exactly — the basis for replays, ghosts, and anti-cheat.
/// Serialization is a compact engine-agnostic binary blob.
/// </summary>
public sealed class ReplayData
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public ulong Seed { get; init; }
    public GameModeId Mode { get; init; }
    public GameModifier[] Modifiers { get; init; } = System.Array.Empty<GameModifier>();

    // Handling that affects the simulation (DAS/ARR) — must match on playback.
    public double Das { get; init; }
    public double Arr { get; init; }

    /// <summary>One <see cref="Buttons"/> byte per fixed tick.</summary>
    public byte[] Inputs { get; init; } = System.Array.Empty<byte>();

    // Result metadata (for listings + an integrity cross-check on playback).
    public long FinalScore { get; init; }
    public int FinalLines { get; init; }
    public double Duration { get; init; }

    public int TickCount => Inputs.Length;

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'B'); w.Write((byte)'R'); // magic "BR"
        w.Write(Version);
        w.Write(Seed);
        w.Write((int)Mode);
        w.Write(Modifiers.Length);
        foreach (var m in Modifiers) w.Write((int)m);
        w.Write(Das);
        w.Write(Arr);
        w.Write(FinalScore);
        w.Write(FinalLines);
        w.Write(Duration);
        w.Write(Inputs.Length);
        w.Write(Inputs);
        w.Flush();
        return ms.ToArray();
    }

    public static ReplayData Deserialize(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        if (r.ReadByte() != 'B' || r.ReadByte() != 'R')
            throw new InvalidDataException("Not a Blockfall replay.");
        int version = r.ReadInt32();
        ulong seed = r.ReadUInt64();
        var mode = (GameModeId)r.ReadInt32();
        int modCount = r.ReadInt32();
        var mods = new GameModifier[modCount];
        for (int i = 0; i < modCount; i++) mods[i] = (GameModifier)r.ReadInt32();
        double das = r.ReadDouble();
        double arr = r.ReadDouble();
        long score = r.ReadInt64();
        int lines = r.ReadInt32();
        double dur = r.ReadDouble();
        int inLen = r.ReadInt32();
        var inputs = r.ReadBytes(inLen);
        return new ReplayData
        {
            Version = version, Seed = seed, Mode = mode, Modifiers = mods,
            Das = das, Arr = arr, FinalScore = score, FinalLines = lines,
            Duration = dur, Inputs = inputs,
        };
    }

    /// <summary>gzip-compressed binary — used for on-disk storage (button streams compress well).</summary>
    public byte[] SerializeCompressed()
    {
        var raw = Serialize();
        using var outMs = new MemoryStream();
        using (var gz = new GZipStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(raw, 0, raw.Length);
        return outMs.ToArray();
    }

    public static ReplayData DeserializeCompressed(byte[] compressed)
    {
        using var inMs = new MemoryStream(compressed);
        using var gz = new GZipStream(inMs, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        return Deserialize(outMs.ToArray());
    }

    /// <summary>A copy-pasteable text code (base64 of the compressed blob) for sharing a replay.</summary>
    public string ToShareCode() => Convert.ToBase64String(SerializeCompressed());

    public static bool TryFromShareCode(string code, out ReplayData? data)
    {
        data = null;
        try
        {
            data = DeserializeCompressed(Convert.FromBase64String(code.Trim()));
            return true;
        }
        catch
        {
            return false; // malformed / truncated code — never throw at the UI
        }
    }
}

/// <summary>Accumulates the per-tick button stream during a live run, then bakes a <see cref="ReplayData"/>.</summary>
public sealed class ReplayRecorder
{
    private readonly System.Collections.Generic.List<byte> _inputs = new(4096);
    private readonly ulong _seed;
    private readonly GameModeId _mode;
    private readonly GameModifier[] _modifiers;
    private readonly double _das, _arr;

    public ReplayRecorder(ulong seed, GameModeId mode, GameModifier[] modifiers, double das, double arr)
    {
        _seed = seed;
        _mode = mode;
        _modifiers = modifiers;
        _das = das;
        _arr = arr;
    }

    public int TickCount => _inputs.Count;

    /// <summary>Record the buttons applied on one tick. Call once per fixed tick.</summary>
    public void Record(Buttons b) => _inputs.Add((byte)b);

    public ReplayData Build(Game finished) => new()
    {
        Seed = _seed, Mode = _mode, Modifiers = _modifiers, Das = _das, Arr = _arr,
        Inputs = _inputs.ToArray(),
        FinalScore = finished.Scoring.Score,
        FinalLines = finished.Scoring.LinesCleared,
        Duration = finished.Elapsed,
    };
}

/// <summary>
/// Deterministically re-simulates a <see cref="ReplayData"/>. Advance it with
/// <see cref="StepOne"/> (for on-screen playback) or <see cref="PlayToEnd"/>
/// (for verification). Rebuilds the exact config the run used.
/// </summary>
public sealed class ReplayPlayer
{
    private readonly ReplayData _data;
    private readonly InputProcessor _proc;
    private int _tick;

    public Game Game { get; }
    public int Tick => _tick;
    public bool Finished => _tick >= _data.Inputs.Length || Game.Status != GameStatus.Running;
    public double Progress => _data.Inputs.Length == 0 ? 1.0 : (double)_tick / _data.Inputs.Length;

    public ReplayPlayer(ReplayData data)
    {
        _data = data;
        // Reconstruct the exact simulation config: mode base + player handling + modifiers,
        // applied in the same order the live GameController uses.
        var cfg = GameMode.ById(data.Mode).Config.With(das: data.Das, arr: data.Arr);
        if (data.Modifiers.Length > 0) cfg = ModifierSet.Apply(cfg, data.Modifiers);
        Game = Game.Create(data.Mode, data.Seed, cfg);
        _proc = new InputProcessor(Game.Config);
        Game.Start();
    }

    public void StepOne()
    {
        if (Finished) return;
        var b = (Buttons)_data.Inputs[_tick++];
        _proc.Step(b, Game);
        Game.Update(Sim.TickDt);
    }

    public Game PlayToEnd()
    {
        while (!Finished) StepOne();
        return Game;
    }
}
