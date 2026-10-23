using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Outlines every button and toggle in the Scene view by status — styled, not styled, has an issue, or a picture
/// left as is — and names it on hover, so unstyled and broken controls are found by looking rather than by
/// scanning. Off by default; switched on from the "Style status" overlay. Statuses are rebuilt when the scene or assets change,
/// never per frame.
/// </summary>
[InitializeOnLoad]
public static class ButtonStatusView
{
    public enum Status { Styled, NotStyled, Issue, Picture }

    private struct Entry
    {
        public Selectable Button;
        public Status Status;
        public string Text;
    }

    private const string PrefKey = "DesignSystem.ButtonStatusView";
    private static readonly Vector3[] corners = new Vector3[4];
    private static List<Entry> entries = new List<Entry>();
    private static bool dirty = true;
    private static GUIStyle tip;

    public static readonly Color StyledColour = new Color(0.30f, 0.75f, 0.35f);
    public static readonly Color NotStyledColour = new Color(1f, 0.65f, 0.1f);
    public static readonly Color IssueColour = new Color(0.95f, 0.25f, 0.25f);
    public static readonly Color PictureColour = new Color(0.55f, 0.55f, 0.6f);

    static ButtonStatusView()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += Invalidate;
        EditorApplication.projectChanged += Invalidate;
        Undo.undoRedoPerformed += Invalidate;
        PrefabStage.prefabStageOpened += _ => Invalidate();
        PrefabStage.prefabStageClosing += _ => Invalidate();
    }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, false);
        set
        {
            EditorPrefs.SetBool(PrefKey, value);
            Invalidate();
            SceneView.RepaintAll();
        }
    }

    public static void Invalidate() => dirty = true;

    public static int Count(Status status)
    {
        if (dirty) Rebuild();
        return entries.Count(e => e.Status == status);
    }

    public static void Select(Status status) => Selection.objects = GameObjects(status).Cast<Object>().ToArray();

    public static List<GameObject> GameObjects(Status status)
    {
        if (dirty) Rebuild();
        return entries.Where(e => e.Status == status && e.Button != null).Select(e => e.Button.gameObject).ToList();
    }

    private static void Rebuild()
    {
        dirty = false;
        entries = new List<Entry>();
        foreach (Selectable b in StageUtility.GetCurrentStageHandle().FindComponentsOfType<Selectable>())
        {
            if (b == null || !b.gameObject.activeInHierarchy || !ComponentKinds.IsScannable(b)) continue;
            entries.Add(Describe(b));
        }
    }

    private static Entry Describe(Selectable b)
    {
        if (b.TryGetComponent(out StyledSelectable styled) && styled.ComponentStyle != null)
        {
            UIComponentStyle style = styled.ComponentStyle;
            string issue = DesignSystemCheck.LabelFitProblem(styled)
                           ?? LayoutDiagnostics.Diagnose((RectTransform)b.transform, style).FirstOrDefault(i => !i.Benign)?.Message;
            return new Entry { Button = b, Status = issue != null ? Status.Issue : Status.Styled,
                               Text = issue != null ? $"{style.name}\n{issue}" : style.name };
        }
        Image bg = StyledSelectable.FindBackground(b);
        bool picture = bg != null && bg.sprite != null && !SurfaceLook.IsShape(bg.sprite);
        return new Entry { Button = b, Status = picture ? Status.Picture : Status.NotStyled,
                           Text = picture ? "Picture — left as it is" : "Not styled — Style these in the Style status overlay, or right-click ▸ Apply Style…" };
    }

    public static Color ColourOf(Status status) => status switch
    {
        Status.Styled => StyledColour,
        Status.Issue => IssueColour,
        Status.Picture => PictureColour,
        _ => NotStyledColour,
    };

    private static void OnSceneGUI(SceneView view)
    {
        if (!Enabled) return;
        if (dirty) Rebuild();

        Event e = Event.current;
        Entry? hovered = null;
        float hoveredArea = float.MaxValue;
        foreach (Entry entry in entries)
        {
            if (entry.Button == null) { dirty = true; continue; }
            ((RectTransform)entry.Button.transform).GetWorldCorners(corners);
            Color c = ColourOf(entry.Status);
            Handles.DrawSolidRectangleWithOutline(corners, new Color(c.r, c.g, c.b, 0.08f), c);

            Rect gui = GuiRect(corners);
            float area = gui.width * gui.height;
            if (gui.Contains(e.mousePosition) && area < hoveredArea) { hovered = entry; hoveredArea = area; }
        }

        if (hovered.HasValue)
        {
            tip ??= new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontSize = 11 };
            Handles.BeginGUI();
            var content = new GUIContent(hovered.Value.Text);
            float width = Mathf.Min(320f, tip.CalcSize(content).x + 8f);
            float height = tip.CalcHeight(content, width);
            GUI.Label(new Rect(e.mousePosition.x + 14f, e.mousePosition.y + 14f, width, height), content, tip);
            Handles.EndGUI();
        }
        if (e.type == EventType.MouseMove) view.Repaint();
    }

    private static Rect GuiRect(Vector3[] world)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 w in world)
        {
            Vector2 p = HandleUtility.WorldToGUIPoint(w);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}

/// <summary>The Scene view's "Style status" overlay: one toggle, a legend with counts, and select-by-status.</summary>
[Overlay(typeof(SceneView), "adgbl-button-status", "Style status")]
public class ButtonStatusOverlay : IMGUIOverlay
{
    public override void OnGUI()
    {
        bool on = GUILayout.Toggle(ButtonStatusView.Enabled, "Show style status");
        if (on != ButtonStatusView.Enabled) ButtonStatusView.Enabled = on;
        if (!on) return;

        Row(ButtonStatusView.Status.Styled, "Styled");
        Row(ButtonStatusView.Status.NotStyled, "Not styled");
        Row(ButtonStatusView.Status.Issue, "Has an issue");
        Row(ButtonStatusView.Status.Picture, "Picture");
    }

    private static void Row(ButtonStatusView.Status status, string label)
    {
        using (new GUILayout.HorizontalScope())
        {
            Rect r = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
            r.y += 4f;
            EditorGUI.DrawRect(r, ButtonStatusView.ColourOf(status));
            int count = ButtonStatusView.Count(status);
            GUILayout.Label($"{label}  {count}", GUILayout.Width(110f));
            if (status == ButtonStatusView.Status.NotStyled)
            {
                using (new EditorGUI.DisabledScope(count == 0))
                    if (GUILayout.Button(new GUIContent("Style these", "Style every one a style already matches with no visible change, " +
                                                                       "then review the rest."), EditorStyles.miniButton))
                        DesignSystemContextMenu.StyleChildren(ButtonStatusView.GameObjects(status));
            }
            else
                using (new EditorGUI.DisabledScope(count == 0 || status != ButtonStatusView.Status.Issue))
                    if (GUILayout.Button(new GUIContent("Select", "Select these to see each issue in its Inspector."), EditorStyles.miniButton))
                        ButtonStatusView.Select(status);
        }
    }
}
