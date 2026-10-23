using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Which content text appears in which control, and how long it may run there.</summary>
[Serializable]
public class TextFitRule
{
    [Tooltip("Content files under Assets/StreamingAssets: a name, or a pattern such as QualityLO5*.json.")]
    public string contentFiles;
    [Tooltip("The field the control shows, as the end of its path with [] for lists, e.g. points[].options[].label.")]
    public string field;
    [Tooltip("The control prefab the text appears in. Its label sets the width, font and size the text is measured at.")]
    public GameObject control;
    [Tooltip("The most lines the text may take before it reads as a paragraph rather than an option.")]
    [Min(1)] public int maxLines = 3;
}

/// <summary>
/// Measures the game's content text against the controls that show it, so a label that overflows its card is caught
/// when the content is written rather than in a playtest. Each <see cref="TextFitRule"/> names content fields and the
/// control prefab they appear in; every string is measured at that control's label width, font and size. Text that
/// can't fit a fixed-height control is a problem; text longer than the rule's line limit is a warning for the
/// content's author.
/// </summary>
public static class TextFitCheck
{
    public const string Overflows = "Text overflows its control";
    public const string TooLong = "Text longer than its control suits";

    public static void Run(DesignSystemSettings settings, List<DesignIssue> issues)
    {
        foreach (TextFitRule rule in settings.textFitRules.Where(r => r != null && r.control != null && !string.IsNullOrEmpty(r.field)))
            Check(rule, ContentTexts(rule), issues);
    }

    /// <summary>Every string the rule's field holds in its content files, with where it came from.</summary>
    public static IEnumerable<(string where, string text)> ContentTexts(TextFitRule rule)
    {
        const string root = "Assets/StreamingAssets";
        if (!Directory.Exists(root) || string.IsNullOrEmpty(rule.contentFiles)) yield break;
        foreach (string file in Directory.GetFiles(root, rule.contentFiles, SearchOption.AllDirectories).OrderBy(f => f))
        {
            string path = file.Replace('\\', '/');
            foreach (var (fieldPath, text) in JsonStrings.Read(File.ReadAllText(path)))
                if (Matches(fieldPath, rule.field)) yield return ($"{path} › {fieldPath}", text);
        }
    }

    public static bool Matches(string fieldPath, string field) =>
        fieldPath == field || fieldPath.EndsWith("." + field);

    /// <summary>Measures each text in the rule's control and reports what doesn't fit.</summary>
    public static void Check(TextFitRule rule, IEnumerable<(string where, string text)> texts, List<DesignIssue> issues)
    {
        if (!TryGetLabel(rule.control, out TMP_Text label, out float width)) return;
        float room = TextRoom(rule.control, label);
        string prefabPath = AssetDatabase.GetAssetPath(rule.control);

        using var meter = new TextMeter(label);
        foreach (var (where, text) in texts.Where(t => !string.IsNullOrWhiteSpace(t.text)))
        {
            Measure m = meter.Measure(text, width);
            if (room > 0f && m.Height > room + 0.5f)
                issues.Add(Issue(DesignIssue.Level.Problem, Overflows, where, prefabPath,
                    $"Runs to {m.Lines} lines ({m.Height:0}px) in {rule.control.name}, whose style fixes its height and leaves room for {room:0}px. " +
                    $"Turn on Grow with text in the style, or shorten it. “{Excerpt(text)}”"));
            else if (m.Lines > rule.maxLines)
                issues.Add(Issue(DesignIssue.Level.Warning, TooLong, where, prefabPath,
                    $"Runs to {m.Lines} lines in {rule.control.name}; keep it to {rule.maxLines}. “{Excerpt(text)}”"));
        }
    }

    private static DesignIssue Issue(DesignIssue.Level level, string category, string where, string prefabPath, string message) =>
        new DesignIssue
        {
            Severity = level, Category = category, Where = where, Message = message,
            AssetPath = where.Split(new[] { " › " }, StringSplitOptions.None)[0],
            Target = AssetDatabase.LoadMainAssetAtPath(where.Split(new[] { " › " }, StringSplitOptions.None)[0]) ?? AssetDatabase.LoadMainAssetAtPath(prefabPath),
        };

