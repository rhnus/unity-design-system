using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Single source of truth for the game's text styles (its "type scale"). Each
/// <see cref="TextRole"/> (Display, H1, Body, ...) maps to one font + size + style. Put a
/// <see cref="StyledText"/> on a TMP text, pick a role, and it pulls its look from
/// here — so typography lives in ONE asset instead of being hand-set on every
/// component. Change a role in this asset and every StyledText using it follows.
///
/// There should be exactly ONE of these assets in the project (StyledText auto-wires
/// to it). Create via: Assets ▸ Create ▸ <see cref="DesignSystemNames.TypographyAssetMenu"/>.
/// </summary>
[CreateAssetMenu(fileName = "TypographyScale", menuName = DesignSystemNames.TypographyAssetMenu)]
public class TypographyScale : ScriptableObject
{
    [Serializable]
    public class RoleStyle
    {
        [Tooltip("The built-in role this entry styles. Ignored when Name is set.")]
        public TextRole role;

        [Tooltip("A role of this project's own, by name: texts and styles refer to it by this name. Empty = the built-in role above.")]
        public string id = "";

        /// <summary>The name texts and styles use for this role.</summary>
        public string Id => TextRoles.Resolve(id, role);

        [Tooltip("Font asset for this role. Leave empty to keep each text's existing font (non-destructive).")]
        public TMP_FontAsset font;

        [Tooltip("Point size for this role (absolute — the Canvas Scaler already scales by resolution).")]
        public float fontSize = 18f;

        [Tooltip("Extra style flags. Usually Normal — weight should come from the font asset (e.g. Outfit-Bold), " +
                 "not from faux-bold on top of it.")]
        public FontStyles fontStyle = FontStyles.Normal;

        [Header("Auto-size (optional)")]
        [Tooltip("If on, the size flexes between the min/max below instead of using Font Size.")]
        public bool autoSize = false;
        public float autoSizeMin = 10f;
        public float autoSizeMax = 40f;

        [Header("Spacing (optional)")]
        public float characterSpacing = 0f;
        [Tooltip("Extra line spacing. 0 = leave the text's own value untouched.")]
        public float lineSpacing = 0f;
    }

    [SerializeField] private List<RoleStyle> styles = new List<RoleStyle>();

    private Dictionary<string, RoleStyle> lookup;

    /// <summary>Every role this scale defines, built-in ones first.</summary>
    public IEnumerable<string> RoleIds => styles.Where(s => s != null).Select(s => s.Id).Distinct();

    /// <summary>
    /// Writes a role's font, size, style and spacing onto a text, touching only what differs so re-applying never
    /// dirties a scene needlessly. A role with no font leaves the text's own font. Returns whether anything changed.
    /// </summary>
    public static bool ApplyRole(TMP_Text text, RoleStyle s, float? sizeOverride = null)
    {
        if (text == null || s == null) return false;
        bool changed = false;
        if (s.font != null && text.font != s.font) { text.font = s.font; changed = true; }
        if (text.fontStyle != s.fontStyle) { text.fontStyle = s.fontStyle; changed = true; }

        if (s.autoSize)
        {
            if (!text.enableAutoSizing) { text.enableAutoSizing = true; changed = true; }
            if (!Mathf.Approximately(text.fontSizeMin, s.autoSizeMin)) { text.fontSizeMin = s.autoSizeMin; changed = true; }
            if (!Mathf.Approximately(text.fontSizeMax, s.autoSizeMax)) { text.fontSizeMax = s.autoSizeMax; changed = true; }
        }
        else
        {
            if (text.enableAutoSizing) { text.enableAutoSizing = false; changed = true; }
            float size = sizeOverride ?? s.fontSize;
            if (!Mathf.Approximately(text.fontSize, size)) { text.fontSize = size; changed = true; }
        }

        if (!Mathf.Approximately(text.characterSpacing, s.characterSpacing)) { text.characterSpacing = s.characterSpacing; changed = true; }
        if (s.lineSpacing != 0f && !Mathf.Approximately(text.lineSpacing, s.lineSpacing)) { text.lineSpacing = s.lineSpacing; changed = true; }
        return changed;
    }

    /// <summary>The style for a role, or null if it isn't defined in this asset.</summary>
    public RoleStyle Get(TextRole role) => Get(role.ToString());

    /// <summary>The style for a role id, or null if this asset doesn't define it.</summary>
    public RoleStyle Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (lookup == null || lookup.Count != styles.Count(s => s != null)) RebuildLookup();
        return lookup.TryGetValue(id, out RoleStyle s) ? s : null;
    }

    /// <summary>Adds a role of the project's own, starting from an existing role's look. Returns the new or existing role.</summary>
    public RoleStyle AddRole(string id, string copyFrom = null)
    {
        RoleStyle existing = Get(id);
        if (existing != null) return existing;
        RoleStyle from = Get(copyFrom);
        var added = new RoleStyle { id = id };
        if (from != null)
        {
            added.font = from.font;
            added.fontSize = from.fontSize;
            added.fontStyle = from.fontStyle;
            added.autoSize = from.autoSize;
            added.autoSizeMin = from.autoSizeMin;
            added.autoSizeMax = from.autoSizeMax;
            added.characterSpacing = from.characterSpacing;
            added.lineSpacing = from.lineSpacing;
        }
        styles.Add(added);
        RebuildLookup();
        return added;
    }

    // Two entries with one name would make which one applies depend on list order; the first wins, as in the list.
    private void RebuildLookup()
    {
        lookup = new Dictionary<string, RoleStyle>();
        foreach (RoleStyle s in styles)
            if (s != null && !lookup.ContainsKey(s.Id)) lookup[s.Id] = s;
    }

    private void OnEnable() => RebuildLookup();

    // OnValidate only runs in the editor; the method compiles harmlessly in a build.
    // Guarantees exactly one entry per TextRole so the asset never silently misses a
    // role (e.g. right after a new enum value is added).
    private void OnValidate()
    {
        foreach (TextRole role in Enum.GetValues(typeof(TextRole)))
            if (!styles.Exists(s => s != null && s.Id == role.ToString()))
                styles.Add(new RoleStyle { role = role });
        RebuildLookup();
    }
}

/// <summary>
/// Semantic text styles. Rename / add / remove to match the design system the
/// designer defines — the enum is the vocabulary the whole team picks from.
/// </summary>
public enum TextRole
{
    Display,     // Outfit-medium 36 — big splash titles / large figures
    H1,          // Outfit-medium 30 — primary heading
    H2,          // Outfit-SemiBold 24 — section heading
    ContentH3,          // Outfit-Bold 18 — sub-heading / strong label
    BodyLarge,   // Outfit-Regular 24 — prominent body / primary UI text
    Body,        // Outfit-Regular 20 — default body
    BodySmall,        // Outfit-Regular 16 — default body
    BodyXSmall,   // Outfit-Regular 12 — dense / secondary text
    Caption,     // Outfit-Regular 10 — smallest labels / captions
    Button,       // Outfit-SemiBold 20 — button labels
    MCQTitle1,      // Outfit-SemiBold 24 — card title text
    MCQTitle2,      // Outfit-Light 24 — card title text
    MCQBody1,       // Outfit-Regular 16 — card body text
    MCQBody2,       // Outfit-Regular 12 — card body text
    DragDropCardBody       // Outfit-Medium 16 — card body text

}
