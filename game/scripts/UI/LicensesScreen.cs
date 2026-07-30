using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// The in-game attribution screen required before store submission ("open-source license
/// attributions bundled … in an in-game 'Licenses' screen", docs/STORE_SUBMISSION.md).
/// Reached from Settings ▸ HELP.
///
/// <para>WHAT IS ATTRIBUTED, AND WHY ONLY THIS. Blockfall ships almost nothing it did not
/// generate: the rules engine has zero package references, every texture is baked by
/// <see cref="TextureFactory"/> and every sound is synthesised at runtime, so the entire
/// third-party surface is the engine, the .NET runtime, and two typefaces. A screen that
/// invented anything beyond that would be worse than one that listed too little — a false
/// attribution is a claim about someone else's rights.</para>
///
/// <para>THE ENGINE NOTICE IS READ FROM THE ENGINE. <c>Engine.GetLicenseText()</c> and
/// <c>Engine.GetLicenseInfo()</c> return Godot's own copyright block and the full text of every
/// license it bundles for its third-party components. Rendering those instead of a transcription
/// means the notice cannot drift out of date when the engine is upgraded, and cannot be wrong
/// because someone mistyped a year.</para>
///
/// <para>WHAT IS NOT LOCALIZED. Chrome and prose go through <see cref="Loc"/> like every other
/// screen. Copyright lines, license names, and license bodies deliberately do NOT: they are the
/// notices themselves, and a translated MIT license is not the MIT license. Only whitespace is
/// ever touched, by <see cref="Reflow"/>.</para>
/// </summary>
public partial class LicensesScreen : Control
{
    public event Action? BackRequested;

    /// <summary>44pt on the 720×1280 design canvas — the platform minimum touch target,
    /// same constant the settings rows are built on.</summary>
    private const int TouchTarget = 84;

