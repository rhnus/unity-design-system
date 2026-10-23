using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Reads a button's spec from a Figma node — size, corner radius, fill, label colour and text size — so a style can
/// match the design file instead of being eyeballed. Parsing is separate from fetching so it can be tested.
/// </summary>
public static class FigmaSpec
{
    public struct Link
    {
        public string FileKey;
        public string NodeId;
    }

    public class ButtonSpec
    {
        public string Name;
        public float Width, Height, CornerRadius;
        public Color? Fill, LabelColour;
        public float FontSize;
        public string FontFamily;
    }

    private static readonly Regex LinkPattern = new Regex(@"figma\.com/(?:file|design|proto)/([A-Za-z0-9]+)[^?]*\?(?:.*&)?node-id=([0-9]+)(?:-|%3A|:)([0-9]+)");

    /// <summary>Accepts the link Figma copies for a selected layer ("Copy link to selection").</summary>
    public static bool TryParseLink(string url, out Link link)
    {
        link = default;
        Match m = url == null ? Match.Empty : LinkPattern.Match(url.Trim());
        if (!m.Success) return false;
        link = new Link { FileKey = m.Groups[1].Value, NodeId = $"{m.Groups[2].Value}:{m.Groups[3].Value}" };
        return true;
    }

    /// <summary>The button spec in a node document: its own box and fill, and the first text inside it.</summary>
    public static ButtonSpec ParseNode(JObject node)
    {
        var spec = new ButtonSpec { Name = (string)node["name"] ?? "Button" };
        if (node["absoluteBoundingBox"] is JObject box)
        {
            spec.Width = box.Value<float?>("width") ?? 0f;
            spec.Height = box.Value<float?>("height") ?? 0f;
        }
        spec.CornerRadius = node.Value<float?>("cornerRadius") ?? (node["rectangleCornerRadii"] as JArray)?.First?.Value<float>() ?? 0f;
        spec.Fill = SolidFill(node) ?? Descendants(node).Where(n => (string)n["type"] != "TEXT").Select(SolidFill).FirstOrDefault(c => c.HasValue);

        JObject text = Descendants(node).FirstOrDefault(n => (string)n["type"] == "TEXT");
        if (text != null)
        {
            spec.FontSize = text["style"]?.Value<float?>("fontSize") ?? 0f;
            spec.FontFamily = (string)text["style"]?["fontFamily"];
            spec.LabelColour = SolidFill(text);
        }
        return spec;
    }

