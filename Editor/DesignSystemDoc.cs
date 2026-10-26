using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes DESIGN_SYSTEM.md: what the game ships — theme tokens, named sizes, type scale and every style — with the
/// Figma name each should match and, once a Figma sync has run, how it compares. It opens with a brief for a
/// designer's AI agent, so the designer can hand the file to an agent with a Figma connector and get a comparison
/// with the Figma library. Generated from the assets, never edited by hand; tokens.json beside it has exact values.
/// </summary>
public static class DesignSystemDoc
{
    /// <summary>Set per project in Design System Settings.</summary>
    public static string Folder => DesignSystemSettings.Find()?.DocumentFolder ?? "docs/design-system";
    public const string FileName = "DESIGN_SYSTEM.md";
    public const string NotCompared = "not compared";

    [MenuItem(DesignSystemMenu.Root + "Write Design System Doc", priority = 4)]
    private static void WriteFromMenu()
    {
        string path = Write();
        Debug.Log($"[DesignSystem] Wrote {path} and {Folder}/tokens.json.");
        EditorUtility.RevealInFinder(path);
    }

    /// <summary>
    /// Writes the doc and tokens.json for the project's theme and styles. Without a report, the last Figma snapshot
    /// is compared (read-only) when there is one. Returns the doc's path.
    /// </summary>
    public static string Write(bool countUses = true, FigmaReport report = null)
    {
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        List<UIComponentStyle> styles = DesignSystemAssets.FindAll<UIComponentStyle>();
        Dictionary<UIComponentStyle, int> uses = countUses
            ? StyleConsolidation.MeasureUsage().Where(u => u.Style != null).ToDictionary(u => u.Style, u => u.References)
            : null;

        report ??= FigmaSync.Preview();
        List<FigmaAdaptation> adaptations = FigmaSync.Load<List<FigmaAdaptation>>(FigmaSync.AdaptationsPath);
        List<FigmaPlatformRule> rules = FigmaSync.Load<List<FigmaPlatformRule>>(FigmaSync.RulesPath);

        Directory.CreateDirectory(Folder);
        string path = $"{Folder}/{FileName}";
        File.WriteAllText(path, Generate(theme, styles, uses, DateTime.Now, report, adaptations, DesignSystemSettings.Find()?.DocumentTitle,
                                         DesignSystemAssets.FindAll<UIStateStyle>(), rules));
        if (theme != null) File.WriteAllText($"{Folder}/tokens.json", DesignTokenJson.Export(theme));
        return path;
    }

