using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reads and writes <see cref="UITheme"/> colour tokens as W3C design-token JSON (DTCG) — the format of Figma's
/// variable export, Tokens Studio and Style Dictionary. Accepts both <c>$value</c>/<c>$type</c> and the older
/// <c>value</c>/<c>type</c> keys, group-level types, <c>{group.token}</c> aliases, and hex, rgb() or
/// <c>{ components, alpha }</c> colour values. Non-colour tokens are ignored.
/// </summary>
public static class DesignTokenJson
{
    private const string IdExtension = "com.adgbl.id";

    public struct ImportReport
    {
        public int Added, Updated, Unchanged;
        public List<string> NotInFile;

        public override string ToString() =>
            $"{Added} added, {Updated} updated, {Unchanged} unchanged" +
            (NotInFile.Count > 0 ? $"; {NotInFile.Count} theme token(s) absent from the file were kept: {string.Join(", ", NotInFile)}" : "");
    }

    public static void ImportWithDialog(UITheme theme)
    {
        string path = EditorUtility.OpenFilePanel("Import design tokens", "", "json");
        if (string.IsNullOrEmpty(path)) return;
        Undo.RecordObject(theme, "Import design tokens");
        ImportReport report = Import(theme, File.ReadAllText(path));
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssetIfDirty(theme);
        Debug.Log($"[DesignSystem] Imported {Path.GetFileName(path)} into {theme.name}: {report}");
        DesignSystemAssets.ReapplyOpen();
    }

    public static void ExportWithDialog(UITheme theme)
    {
        string path = EditorUtility.SaveFilePanel("Export design tokens", "", theme.name + ".tokens", "json");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllText(path, Export(theme));
        Debug.Log($"[DesignSystem] Exported {theme.Colors.Count} colour token(s) to {path}");
    }

    /// <summary>
    /// Upserts every colour token in the JSON into the theme. A token keeps its id when it matches by the id
    /// this exporter writes, or else by name, so references survive. Tokens missing from the file are never
    /// deleted — that would orphan every style pointing at them.
    /// </summary>
    public static ImportReport Import(UITheme theme, string json)
    {
        var root = JObject.Parse(json);
        var leaves = new Dictionary<string, (JToken value, string type, string id)>();
        Flatten(root, new List<string>(), null, leaves);

        var report = new ImportReport { NotInFile = new List<string>() };
        var seen = new HashSet<string>();
        foreach (var pair in leaves)
        {
            JToken resolved = Resolve(pair.Value.value, leaves, 0);
            if ((pair.Value.type ?? InferType(resolved)) != "color") continue;
            if (!TryParseColor(resolved, out Color color))
            {
                Debug.LogWarning($"[DesignSystem] Skipped token '{pair.Key}': unreadable colour '{pair.Value.value}'.");
                continue;
            }

            string name = pair.Key.Replace('.', '/');
            UITheme.ColorToken existing = (pair.Value.id != null ? theme.GetToken(pair.Value.id) : null) ?? theme.FindByName(name);
            if (existing == null) report.Added++;
            else if (existing.value == color && existing.name == name) report.Unchanged++;
            else report.Updated++;

            if (existing != null) existing.name = name;
            seen.Add(theme.SetColor(name, color).id);
        }

        foreach (UITheme.ColorToken t in theme.Colors)
            if (!seen.Contains(t.id)) report.NotInFile.Add(t.name);
        return report;
    }