    private static string Excerpt(string text) => text.Length <= 60 ? text : text.Substring(0, 57) + "…";

    public struct Measure
    {
        public int Lines;
        public float Height;
    }

    private static bool TryGetLabel(GameObject control, out TMP_Text label, out float width)
    {
        label = control.TryGetComponent(out StyledElement styled) && styled.Label != null ? styled.Label : control.GetComponentInChildren<TMP_Text>(true);
        width = label != null ? label.rectTransform.rect.width - label.margin.x - label.margin.z : 0f;
        return label != null && width > 1f;
    }

    /// <summary>
    /// The height the label can use in a control whose style fixes its height, or 0 when the control grows with its
    /// text (or nothing fixes it), in which case only the line limit applies.
    /// </summary>
    public static float TextRoom(GameObject control, TMP_Text label)
    {
        if (!control.TryGetComponent(out StyledElement styled) || styled.ComponentStyle == null || styled.KeepSize) return 0f;
        UIComponentStyle style = styled.ComponentStyle;
        if (style.Height <= 0f || style.GrowWithContent) return 0f;
        float padding = control.TryGetComponent(out HorizontalOrVerticalLayoutGroup group) ? group.padding.vertical : 0f;
        return Mathf.Max(0f, style.Height - padding - label.margin.y - label.margin.w);
    }

    /// <summary>A hidden copy of a label's text settings, for measuring strings without touching the label.</summary>
    public sealed class TextMeter : IDisposable
    {
        private readonly GameObject go;
        private readonly TextMeshProUGUI text;
        private readonly float lineHeight;

        public TextMeter(TMP_Text source)
        {
            go = new GameObject("__TextMeter", typeof(RectTransform), typeof(TextMeshProUGUI)) { hideFlags = HideFlags.HideAndDontSave };
            text = go.GetComponent<TextMeshProUGUI>();
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontSize = source.enableAutoSizing ? source.fontSizeMin : source.fontSize;
            text.fontStyle = source.fontStyle;
            text.characterSpacing = source.characterSpacing;
            text.wordSpacing = source.wordSpacing;
            text.lineSpacing = source.lineSpacing;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = source.richText;
            lineHeight = text.GetPreferredValues("Ag", 10000f, 0f).y;
        }

        public Measure Measure(string s, float width)
        {
            float h = text.GetPreferredValues(s, width, 0f).y;
            return new Measure { Height = h, Lines = lineHeight > 0f ? Mathf.Max(1, Mathf.RoundToInt(h / lineHeight)) : 1 };
        }

        public void Dispose() => Object.DestroyImmediate(go);
    }
}

/// <summary>
/// Reads every string in a JSON document with its path, lists written as [] so one path names a field in every
/// entry: walks[].points[].options[].label. Enough JSON for content files; numbers, booleans and nulls are skipped.
/// </summary>
public static class JsonStrings
{
    public static List<(string path, string value)> Read(string json)
    {
        var found = new List<(string, string)>();
        int i = 0;
        Value(json, ref i, "", found);
        return found;
    }

    private static void Value(string s, ref int i, string path, List<(string, string)> found)
    {
        Skip(s, ref i);
        if (i >= s.Length) return;
        char c = s[i];
        if (c == '{')
        {
            i++;
            for (Skip(s, ref i); i < s.Length && s[i] != '}'; Skip(s, ref i))
            {
                string key = String(s, ref i);
                Skip(s, ref i);
                i++; // ':'
                Value(s, ref i, path.Length == 0 ? key : $"{path}.{key}", found);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
            }
            i++;
        }
        else if (c == '[')
        {
            i++;
            for (Skip(s, ref i); i < s.Length && s[i] != ']'; Skip(s, ref i))
            {
                Value(s, ref i, path + "[]", found);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
            }
            i++;
        }
        else if (c == '"') found.Add((path, String(s, ref i)));
        else while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
    }

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static string String(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (i < s.Length && s[i] != '"')
        {
            char c = s[i++];
            if (c != '\\' || i >= s.Length) { sb.Append(c); continue; }
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u' when i + 4 <= s.Length:
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        i++; // closing quote
        return sb.ToString();
    }
}