    public static string Generate(UITheme theme, IEnumerable<UIComponentStyle> styles, IReadOnlyDictionary<UIComponentStyle, int> uses, DateTime when,
                                  FigmaReport report = null, IList<FigmaAdaptation> adaptations = null, string title = null,
                                  IEnumerable<UIStateStyle> stateStyles = null, IList<FigmaPlatformRule> rules = null)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {title ?? "Design System"}");
        md.AppendLine();
        md.AppendLine($"> Generated from the Unity project on {when:yyyy-MM-dd HH:mm}. Don't edit this file by hand: run " +
                      $"**{DesignSystemMenu.Path("Write Design System Doc")}** to regenerate it.");
        md.AppendLine(">");
        if (report == null)
            md.AppendLine("> **Figma sync: not run yet.** The Figma columns show the name each item should have in Figma; " +
                          $"every status reads \"{NotCompared}\" until the first sync.");
        else
            md.AppendLine($"> **Figma sync:** {report.snapshot.fileName} (version {report.snapshot.figmaVersion}), read {report.snapshot.syncedAt}. " +
                          (report.hasBaseline ? "Compared with the last accepted sync." :
                              "**First sync:** there is no baseline yet, so every difference the platform rules don't cover is listed for the game to follow Figma."));
        md.AppendLine();
        AppendAtAGlance(md, report, theme, rules);
        AppendBrief(md);
        AppendNaming(md);
        if (theme != null) AppendTheme(md, theme);
        else md.AppendLine("## Theme\n\nNo UI Theme asset was found in the project.\n");
        AppendComponents(md, theme, styles.Where(s => s != null).ToList(), uses, report);
        AppendStateStyles(md, theme, stateStyles?.Where(s => s != null).ToList());
        if (report != null) AppendComparison(md, report, theme);
        AppendRules(md, rules);
        AppendAdaptations(md, adaptations, rules);
        AppendSinceBaseline(md, report);
        return md.ToString();
    }

    private static readonly FigmaResult[] OnPurpose = { FigmaResult.Adapted, FigmaResult.AdaptedFigmaMoved };

    /// <summary>Every compared value in one of three groups, with what the game must do for each difference no agreed rule covers.</summary>
    private static void AppendAtAGlance(StringBuilder md, FigmaReport report, UITheme theme, IList<FigmaPlatformRule> rules)
    {
        md.AppendLine("## At a glance");
        md.AppendLine();
        if (report == null)
        {
            md.AppendLine("No Figma sync yet. After the first one, this section sorts every compared value into three groups: " +
                          "**matches**, **differs by platform rule** and **to do**.");
            md.AppendLine();
            return;
        }

        List<FigmaFieldRow> matches = report.rows.Where(r => r.result == FigmaResult.InSync).ToList();
        List<FigmaFieldRow> onPurpose = report.rows.Where(r => OnPurpose.Contains(r.result)).ToList();
        List<FigmaFieldRow> decide = report.rows.Where(r => r.result != FigmaResult.InSync && !OnPurpose.Contains(r.result)).ToList();
        List<FigmaFieldRow> unconfirmed = report.rows.Where(r => !r.mappingConfirmed).GroupBy(r => r.style).Select(g => g.First()).ToList();
        var outside = FigmaSync.ColoursOutsideTheme(report.snapshot, theme);
        int decisions = decide.Count + unconfirmed.Count + (outside.Count > 0 ? 1 : 0);

        md.AppendLine("| Matches | Differs by platform rule | To do |");
        md.AppendLine("|---|---|---|");
        md.AppendLine($"| {matches.Count} value(s) | {onPurpose.Count} value(s) | {decisions} item(s) |");
        md.AppendLine();

        md.AppendLine("### To do");
        md.AppendLine();
        md.AppendLine("Work for a developer. The game follows Figma unless an agreed platform rule covers the difference; nothing here waits on the designer.");
        md.AppendLine();
        if (decisions == 0) md.AppendLine("Nothing to do.");
        else
        {
            md.AppendLine("| Item | Figma | Game | What to do |");
            md.AppendLine("|---|---|---|---|");
            foreach (FigmaFieldRow u in unconfirmed)
                md.AppendLine(Row($"Mapping: {u.figmaComponent} → `{u.style}`", "—", "—",
                                  "Is this the right Unity style? If yes, confirm it in `component-map.json`."));
            foreach (FigmaFieldRow r in decide)
                md.AppendLine(Row($"`{r.style}` {FieldName(r.field)}", r.figmaNow, r.unityNow ?? "each control's own",
                                  Question(r.result) + (r.markProblem != null ? $" Marked, but {LowerFirst(r.markProblem)}" : "")));
            if (outside.Count > 0)
                md.AppendLine(Row($"{outside.Count} Figma colour(s) with no Unity token",
                                  string.Join(", ", outside.Take(3).Select(o => o.colour)) + (outside.Count > 3 ? " …" : ""), "—",
                                  "New game-wide tokens, or a look only these components use? See *Figma colours with no Unity token*."));
        }
        md.AppendLine();

        md.AppendLine("### Differs by platform rule");
        md.AppendLine();
        if (onPurpose.Count == 0) md.AppendLine("None yet.");
        else
        {
            md.AppendLine("| Item | Figma | Game | Why |");
            md.AppendLine("|---|---|---|---|");
            foreach (FigmaFieldRow r in onPurpose)
            {
                FigmaPlatformRule rule = rules?.FirstOrDefault(x => x != null && x.id == r.adaptation?.rule);
                md.AppendLine(Row($"`{r.style}` {FieldName(r.field)}", r.figmaNow, r.unityNow ?? "each control's own",
                                  $"{rule?.rule} (`{r.adaptation?.rule}`)" + (string.IsNullOrEmpty(r.adaptation?.reason) ? "" : $" {r.adaptation.reason}")
                                  + (r.result == FigmaResult.AdaptedFigmaMoved ? " Figma has changed since: check the rule still covers the new value." : "")));
            }
        }
        md.AppendLine();

        md.AppendLine("### Matches");
        md.AppendLine();
        if (matches.Count == 0) md.AppendLine("Nothing matches yet.");
        foreach (IGrouping<string, FigmaFieldRow> style in matches.GroupBy(r => r.style))
            md.AppendLine($"- `{Cell(style.Key)}`: {string.Join(", ", style.Select(r => FieldName(r.field)))}");
        md.AppendLine();
    }

    internal static string FieldName(string field) => field.Contains('.')
        ? $"{field.Substring(0, field.LastIndexOf('.'))} {FieldName(field.Substring(field.LastIndexOf('.') + 1))}"
        : field switch
    {
        "fill" => "fill colour",
        "border" => "border colour",
        "ink" => "text colour",
        "corner" => "corner radius",
        "labelColour" => "label colour",
        "labelSize" => "label size",
        "labelFont" => "label font",
        _ => field,
    };

    private static string Question(FigmaResult result) => result switch
    {
        FigmaResult.FollowFigma => "Change the game to Figma's value, or cite an agreed platform rule.",
        FigmaResult.UnityChanged => "The game changed this since the last sync. Go back to Figma's value, or cite an agreed platform rule.",
        FigmaResult.DesignUpdate => "Figma changed this. Apply it to the game through Style Proposals.",
        FigmaResult.Conflict => "Figma and the game both changed this. Apply Figma's new value, or cite an agreed platform rule.",
        _ => "",
    };

    private static string LowerFirst(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static void AppendBrief(StringBuilder md)
    {
        md.AppendLine("## For the designer's AI agent");
        md.AppendLine();
        md.AppendLine("You have this file and a Figma connector. This file records what the Unity game ships. Compare it with the Figma design library:");
        md.AppendLine();
        md.AppendLine("1. For each row under **Theme** and **Components**, find the Figma variable, text style or component named in its *Figma name* column (scheme below).");
        md.AppendLine("2. Report in the same three groups as **At a glance**: *matches*; *differs by platform rule* (with the rule from **Platform rules**); *to do* (every other difference, and anything missing in Figma or in Unity), showing both values.");
        md.AppendLine("3. Figma is the source of truth. Don't ask the designer to approve or review the game's values; a value differing by platform rule is explained, not questioned.");
        md.AppendLine("4. Don't edit, detach or rename library components or variables. If the designer asks for draft fixes, make them on a separate page named **Unity review**.");
        md.AppendLine("5. For exact values, read `tokens.json` beside this file (W3C design tokens).");
        md.AppendLine("6. End with what the developers will change in the game to follow Figma. Raise something with the designer only if a Figma value looks unworkable in the game and no rule covers it.");
        md.AppendLine();
    }

    private static void AppendNaming(StringBuilder md)
    {
        md.AppendLine("## Naming scheme");
        md.AppendLine();
        md.AppendLine("- **Colour variables** share the theme token's path: `Brand/Primary` in Unity is `Brand/Primary` in Figma.");
        md.AppendLine("- **Text styles** are named after the type-scale role: `Button`, `Body`, `BodySmall`.");
        md.AppendLine("- **Components**: a Unity style `Button_Primary_Small_Icon` is the Figma component set `Button` with the variant " +
                      "`Role=Primary, Size=Small, Icon=true`. Regular size is left out of the name. A last word such as `_Send` is a use case.");
        md.AppendLine();
    }

    private static void AppendTheme(StringBuilder md, UITheme theme)
    {
        md.AppendLine("## Theme");
        md.AppendLine();
        md.AppendLine($"Default button height **{Px(theme.ButtonHeight)}**; button text role **{(theme.SetButtonLabelFont ? theme.ButtonLabelRoleId : "each label's own")}**.");
        md.AppendLine();

        md.AppendLine("### Colours");
        md.AppendLine();
        md.AppendLine("| Token | Value | Figma name | Figma value | Status |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (UITheme.ColorToken t in theme.Colors.Where(t => t != null).OrderBy(t => t.name, StringComparer.Ordinal))
            md.AppendLine(Row($"`{t.name}`", Hex(t.value), $"`{t.name}`", "—", NotCompared));
        md.AppendLine();

        if (theme.Sizes.Count > 0)
        {
            md.AppendLine("### Named sizes");
            md.AppendLine();
            md.AppendLine("| Size | Height | Text role | Figma name | Status |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (UITheme.SizeToken z in theme.Sizes.Where(z => z != null))
                md.AppendLine(Row(z.name, Px(z.height), z.setLabelRole ? z.LabelRoleId : "theme's button role", $"Size={z.name}", NotCompared));
            md.AppendLine();
        }

        TypographyScale scale = theme.Typography;
        if (scale != null)
        {
            md.AppendLine("### Type scale");
            md.AppendLine();
            md.AppendLine("| Role | Font | Size | Style | Line spacing | Figma text style | Status |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (string role in scale.RoleIds)
            {
                TypographyScale.RoleStyle r = scale.Get(role);
                if (r == null) continue;
                string size = r.autoSize ? $"auto {r.autoSizeMin:0.#}–{r.autoSizeMax:0.#} px" : Px(r.fontSize);
                md.AppendLine(Row(role, r.font != null ? r.font.name : "—", size, r.fontStyle.ToString(),
                                  r.lineSpacing.ToString("0.#", CultureInfo.InvariantCulture), role, NotCompared));
            }
            md.AppendLine();
        }

        if (theme.Sprites.Count > 0)
        {
            md.AppendLine("### Shapes");
            md.AppendLine();
            md.AppendLine("| Token | Sprite | Status |");
            md.AppendLine("|---|---|---|");
            foreach (UITheme.SpriteToken t in theme.Sprites.Where(t => t != null))
                md.AppendLine(Row($"`{t.name}`", t.sprite != null ? t.sprite.name : "—", NotCompared));
            md.AppendLine();
        }

        if (theme.UseForGradedAnswers)
        {
            md.AppendLine("### Right and wrong answers");
            md.AppendLine();
            md.AppendLine("| Verdict | Fill | Border | Mark |");
            md.AppendLine("|---|---|---|---|");
            md.AppendLine(Row("Right", Colour(theme.RightFill, theme), Colour(theme.RightBorder, theme), theme.RightMark != null ? theme.RightMark.name : "—"));
            md.AppendLine(Row("Wrong", Colour(theme.WrongFill, theme), Colour(theme.WrongBorder, theme), theme.WrongMark != null ? theme.WrongMark.name : "—"));
            md.AppendLine();
        }
    }

    private static void AppendComponents(StringBuilder md, UITheme theme, List<UIComponentStyle> styles,
                                         IReadOnlyDictionary<UIComponentStyle, int> uses, FigmaReport report)
    {
        md.AppendLine("## Components");
        md.AppendLine();
        md.AppendLine("*Own settings* lists what a variant does differently from the style it follows.");
        md.AppendLine();
        foreach (IGrouping<string, UIComponentStyle> kind in styles.GroupBy(s => ComponentKinds.For(s)?.DisplayName ?? "Other").OrderBy(g => g.Key))
        {
            md.AppendLine($"### {kind.Key} styles");
            md.AppendLine();
            md.AppendLine("| Unity style | Figma component | Follows | Own settings | Fill | Label | Corner | Height | Text | Used by | Status |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (UIComponentStyle s in kind.OrderBy(s => s.name, StringComparer.Ordinal))
            {
                string usedBy = uses != null && uses.TryGetValue(s, out int n) ? n.ToString(CultureInfo.InvariantCulture) : "—";
                List<FigmaFieldRow> rows = report?.RowsFor(s.name).ToList() ?? new List<FigmaFieldRow>();
                string figmaName = rows.Count > 0
                    ? rows[0].figmaComponent + (rows[0].mappingConfirmed ? "" : " *(mapping not confirmed)*")
                    : ExpectedFigmaName(s.name, theme);
                string status = s.Locked ? "locked (approved)"
                    : rows.Count > 0 ? FigmaSync.Summary(rows)
                    : report != null ? "not mapped to Figma" : NotCompared;
                md.AppendLine(Row(
                    $"`{s.name}`",
                    figmaName,
                    s.IsVariant ? $"`{s.BaseStyle.name}`" : "—",
                    s.IsVariant && s.Overrides.Count > 0 ? string.Join(", ", s.Overrides.Select(o => o.Split('.')[0]).Distinct()) : "—",
                    Colour(s.Background.fill, theme),
                    Colour(s.LabelColor, theme),
                    s.Background.cornerRadius > 0f ? Px(s.Background.cornerRadius) : "own",
                    DesignSystemDescribe.Height(s),
                    DesignSystemDescribe.LabelFont(s),
                    usedBy,
                    status));
            }
            md.AppendLine();
        }
    }

    private static void AppendStateStyles(StringBuilder md, UITheme theme, List<UIStateStyle> styles)
    {
        if (styles == null || styles.Count == 0) return;
        md.AppendLine("### State styles");
        md.AppendLine();
        md.AppendLine("Components whose look follows game state (Selected, Correct, Steer…). Each row is a Figma variant's state; *own* keeps the component's colour.");
        md.AppendLine();
        foreach (UIStateStyle s in styles.OrderBy(s => s.name, StringComparer.Ordinal))
        {
            md.AppendLine($"#### `{s.name}`");
            md.AppendLine();
            md.AppendLine("| State | Fill | Border | Ink | Accent | Opacity |");
            md.AppendLine("|---|---|---|---|---|---|");
            UITheme source = s.Theme != null ? s.Theme : theme;
            foreach (UIStateStyle.State state in s.States.Where(st => st != null))
                md.AppendLine(Row(state.name, Colour(state.fill, source), Colour(state.border, source), Colour(state.ink, source),
                                  Colour(state.accent, source), $"{Mathf.RoundToInt(state.opacity * 100f)}%"));
            md.AppendLine();
        }
    }

    private static void AppendComparison(StringBuilder md, FigmaReport report, UITheme theme)
    {
        md.AppendLine("## Figma comparison");
        md.AppendLine();
        md.AppendLine("Each mapped style, field by field. *Baseline* is Figma's value at the last accepted sync.");
        md.AppendLine();
        if (report.rows.Count > 0)
        {
            md.AppendLine("| Unity style | Figma component | Field | Figma now | Baseline | Unity | Result |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            foreach (FigmaFieldRow r in report.rows)
                md.AppendLine(Row($"`{r.style}`", r.figmaComponent + (r.mappingConfirmed ? "" : " *(not confirmed)*"), r.field,
                                  r.figmaNow, r.figmaBaseline ?? "—", r.unityNow ?? "each control's own", FigmaSync.Label(r.result)));
            md.AppendLine();
        }
        else md.AppendLine("No Figma component is mapped to a Unity style yet.\n");

        if (report.unmapped.Count > 0)
        {
            md.AppendLine("### Figma components not mapped yet");
            md.AppendLine();
            md.AppendLine("A suggestion is added to `component-map.json` for each one the sync could match by value; confirm or change it there.");
            md.AppendLine();
            md.AppendLine("| Figma component | Suggested Unity style | Note |");
            md.AppendLine("|---|---|---|");
            foreach (FigmaMapEntry u in report.unmapped)
                md.AppendLine(Row(u.figmaName, string.IsNullOrEmpty(u.unityStyle) ? "—" : $"`{u.unityStyle}`", u.note));
            md.AppendLine();
        }
        if (report.stateGaps.Count > 0)
        {
            md.AppendLine("### States only one side has");
            md.AppendLine();
            md.AppendLine("A state the Figma component has and its Unity state style doesn't, or the other way round. Nothing is compared for it.");
            md.AppendLine();
            md.AppendLine("| Unity style | Figma component | State | Only in |");
            md.AppendLine("|---|---|---|---|");
            foreach (FigmaStateGap g in report.stateGaps)
                md.AppendLine(Row($"`{g.style}`", g.component, g.state, g.onlyIn));
            md.AppendLine();
        }
        if (report.unmappedStateStyles.Count > 0)
        {
            md.AppendLine("### State styles no Figma component follows");
            md.AppendLine();
            foreach (string s in report.unmappedStateStyles) md.AppendLine($"- `{Cell(s)}`: no Figma component follows it (Unity only).");
            md.AppendLine();
        }
        if (report.notUsed.Count > 0)
        {
            md.AppendLine("### Figma components the game doesn't use");
            md.AppendLine();
            foreach (FigmaMapEntry m in report.notUsed) md.AppendLine($"- {Cell(m.figmaName)}: {Cell(m.note)}");
            md.AppendLine();
        }
        var outside = FigmaSync.ColoursOutsideTheme(report.snapshot, theme);
        if (outside.Count > 0)
        {
            md.AppendLine("### Figma colours with no Unity token");
            md.AppendLine();
            md.AppendLine("Colours these components use that the game's theme has no name for. Each is either a new token to add, or a look only that game uses.");
            md.AppendLine();
            md.AppendLine("| Colour | Used by |");
            md.AppendLine("|---|---|");
            foreach (var (colour, components) in outside)
                md.AppendLine(Row(colour, components.Count <= 4 ? string.Join(", ", components) : $"{string.Join(", ", components.Take(4))} and {components.Count - 4} more"));
            md.AppendLine();
        }
        if (report.missingStyles.Count > 0)
        {
            md.AppendLine("### Map entries naming a style that doesn't exist");
            md.AppendLine();
            foreach (FigmaMapEntry m in report.missingStyles) md.AppendLine($"- {Cell(m.figmaName)} → `{Cell(m.unityStyle)}`");
            md.AppendLine();
        }
    }

    private static void AppendRules(StringBuilder md, IList<FigmaPlatformRule> rules)
    {
        md.AppendLine("## Platform rules");
        md.AppendLine();
        md.AppendLine("The only reasons the game may differ from Figma, agreed once with the designer in `figma/rules.json`. " +
                      "A rule nobody has agreed is a proposal for the next rules session and covers nothing.");
        md.AppendLine();
        List<FigmaPlatformRule> listed = rules?.Where(r => r != null).ToList() ?? new List<FigmaPlatformRule>();
        if (!listed.Any(r => r.Agreed))
        {
            md.AppendLine("No platform rules are agreed yet, so the game follows Figma everywhere.");
            md.AppendLine();
        }
        if (listed.Count == 0) return;
        md.AppendLine("| Rule | What it says | Covers | Agreed |");
        md.AppendLine("|---|---|---|---|");
        foreach (FigmaPlatformRule r in listed)
            md.AppendLine(Row($"`{r.id}`", r.rule, r.fields == null || r.fields.Count == 0 ? "any field" : string.Join(", ", r.fields),
                              r.Agreed ? $"{r.agreedBy}, {r.date}" : "*proposed: covers nothing until the designer agrees it*"));
        md.AppendLine();
    }

    private static void AppendAdaptations(StringBuilder md, IList<FigmaAdaptation> adaptations, IList<FigmaPlatformRule> rules)
    {
        md.AppendLine("## Adapted for the game");
        md.AppendLine();
        if (adaptations == null || adaptations.Count == 0)
        {
            md.AppendLine("Values the game keeps under an agreed platform rule, with who marked them. None are recorded yet.");
            md.AppendLine();
            return;
        }
        md.AppendLine("| Unity style | Field | Unity | Figma when marked | Platform rule | Note | Marked |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (FigmaAdaptation a in adaptations)
        {
            string problem = FigmaSync.CheckMark(a, rules);
            md.AppendLine(Row($"`{a.style}`", a.field, a.unityValue, a.figmaValue, problem == null ? $"`{a.rule}`" : $"**Doesn't count:** {LowerFirst(problem)}",
                              a.reason ?? "", $"{a.decidedBy}, {a.date}"));
        }
        md.AppendLine();
    }

    private static readonly FigmaResult[] Changes = { FigmaResult.DesignUpdate, FigmaResult.Conflict, FigmaResult.AdaptedFigmaMoved, FigmaResult.UnityChanged };

    private static void AppendSinceBaseline(StringBuilder md, FigmaReport report)
    {
        md.AppendLine("## Since the last baseline");
        md.AppendLine();
        if (report == null || !report.hasBaseline)
        {
            md.AppendLine("No baseline yet. After each accepted Figma sync this lists what changed in Figma and in Unity since the sync before.");
            md.AppendLine();
            return;
        }
        List<FigmaFieldRow> changed = report.rows.Where(r => Changes.Contains(r.result)).ToList();
        if (changed.Count == 0 && report.removed.Count == 0) md.AppendLine("Nothing changed on either side.");
        foreach (FigmaFieldRow r in changed)
            md.AppendLine($"- `{Cell(r.style)}` {r.field}: {FigmaSync.Label(r.result)} (Figma {Cell(r.figmaBaseline)} → {Cell(r.figmaNow)}, Unity {Cell(r.unityNow ?? "own")})");
        foreach (FigmaComponent c in report.removed)
            md.AppendLine($"- {Cell(c.name)} was removed from Figma. Nothing is removed in Unity.");
        md.AppendLine();
    }

    /// <summary>
    /// The Figma component a style's name says it is, by the naming scheme: Button_Primary_Small_Icon_Send is the
    /// Button set's variant Role=Primary, Size=Small, Icon=true (use case Send). A name outside the scheme says so.
    /// </summary>
    public static string ExpectedFigmaName(string styleName, UITheme theme)
    {
        string[] words = styleName.Split('_').Where(w => w.Length > 0).ToArray();
        string[] kinds = { "Button", "Toggle", "Panel", "DragCard", "Surface" };
        if (words.Length < 2 || !kinds.Contains(words[0])) return "*(name outside the scheme)*";

        var props = new List<string> { $"Role={words[1]}" };
        var rest = new List<string>();
        foreach (string w in words.Skip(2))
        {
            if (theme != null && theme.Sizes.Any(z => z != null && z.name == w)) props.Add($"Size={w}");
            else if (w == "Icon") props.Add("Icon=true");
            else if (w.Length > 1 && w[0] == 'H' && w.Skip(1).All(char.IsDigit)) props.Add($"Height={w.Substring(1)}");
            else rest.Add(w);
        }
        return $"{words[0]} / {string.Join(", ", props)}" + (rest.Count > 0 ? $" (use case {string.Join(" ", rest)})" : "");
    }

    private static string Colour(ThemeColor colour, UITheme theme)
    {
        if (!colour.IsSet) return "own";
        UITheme.ColorToken token = theme != null ? theme.GetToken(colour.tokenId) : null;
        return token != null ? $"`{token.name}` {Hex(token.value)}" : "⚠ missing token";
    }

    private static string Hex(Color c)
    {
        string rgba = ColorUtility.ToHtmlStringRGBA(c);
        return "#" + (rgba.EndsWith("FF") ? rgba.Substring(0, 6) : rgba);
    }

    private static string Px(float px) => px > 0f ? $"{px.ToString("0.#", CultureInfo.InvariantCulture)} px" : "not set";

    private static string Row(params string[] cells) => "| " + string.Join(" | ", cells.Select(Cell)) + " |";

    // A pipe would end the cell early, and a line break would end the table.
    private static string Cell(string text) => (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
