using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Shows what a style produces. First choice is a real render — sprite, recolour, TMP font — of a control of the
/// style's kind built in a private preview scene, in the Off or On state. If that render comes out empty (a render pipeline that won't draw UI in
/// preview scenes), it falls back for good to a drawn approximation: the style's shape colour and corner with its
/// label at the right size and colour. Either way the Inspector never shows a blank box.
/// </summary>
public sealed class ButtonPreviewRenderer : IDisposable
{
    /// <summary>The resolved values a drawn preview needs, captured so a "before" can outlive later edits.</summary>
    public struct Spec
    {
        public Color Fill, Label;
        public float Width, Height, Radius, FontSize;
        public string Text;
    }

    private PreviewRenderUtility preview;
    private GameObject root;
    private RectTransform buttonRect;
    private StyledElement styled;
    private IComponentKind kind;
    private bool? rendersUI;

    /// <summary>Whether real renders work here; false once one came out empty.</summary>
    public bool UsesRender => rendersUI != false;

    /// <summary>Draws the style's preview into rect. Returns the render texture when a real render was drawn.</summary>
    public Texture Draw(Rect rect, UIComponentStyle style, bool on = false)
    {
        if (Event.current.type != EventType.Repaint || style == null) return null;
        Texture texture = rendersUI == false ? null : Render(style, rect, on);
        if (texture != null && rendersUI == null) rendersUI = !IsBlank(texture);
        if (texture != null && rendersUI == true)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            return texture;
        }
        DrawSpec(rect, Capture(style, on));
        return null;
    }

    public static Spec Capture(UIComponentStyle style, bool on = false)
    {
        float fontSize = 20f;
        UITheme theme = style.Theme;
        if (style.TryGetLabelRoleId(out string role) && theme != null && theme.Typography != null)
        {
            TypographyScale.RoleStyle rs = theme.Typography.Get(role);
            if (rs != null) fontSize = rs.autoSize ? rs.autoSizeMax : rs.fontSize;
        }
        if (style.LabelSize > 0f) fontSize = style.LabelSize;
        float height = style.Height > 0f ? style.Height : 56f;
        return new Spec
        {
            Fill = StyleConsolidation.VisibleColour(style, on),
            Label = style.LabelColorFor(on).TryResolve(theme, out Color ink) ? ink : Color.black,
            Width = style.Square ? height : Mathf.Max(style.Width, style.MinWidth, 180f),
            Height = height,
            Radius = style.Background.cornerRadius,
            FontSize = fontSize,
            Text = KindOf(style).DisplayName,
        };
    }

    /// <summary>A rounded shape in the style's colour with its label — close to, but not exactly, the real button.</summary>
    public static void DrawSpec(Rect rect, Spec spec)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.78f, 0.78f, 0.78f));
        float scale = Mathf.Min(rect.width * 0.9f / spec.Width, rect.height * 0.8f / spec.Height, 1f);
        var size = new Vector2(spec.Width * scale, spec.Height * scale);
        var button = new Rect(rect.center - size / 2f, size);
        GUI.DrawTexture(button, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, spec.Fill, Vector4.zero,
                        Mathf.Min(spec.Radius * scale, size.y / 2f));
        var label = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(6, Mathf.RoundToInt(spec.FontSize * scale)),
            normal = { textColor = spec.Label },
        };
        GUI.Label(button, spec.Text, label);
    }

    private static IComponentKind KindOf(UIComponentStyle style) => ComponentKinds.For(style) ?? ComponentKinds.Buttons;

    private Texture Render(UIComponentStyle style, Rect rect, bool on)
    {
        if (rect.width < 2f || rect.height < 2f) return null;
        try
        {
            Build(KindOf(style));
            Spec spec = Capture(style, on);
            buttonRect.sizeDelta = new Vector2(spec.Width, spec.Height);
            if (styled.Label != null) styled.Label.text = spec.Text;
            if (styled.TryGetComponent(out Toggle toggle)) toggle.SetIsOnWithoutNotify(on);
            styled.AssignStyle(null);
            styled.AssignStyle(style);
            if (styled.Label != null) styled.Label.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();

            Camera cam = preview.camera;
            float aspect = rect.width / rect.height;
            cam.orthographicSize = Mathf.Max(spec.Height * 0.65f, spec.Width / aspect * 0.6f);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -100f), Quaternion.identity);

            preview.BeginPreview(rect, GUIStyle.none);
            cam.Render();
            return preview.EndPreview();
        }
        catch (Exception)
        {
            rendersUI = false;
            return null;
        }
    }

    // The centre of a real render is the button; if it matches the corner, which is always background, nothing drew.
    private static bool IsBlank(Texture texture)
    {
        if (!(texture is RenderTexture rt)) return true;
        RenderTexture previous = RenderTexture.active;
        var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            Color centre = read.GetPixel(rt.width / 2, rt.height / 2), corner = read.GetPixel(1, 1);
            return Mathf.Abs(centre.r - corner.r) + Mathf.Abs(centre.g - corner.g) + Mathf.Abs(centre.b - corner.b) < 0.02f;
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(read);
        }
    }

    /// <summary>A copy of a real render that survives later renders — used to keep a "before" image.</summary>
    public static RenderTexture Snapshot(Texture source)
    {
        if (source == null) return null;
        var copy = new RenderTexture(source.width, source.height, 0) { hideFlags = HideFlags.HideAndDontSave };
        Graphics.Blit(source, copy);
        return copy;
    }

    // The preview control is rebuilt when a style of another kind is shown, since each kind has its own control.
    private void Build(IComponentKind controlKind)
    {
        if (preview != null && kind == controlKind) return;
        Dispose();
        kind = controlKind;
        preview = new PreviewRenderUtility();
        Camera cam = preview.camera;
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.78f, 0.78f, 0.78f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 500f;
        // The preview camera only draws its private culling layer by default; the preview scene holds nothing but
        // this button, so drawing every layer is safe and doesn't depend on that internal layer number.
        cam.cullingMask = ~0;

        root = new GameObject("Style Preview", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        ((RectTransform)root.transform).sizeDelta = new Vector2(800f, 400f);

        var go = new GameObject(kind.DisplayName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root.transform, false);
        if (go.GetComponent(kind.ControlType) == null && go.AddComponent(kind.ControlType) is Selectable control)
            control.targetGraphic = go.GetComponent<Image>();
        buttonRect = (RectTransform)go.transform;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;

        styled = (StyledElement)go.AddComponent(kind.StyledType);
        styled.AutoDetect();
        preview.AddSingleGO(root);
    }

    public void Dispose()
    {
        preview?.Cleanup();
        preview = null;
        if (root != null) Object.DestroyImmediate(root);
        root = null;
    }
}