    public static string Export(UITheme theme)
    {
        var root = new JObject();
        foreach (UITheme.ColorToken t in theme.Colors)
        {
            string[] path = (string.IsNullOrEmpty(t.name) ? t.id : t.name).Split('/');
            JObject group = root;
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (!(group[path[i]] is JObject child)) group[path[i]] = child = new JObject();
                group = child;
            }
            group[path[^1]] = new JObject
            {
                ["$type"] = "color",
                ["$value"] = "#" + (Mathf.Approximately(t.value.a, 1f) ? ColorUtility.ToHtmlStringRGB(t.value) : ColorUtility.ToHtmlStringRGBA(t.value)),
                ["$extensions"] = new JObject { [IdExtension] = t.id },
            };
        }
        AddSizesAndType(root, theme);
        return root.ToString();
    }

    // Named sizes and the type scale go out too, so a designer can seed Figma variables and text styles from the
    // game; Import reads colours only, so these never come back in through it.
    private static void AddSizesAndType(JObject root, UITheme theme)
    {
        if (theme.Sizes.Count > 0 && root["Size"] == null)
        {
            var sizes = new JObject();
            foreach (UITheme.SizeToken z in theme.Sizes.Where(z => z != null && !string.IsNullOrEmpty(z.name)))
                sizes[z.name] = new JObject
                {
                    ["$type"] = "dimension",
                    ["$value"] = $"{z.height.ToString("0.#", CultureInfo.InvariantCulture)}px",
                    ["$extensions"] = new JObject { [IdExtension] = z.id },
                };
            root["Size"] = sizes;
        }

        TypographyScale scale = theme.Typography;
        if (scale == null || root["Typography"] != null) return;
        var type = new JObject();
        foreach (string role in scale.RoleIds)
        {
            TypographyScale.RoleStyle r = scale.Get(role);
            if (r?.font == null) continue;
            (string family, int weight) = FigmaSync.ParseFontAsset(r.font.name);
            if ((r.fontStyle & TMPro.FontStyles.Bold) != 0 && weight < 700) weight = 700;
            type[role] = new JObject
            {
                ["$type"] = "typography",
                ["$value"] = new JObject
                {
                    ["fontFamily"] = family,
                    ["fontWeight"] = weight,
                    ["fontSize"] = $"{r.fontSize.ToString("0.#", CultureInfo.InvariantCulture)}px",
                },
            };
        }
        root["Typography"] = type;
    }

    private static void Flatten(JObject node, List<string> path, string inheritedType,
                                Dictionary<string, (JToken, string, string)> leaves)
    {
        string type = StringOf(node["$type"]) ?? StringOf(node["type"]) ?? inheritedType;
        JToken value = node["$value"] ?? node["value"];
        if (value != null && path.Count > 0)
        {
            string id = (string)node["$extensions"]?[IdExtension];
            leaves[string.Join(".", path)] = (value, type, id);
            return;
        }

        foreach (JProperty prop in node.Properties())
        {
            if (prop.Name.StartsWith("$") || !(prop.Value is JObject child)) continue;
            path.Add(prop.Name);
            Flatten(child, path, type, leaves);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static string StringOf(JToken token) => token?.Type == JTokenType.String ? (string)token : null;

    private static JToken Resolve(JToken value, Dictionary<string, (JToken value, string type, string id)> leaves, int depth)
    {
        if (depth > 16 || value.Type != JTokenType.String) return value;
        string s = ((string)value).Trim();
        if (s.Length > 2 && s[0] == '{' && s[^1] == '}' && leaves.TryGetValue(s.Substring(1, s.Length - 2), out var target))
            return Resolve(target.value, leaves, depth + 1);
        return value;
    }

    private static string InferType(JToken value)
    {
        if (value is JObject obj) return obj["components"] != null || obj["hex"] != null ? "color" : null;
        string s = value.Type == JTokenType.String ? ((string)value).Trim() : "";
        return s.StartsWith("#") || s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) ? "color" : null;
    }

    private static readonly Regex Rgb = new Regex(@"rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)",
                                                  RegexOptions.IgnoreCase);

    public static bool TryParseColor(JToken value, out Color color)
    {
        color = Color.white;
        if (value == null) return false;

        if (value is JObject obj)
        {
            float alpha = obj["alpha"]?.Value<float>() ?? 1f;
            if (obj["components"] is JArray c && c.Count >= 3)
            {
                color = new Color(c[0].Value<float>(), c[1].Value<float>(), c[2].Value<float>(), alpha);
                return true;
            }
            if (obj["hex"] != null && TryParseColor(obj["hex"], out color))
            {
                color.a = alpha;
                return true;
            }
            return false;
        }

        string s = ((string)value)?.Trim();
        if (string.IsNullOrEmpty(s)) return false;
        if (s.StartsWith("#")) return ColorUtility.TryParseHtmlString(s, out color);

        Match m = Rgb.Match(s);
        if (!m.Success) return false;
        float F(int g) => float.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        color = new Color(F(1) / 255f, F(2) / 255f, F(3) / 255f, m.Groups[4].Success ? F(4) : 1f);
        return true;
    }
}
