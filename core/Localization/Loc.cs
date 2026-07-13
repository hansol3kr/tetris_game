using System;
using System.Collections.Generic;

namespace Blockfall.Core.Localization;

/// <summary>Languages the UI can display, in picker order. Append-only (persisted as int).</summary>
public enum Language
{
    English,
    Korean,
}

/// <summary>
/// Lightweight UI localization. Strings are keyed by their English source text,
/// so any string that is never wrapped — or has no translation yet — degrades
/// gracefully to English rather than showing a raw key. Engine-agnostic and
/// unit-tested; the Godot layer just sets <see cref="Current"/> from settings and
/// listens to <see cref="Changed"/> to rebuild open screens.
/// </summary>
public static class Loc
{
    private static Language _current = Language.English;

    /// <summary>Raised after the language actually changes so screens can rebuild.</summary>
    public static event Action? Changed;

    public static Language Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke();
        }
    }

    private static readonly Dictionary<Language, Dictionary<string, string>> Tables = new()
    {
        [Language.Korean] = LocData.Korean,
    };

    /// <summary>Translate an English UI string to the current language (English fallback).</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english)) return english ?? "";
        return Tables.TryGetValue(_current, out var table) && table.TryGetValue(english, out var t)
            ? t
            : english;
    }

    /// <summary>Translate a format template, then fill it (e.g. <c>T("Level {0}", n)</c>).</summary>
    public static string T(string englishTemplate, params object[] args)
        => string.Format(T(englishTemplate), args);

    /// <summary>The languages offered in the settings picker, in display order.</summary>
    public static IReadOnlyList<Language> Available { get; } = new[] { Language.English, Language.Korean };

    /// <summary>The endonym shown for a language in the picker.</summary>
    public static string DisplayName(Language lang) => lang switch
    {
        Language.Korean => "한국어",
        _ => "English",
    };
}
