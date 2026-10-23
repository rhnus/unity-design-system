using System;
using UnityEngine;

/// <summary>
/// Text roles are named by id: a built-in role's id is its <see cref="TextRole"/> name ("Button", "BodySmall"), and a
/// project adds its own roles in the <see cref="TypographyScale"/> without touching code. Each place that stores a
/// role keeps the older built-in field beside the id; an empty id means "the built-in field", so assets saved before
/// ids existed read exactly as they did.
/// </summary>
public static class TextRoles
{
    /// <summary>The role an id field and its older built-in field name together.</summary>
    public static string Resolve(string id, TextRole builtIn) => string.IsNullOrEmpty(id) ? builtIn.ToString() : id;

    /// <summary>The built-in role an id names, if it names one.</summary>
    public static bool TryGetBuiltIn(string id, out TextRole role) =>
        Enum.TryParse(id, false, out role) && Enum.IsDefined(typeof(TextRole), role);
}

/// <summary>
/// Draws a string field as a choice of the type scale's roles. <see cref="BuiltInField"/> names the older
/// <see cref="TextRole"/> field beside it, which an empty id falls back to and which a built-in choice keeps in step.
/// </summary>
public class TextRoleIdAttribute : PropertyAttribute
{
    public readonly string BuiltInField;
    public TextRoleIdAttribute(string builtInField) => BuiltInField = builtInField;
}
