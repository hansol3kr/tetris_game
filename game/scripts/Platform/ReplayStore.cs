using Godot;
using System.Collections.Generic;
using Blockfall.Core;

namespace Blockfall.Platform;

/// <summary>
/// On-disk replay library under <c>user://replays</c>. Replays are stored gzip-
/// compressed (.brp); the headline metadata (mode / score / lines / time) is encoded
/// in the filename so the browser can list without decompressing every file. Keeps
/// the most recent <see cref="MaxKeep"/> to bound disk use.
/// </summary>
public static class ReplayStore
{
    private const string Dir = "user://replays";
    private const int MaxKeep = 40;

    public readonly struct Entry
    {
        public string Path { get; init; }
        public GameModeId Mode { get; init; }
        public long Score { get; init; }
        public int Lines { get; init; }
        public long UnixTime { get; init; }
    }

    private static void EnsureDir()
    {
        if (!DirAccess.DirExistsAbsolute(Dir))
            DirAccess.MakeDirRecursiveAbsolute(Dir);
    }

    /// <summary>Persist a replay; returns its path (or null on failure).</summary>
    public static string? Save(ReplayData data)
    {
        EnsureDir();
        long unix = (long)Time.GetUnixTimeFromSystem();
        string name = $"{unix}__{(int)data.Mode}__{data.FinalScore}__{data.FinalLines}.brp";
        string path = $"{Dir}/{name}";
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (f is null) return null;
            f.StoreBuffer(data.SerializeCompressed());
        }
        catch (System.Exception e) { GD.PushWarning($"Replay save failed: {e.Message}"); return null; }
        Prune();
        return path;
    }

    /// <summary>All saved replays, newest first.</summary>
    public static List<Entry> List()
    {
        var list = new List<Entry>();
        if (!DirAccess.DirExistsAbsolute(Dir)) return list;
        using var dir = DirAccess.Open(Dir);
        if (dir is null) return list;
        foreach (var file in dir.GetFiles())
        {
            if (!file.EndsWith(".brp")) continue;
            if (TryParse(file, out var entry)) list.Add(entry);
        }
        list.Sort((a, b) => b.UnixTime.CompareTo(a.UnixTime));
        return list;
    }

    private static bool TryParse(string file, out Entry entry)
    {
        entry = default;
        var stem = file.Substring(0, file.Length - 4); // drop ".brp"
        var parts = stem.Split("__");
        if (parts.Length != 4) return false;
        if (!long.TryParse(parts[0], out var unix)) return false;
        if (!int.TryParse(parts[1], out var mode)) return false;
        if (!long.TryParse(parts[2], out var score)) return false;
        if (!int.TryParse(parts[3], out var lines)) return false;
        entry = new Entry { Path = $"{Dir}/{file}", Mode = (GameModeId)mode, Score = score, Lines = lines, UnixTime = unix };
        return true;
    }

    public static ReplayData? Load(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f is null) return null;
            var bytes = f.GetBuffer((long)f.GetLength());
            return ReplayData.DeserializeCompressed(bytes);
        }
        catch (System.Exception e) { GD.PushWarning($"Replay load failed: {e.Message}"); return null; }
    }

    public static void Delete(string path)
    {
        if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(path);
    }

    private static void Prune()
    {
        var all = List();
        for (int i = MaxKeep; i < all.Count; i++)
            Delete(all[i].Path);
    }
}