    private static Color? SolidFill(JObject node)
    {
        if (!(node["fills"] is JArray fills)) return null;
        foreach (JObject fill in fills.OfType<JObject>())
        {
            if ((string)fill["type"] != "SOLID" || fill.Value<bool?>("visible") == false || !(fill["color"] is JObject c)) continue;
            float opacity = fill.Value<float?>("opacity") ?? 1f;
            return new Color(c.Value<float>("r"), c.Value<float>("g"), c.Value<float>("b"), (c.Value<float?>("a") ?? 1f) * opacity);
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<JObject> Descendants(JObject node)
    {
        if (!(node["children"] is JArray children)) yield break;
        foreach (JObject child in children.OfType<JObject>())
        {
            yield return child;
            foreach (JObject d in Descendants(child)) yield return d;
        }
    }
}

/// <summary>Fetches a Figma node and applies the parts of its spec the designer ticks to one button style.</summary>
public class FigmaButtonImportWindow : EditorWindow
{
    private const string TokenKey = "DesignSystem.FigmaToken";

    private UIComponentStyle style;
    private string url = "";
    private string status = "";
    private UnityWebRequest request;
    private FigmaSpec.ButtonSpec spec;
    private bool useHeight = true, useWidth, useCorner = true, useFill = true, useLabel = true, useFontSize;

    public static void Open(UIComponentStyle style)
    {
        var window = GetWindow<FigmaButtonImportWindow>(true, "Import from Figma");
        window.style = style;
        window.minSize = new Vector2(380f, 300f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(style != null ? $"Into {style.name}" : "No style", EditorStyles.boldLabel);
        string token = EditorPrefs.GetString(TokenKey, "");
        string newToken = EditorGUILayout.PasswordField(new GUIContent("Figma token",
            "A personal access token from Figma ▸ Settings ▸ Security. Stored only in this computer's editor preferences."), token);
        if (newToken != token) EditorPrefs.SetString(TokenKey, newToken);
        url = EditorGUILayout.TextField(new GUIContent("Layer link", "Right-click the button in Figma ▸ Copy link to selection."), url);

        bool valid = FigmaSpec.TryParseLink(url, out FigmaSpec.Link link);
        using (new EditorGUI.DisabledScope(!valid || string.IsNullOrEmpty(newToken) || request != null))
            if (GUILayout.Button(request != null ? "Fetching…" : "Fetch")) Fetch(link, newToken);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        if (spec == null || style == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(spec.Name, EditorStyles.boldLabel);
        useHeight = EditorGUILayout.ToggleLeft($"Height  {spec.Height:0.#} px", useHeight);
        useWidth = EditorGUILayout.ToggleLeft($"Width  {spec.Width:0.#} px  (off for buttons that size to their label)", useWidth);
        useCorner = EditorGUILayout.ToggleLeft($"Corner  {spec.CornerRadius:0.#} px", useCorner);
        if (spec.Fill.HasValue) useFill = ColourToggle("Fill", spec.Fill.Value, useFill);
        if (spec.LabelColour.HasValue) useLabel = ColourToggle("Label colour", spec.LabelColour.Value, useLabel);
        if (spec.FontSize > 0f)
        {
            if (style.TryGetLabelRoleId(out string role))
            {
                int shared = DesignSystemAssets.FindAll<UIComponentStyle>().Count(s => s.TryGetLabelRoleId(out string r) && r == role);
                useFontSize = EditorGUILayout.ToggleLeft($"Set '{role}' text to {spec.FontSize:0.#} px  (shared by {shared} styles)", useFontSize);
            }
            else EditorGUILayout.LabelField($"Text {spec.FontSize:0.#} px {spec.FontFamily} — this style keeps each label's own font.", EditorStyles.wordWrappedMiniLabel);
        }

        using (new EditorGUI.DisabledScope(style.Locked))
            if (GUILayout.Button(style.Locked ? "Style is locked" : $"Apply to {style.name}", GUILayout.Height(26))) Apply();
    }

    private static bool ColourToggle(string label, Color colour, bool value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            value = EditorGUILayout.ToggleLeft($"{label}  #{ColorUtility.ToHtmlStringRGBA(colour)}", value);
            Rect r = GUILayoutUtility.GetRect(30f, 16f, GUILayout.Width(30f));
            EditorGUI.DrawRect(r, colour);
        }
        return value;
    }

    private void Fetch(FigmaSpec.Link link, string token)
    {
        request = UnityWebRequest.Get($"https://api.figma.com/v1/files/{link.FileKey}/nodes?ids={Uri.EscapeDataString(link.NodeId)}");
        request.SetRequestHeader("X-Figma-Token", token);
        request.SendWebRequest();
        status = "Fetching from Figma…";
        string node = link.NodeId;
        EditorApplication.update += Poll;

        void Poll()
        {
            if (request == null || !request.isDone) return;
            EditorApplication.update -= Poll;
            if (request.result != UnityWebRequest.Result.Success)
                status = $"Figma said: {request.responseCode} {request.error}. Check the token and that you can open the file.";
            else
            {
                JObject doc = JObject.Parse(request.downloadHandler.text)["nodes"]?[node]?["document"] as JObject;
                spec = doc != null ? FigmaSpec.ParseNode(doc) : null;
                status = spec != null ? "" : "That link doesn't point at a layer this token can read.";
            }
            request.Dispose();
            request = null;
            Repaint();
        }
    }

    private void Apply()
    {
        UITheme theme = style.Theme;
        Undo.RecordObject(style, "Import from Figma");
        if (theme != null) Undo.RecordObject(theme, "Import from Figma");

        if (useHeight && spec.Height > 0f)
        {
            if (theme != null && Mathf.Approximately(theme.ButtonHeight, spec.Height)) style.HeightSource = ButtonHeightSource.Theme;
            else style.Height = spec.Height;
        }
        if (useWidth && spec.Width > 0f) SetWidth(spec.Width);
        if (useCorner) style.Background.cornerRadius = spec.CornerRadius;
        if (useFill && spec.Fill.HasValue && theme != null) style.Background.fill = ThemeColor.From(Token(theme, $"Figma/{spec.Name}/Fill", spec.Fill.Value));
        if (useLabel && spec.LabelColour.HasValue && theme != null) style.LabelColor = ThemeColor.From(Token(theme, $"Figma/{spec.Name}/Label", spec.LabelColour.Value));
        if (useFontSize && spec.FontSize > 0f && style.TryGetLabelRoleId(out string role) && theme != null && theme.Typography != null)
        {
            TypographyScale.RoleStyle rs = theme.Typography.Get(role);
            if (rs != null)
            {
                Undo.RecordObject(theme.Typography, "Import from Figma");
                rs.fontSize = spec.FontSize;
                EditorUtility.SetDirty(theme.Typography);
                TypographyAssets.ReapplyEverywhere();
            }
        }

        EditorUtility.SetDirty(style);
        if (theme != null) EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        DesignChangeLog.Note($"{style.name} from Figma");
        DesignSystemAssets.ReapplyOpen();
        status = $"Applied to {style.name}. Run the Check before committing.";
    }

    private void SetWidth(float width)
    {
        var so = new SerializedObject(style);
        so.FindProperty("width").floatValue = width;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Reuse a theme colour that already matches, so importing doesn't scatter near-identical tokens.
    private static UITheme.ColorToken Token(UITheme theme, string name, Color colour) =>
        theme.Colors.FirstOrDefault(t => ButtonScanner.ColorDistance(t.value, colour) < 0.01f) ?? theme.SetColor(name, colour);
}