    /// <summary>Column width, matched to <see cref="SettingsScreen"/> so arriving from it
    /// does not shift the page under the player.</summary>
    private const int ColumnWidth = 560;

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Build();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause_game") || e.IsActionPressed("ui_cancel"))
        {
            BackRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Build()
    {
        // TouchScroll, not ScrollContainer: this page is a wall of text under cards, and the
        // stock container only receives drags that land in the gaps between them.
        var scroll = new TouchScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(outer);
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(ColumnWidth, 0),
        };
        col.AddThemeConstantOverride("separation", 16);
        outer.AddChild(col);
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        var title = new Label
        {
            Text = Loc.T("OPEN SOURCE LICENSES"),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", Palette.Accent);
        col.AddChild(title);

        var intro = Body(Loc.T("Blockfall is built on the open-source software below. Every notice is reproduced in full."));
        intro.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(intro);

        BuildEngine(col);
        BuildRuntime(col);
        BuildTypefaces(col);
        BuildEverythingElse(col);
        BuildAcknowledgements(col);

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var back = new Button
        {
            Text = Loc.T("BACK"),
            ThemeTypeVariation = "PrimaryButton",
            CustomMinimumSize = new Vector2(0, TouchTarget),
        };
        Motion.BindButtonFeel(back);
        TouchScroll.Bind(back, () => BackRequested?.Invoke());
        col.AddChild(back);
    }

    // ---- Sections --------------------------------------------------------------

    private void BuildEngine(Container parent)
    {
        var card = SectionCard(parent, "GAME ENGINE");

        // Engine.GetVersionInfo()["string"] is the engine's own self-description
        // ("4.3-stable (official)") — more honest than a hardcoded number that a
        // future upgrade would silently make a lie.
        string version = Engine.GetVersionInfo()["string"].AsString();
        Entry(card, $"Godot Engine {version}", "MIT License", Reflow(Engine.GetLicenseText()));

        // Godot vendors third-party code (its own license list names FreeType and HarfBuzz
        // among others). The engine's copyright block does not cover those, so their texts
        // are carried too — collapsed, because this is an appendix, not something a player
        // is being asked to read.
        var licenses = BundledLicenses();
        int components = Engine.GetCopyrightInfo().Count;
        if (licenses.Count > 0 && components > 0)
        {
            card.AddChild(Divider());
            card.AddChild(Body(Loc.T(
                "The engine bundles {0} third-party components covered by {1} further licenses.",
                components, licenses.Count)));
            AddDisclosure(card, body =>
            {
                foreach (var (name, text) in licenses)
                {
                    body.AddChild(Caption(name));
                    body.AddChild(Body(Reflow(text)));
                }
            });
        }
    }

    private void BuildRuntime(Container parent)
    {
        var card = SectionCard(parent, "RUNTIME");
        // FrameworkDescription is the runtime actually executing this build (".NET 8.0.x"),
        // not the SDK that compiled it.
        string framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        Entry(card, framework, "MIT License", Reflow(DotNetLicense));
    }

    private void BuildTypefaces(Container parent)
    {
        var card = SectionCard(parent, "TYPEFACES");
        // Copyright lines transcribed verbatim from the OFL files that sit beside the fonts in
        // the repo (game/assets/fonts/OFL-Orbitron.txt, OFL-Rajdhani.txt).
        Entry(card, "Orbitron", "SIL Open Font License 1.1",
            "Copyright 2018 The Orbitron Project Authors (https://github.com/theleagueof/orbitron), with Reserved Font Name: \"Orbitron\"");
        card.AddChild(Divider());
        Entry(card, "Rajdhani", "SIL Open Font License 1.1",
            "Copyright (c) 2014, Indian Type Foundry (info@indiantypefoundry.com).");
        card.AddChild(Divider());
        // Clause 2 of the OFL requires the license itself to travel with the font, so the text
        // lives here as a code CONSTANT rather than as a .txt asset. That is not a style
        // preference: a release export was unpacked to check, and all four .ttf files are in
        // the shipped binary while neither OFL-*.txt is. The presets use
        // export_filter="all_resources" with an empty include_filter, and a loose text file is
        // not a resource — so until this screen existed, the fonts shipped with no license at
        // all. (Adding the .txt files to include_filter would be the other fix, but
        // export_presets.cfg is qa-release's file and version-bump automation parses it.)
        AddDisclosure(card, body => body.AddChild(Body(Reflow(OpenFontLicense))));
    }

    private void BuildEverythingElse(Container parent)
    {
        var card = SectionCard(parent, "EVERYTHING ELSE");
        card.AddChild(Body(Loc.T(
            "Nothing else is bundled. The rules engine and the game project reference no third-party packages, and every texture, icon and sound is generated in code while the game runs.")));
    }

    private void BuildAcknowledgements(Container parent)
    {
        var card = SectionCard(parent, "ACKNOWLEDGEMENTS");
        // A credit, not a license: an algorithm carries no notice requirement, but the CPU
        // opponent's judgement is somebody's published work and saying so costs nothing.
        card.AddChild(Body(Loc.T(
            "The CPU opponent ranks candidate placements with a published six-feature board heuristic — landing height, eroded piece cells, row and column transitions, holes and cumulative wells — introduced by Islam El-Ashi as a refinement of Pierre Dellacherie's one-piece placement algorithm. It is implemented independently here; no third-party code is included.")));
    }

    // ---- Builders --------------------------------------------------------------

    /// <summary>A titled group: an accent tab-marker header above a glass card. Mirrors
    /// <see cref="SettingsScreen"/>'s section rhythm so the two screens read as one place.
    /// Returns the rows box.</summary>
    private static VBoxContainer SectionCard(Container parent, string caption)
    {
        var group = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        group.AddThemeConstantOverride("separation", 8);
        parent.AddChild(group);

        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 10);
        header.AddChild(new ColorRect
        {
            Color = Palette.Accent,
            CustomMinimumSize = new Vector2(3, 18),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        });
        header.AddChild(new Label
        {
            Text = Loc.T(caption),
            ThemeTypeVariation = "SectionHeaderLabel",
            VerticalAlignment = VerticalAlignment.Center,
        });
        group.AddChild(header);

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        card.AddChild(box);
        group.AddChild(card);
        return box;
    }

