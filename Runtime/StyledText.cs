using TMPro;
using UnityEngine;

/// <summary>
/// Drives a TMP text's font / size / style from a <see cref="TypographyScale"/> role,
/// so typography is owned by one asset instead of hand-set on every component. Add it,
/// pick a role, done — you never touch Font Asset / Font Size on the text again.
///
/// Applies in the editor too (<see cref="ExecuteAlways"/>) so the Scene view shows the
/// real look without pressing Play. It is non-destructive: a role with no font leaves
/// the text's existing font, and adding this component to a text changes nothing the
/// role doesn't explicitly define.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class StyledText : MonoBehaviour
{
    [Tooltip("The single source of truth. Auto-filled in the editor when left empty (there should be one in the project).")]
    [SerializeField] private TypographyScale scale;

    [SerializeField, HideInInspector] private TextRole role = TextRole.Body;
    [Tooltip("Which text style this is. Pick from the design system's vocabulary.")]
    [TextRoleId(nameof(role))]
    [SerializeField] private string roleId = "";

    [Header("Per-instance override (rare one-offs)")]
    [Tooltip("Use a custom size instead of the role's size. Prefer adding a real role over using this.")]
    [SerializeField] private bool overrideSize = false;
    [SerializeField] private float sizeOverride = 18f;

    private TMP_Text text;

    /// <summary>The role this text uses, by id. Setting it re-applies immediately.</summary>
    public string RoleId
    {
        get => TextRoles.Resolve(roleId, role);
        set
        {
            roleId = value ?? "";
            if (TextRoles.TryGetBuiltIn(roleId, out TextRole builtIn)) role = builtIn;
            Apply();
        }
    }

    /// <summary>The built-in role this text uses; a role of the project's own reads as the last built-in one it had.</summary>
    public TextRole Role { get => TextRoles.TryGetBuiltIn(RoleId, out TextRole r) ? r : role; set => RoleId = value.ToString(); }

    /// <summary>Sets or clears the per-instance size override and re-applies. Used by a button style with its own text size.</summary>
    public void OverrideSize(float? size)
    {
        overrideSize = size.HasValue;
        if (size.HasValue) sizeOverride = size.Value;
        Apply();
    }

    /// <summary>The size this text is held at instead of its role's, if any.</summary>
    public float? SizeOverride => overrideSize ? sizeOverride : (float?)null;

    /// <summary>The scale asset backing this text. Setting it re-applies immediately.</summary>
    public TypographyScale Scale { get => scale; set { scale = value; Apply(); } }

    private void OnEnable() => Apply();

    /// <summary>Pull font / size / style from the scale for the current role. Returns whether anything changed.</summary>
    public bool Apply()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        if (text == null || scale == null) return false;
        return TypographyScale.ApplyRole(text, scale.Get(RoleId), overrideSize ? sizeOverride : (float?)null);
    }
}