    /// <summary>One attributed component: what it is, the license it is under, and the
    /// notice itself. None of the three is translated — see the class doc.</summary>
    private static void Entry(VBoxContainer card, string name, string license, string notice)
    {
        card.AddChild(Caption(name));
        card.AddChild(new Label { Text = license, ThemeTypeVariation = "SectionLabel" });
        card.AddChild(Body(notice));
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        ThemeTypeVariation = "OptionLabel", // scale-aware — honors the TEXT SIZE slider
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    /// <summary>Wrapping body copy. Never given a font-size override: "DimLabel" is routed
    /// through the theme, so legal text grows with the accessibility TEXT SIZE slider like
    /// everything else. That is only survivable because <see cref="Reflow"/> has already
    /// removed the source files' hard wrap.</summary>
    private static Label Body(string text) => new()
    {
        Text = text,
        ThemeTypeVariation = "DimLabel",
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private static ColorRect Divider() => new()
    {
        Color = new Color(1f, 1f, 1f, 0.05f),
        CustomMinimumSize = new Vector2(0, 1),
    };

    /// <summary>
    /// A "show me the wall of legal text" toggle. The body is built up front but starts
    /// hidden: a container skips invisible children when it measures, so the thousands of
    /// characters underneath cost nothing until they are asked for, while still being real
    /// nodes that the smoke harness walks.
    ///
    /// <para>Wired with <see cref="TouchScroll.Bind"/>, not <c>Pressed +=</c> — a fling that
    /// happens to end on this button would otherwise expand the section.</para>
    /// </summary>
    private static void AddDisclosure(VBoxContainer card, Action<VBoxContainer> build)
    {
        var body = new VBoxContainer { Visible = false };
        body.AddThemeConstantOverride("separation", 10);
        build(body);

        var btn = new Button
        {
            Text = Loc.T(ShowText),
            ThemeTypeVariation = "GhostButton",
            CustomMinimumSize = new Vector2(0, TouchTarget),
        };
        Motion.BindButtonFeel(btn);
        // No reveal animation on purpose: an expanding wall of legal text is the one place
        // motion adds nothing, and a snap needs no Motion.Reduced branch to be correct.
        TouchScroll.Bind(btn, () =>
        {
            body.Visible = !body.Visible;
            btn.Text = Loc.T(body.Visible ? HideText : ShowText);
        });
        card.AddChild(btn);
        card.AddChild(body);
    }

    private const string ShowText = "SHOW FULL LICENSE TEXT";
    private const string HideText = "HIDE FULL LICENSE TEXT";

    // ---- License data ----------------------------------------------------------

    /// <summary>The engine's bundled third-party licenses, name → full text, in a stable
    /// order (the engine hands them back in an unspecified one).</summary>
    private static List<(string Name, string Text)> BundledLicenses()
    {
        var list = new List<(string, string)>();
        foreach (var kv in Engine.GetLicenseInfo())
        {
            string name = kv.Key.AsString();
            string text = kv.Value.AsString();
            if (name.Length > 0 && text.Length > 0) list.Add((name, text));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    /// <summary>
    /// Re-flow a hard-wrapped license file into paragraphs. License texts are wrapped at
    /// ~78 columns by their authors; pasted straight into an autowrapping Label at the
    /// player's chosen text size, every one of those breaks lands mid-column and the result
    /// is a ragged double-wrap. Blank lines separate paragraphs; a line containing no letter
    /// or digit (a rule of dashes) is kept on its own so section banners survive.
    ///
    /// <para>Only whitespace changes. The wording stays byte-for-byte what the upstream
    /// license says, which is the part that legally matters.</para>
    /// </summary>
    private static string Reflow(string src)
    {
        var outp = new StringBuilder(src.Length);
        var para = new StringBuilder(256);

        void Flush()
        {
            if (para.Length == 0) return;
            if (outp.Length > 0) outp.Append("\n\n");
            outp.Append(para);
            para.Clear();
        }

        foreach (var raw in src.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (!HasWordChar(line) || IsHeading(line, para.Length == 0))
            {
                Flush();
                if (outp.Length > 0) outp.Append("\n\n");
                outp.Append(line);
                continue;
            }
            if (para.Length > 0) para.Append(' ');
            para.Append(line);
        }
        Flush();
        return outp.ToString();
    }

    private static bool HasWordChar(string s)
    {
        foreach (char c in s) if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    /// <summary>
    /// A section heading — "PREAMBLE", "DISCLAIMER", "PERMISSION &amp; CONDITIONS" — which must
    /// not be swallowed by the paragraph underneath it, or the license loses its structure.
    ///
    /// <para>Recognised as a short line with no lowercase that OPENS a paragraph. Both halves
    /// are load-bearing: the length cut-off spares the all-caps DISCLAIMER body (its lines run
    /// past 60 characters), and requiring paragraph-start spares a wrapped tail like the MIT
    /// disclaimer's final "SOFTWARE." line, which is short and all-caps but is the end of a
    /// sentence, not the start of a section.</para>
    /// </summary>
    private static bool IsHeading(string line, bool atParagraphStart)
    {
        if (!atParagraphStart || line.Length >= 40) return false;
        foreach (char c in line) if (char.IsLower(c)) return false;
        return true;
    }

    /// <summary>Verbatim from the .NET 8 runtime's LICENSE.txt.</summary>
    private const string DotNetLicense = """
        The MIT License (MIT)

        Copyright (c) .NET Foundation and Contributors

        All rights reserved.

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    /// <summary>The license body from game/assets/fonts/OFL-Orbitron.txt, which is byte-identical
    /// to OFL-Rajdhani.txt below the per-font copyright header (that header is carried on each
    /// typeface's own entry instead). Only the decorative rules of dashes around the title are
    /// dropped; every word of the license is present.</summary>
    private const string OpenFontLicense = """
        SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007

        PREAMBLE
        The goals of the Open Font License (OFL) are to stimulate worldwide
        development of collaborative font projects, to support the font creation
        efforts of academic and linguistic communities, and to provide a free and
        open framework in which fonts may be shared and improved in partnership
        with others.

        The OFL allows the licensed fonts to be used, studied, modified and
        redistributed freely as long as they are not sold by themselves. The
        fonts, including any derivative works, can be bundled, embedded,
        redistributed and/or sold with any software provided that any reserved
        names are not used by derivative works. The fonts and derivatives,
        however, cannot be released under any other type of license. The
        requirement for fonts to remain under this license does not apply
        to any document created using the fonts or their derivatives.

        DEFINITIONS
        "Font Software" refers to the set of files released by the Copyright
        Holder(s) under this license and clearly marked as such. This may
        include source files, build scripts and documentation.

        "Reserved Font Name" refers to any names specified as such after the
        copyright statement(s).

        "Original Version" refers to the collection of Font Software components as
        distributed by the Copyright Holder(s).

        "Modified Version" refers to any derivative made by adding to, deleting,
        or substituting -- in part or in whole -- any of the components of the
        Original Version, by changing formats or by porting the Font Software to a
        new environment.

        "Author" refers to any designer, engineer, programmer, technical
        writer or other person who contributed to the Font Software.

        PERMISSION & CONDITIONS
        Permission is hereby granted, free of charge, to any person obtaining
        a copy of the Font Software, to use, study, copy, merge, embed, modify,
        redistribute, and sell modified and unmodified copies of the Font
        Software, subject to the following conditions:

        1) Neither the Font Software nor any of its individual components,
        in Original or Modified Versions, may be sold by itself.

        2) Original or Modified Versions of the Font Software may be bundled,
        redistributed and/or sold with any software, provided that each copy
        contains the above copyright notice and this license. These can be
        included either as stand-alone text files, human-readable headers or
        in the appropriate machine-readable metadata fields within text or
        binary files as long as those fields can be easily viewed by the user.

        3) No Modified Version of the Font Software may use the Reserved Font
        Name(s) unless explicit written permission is granted by the corresponding
        Copyright Holder. This restriction only applies to the primary font name as
        presented to the users.

        4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
        Software shall not be used to promote, endorse or advertise any
        Modified Version, except to acknowledge the contribution(s) of the
        Copyright Holder(s) and the Author(s) or with their explicit written
        permission.

        5) The Font Software, modified or unmodified, in part or in whole,
        must be distributed entirely under this license, and must not be
        distributed under any other license. The requirement for fonts to
        remain under this license does not apply to any document created
        using the Font Software.

        TERMINATION
        This license becomes null and void if any of the above conditions are
        not met.

        DISCLAIMER
        THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
        EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
        MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
        OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
        COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
        INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
        DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
        FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
        OTHER DEALINGS IN THE FONT SOFTWARE.
        """;
}
