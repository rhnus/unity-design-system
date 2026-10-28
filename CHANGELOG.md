# Changelog

## Unreleased

- **Sharp rounded corners.** `CornerSharpness.Upscale(image)` measures how far a sliced Image's corner art is drawn
  beyond its source pixels (above 1 is blurry; ½ or less stays crisp on a 2× screen), and `MultiplierFor` gives the
  multiplier that draws a radius from a sprite's corner. The README's **Sharp rounded corners** section states the
  rule: fix a blurry corner with larger corner art at the same radius, never with a lower multiplier.
- **Disabled look for buttons.** A button style's **Disabled look** gives a button that can't be used its own fill
  and label colour in play, in place of the Disabled Tint, which can only darken. `UIButtonStyle.UsesDisabledLook`,
  `DisabledFill`, `DisabledLabelColor`; `StyledButton.ShowsDisabledLook`. `UIComponentStyle.StateColors` is now
  virtual. The README's **Disabled buttons** section states the rule: primary buttons go light grey.
- **Rounded Square retired.** Every use moves to Semi Rounded at a multiplier of 2. The theme's Shape/Rounded Square
  token stays for old references and is not for new styles.
- **State styles** (`UIStateStyle`, Create ▸ Design System ▸ State Style) for components whose look follows game
  state rather than pointer input: a name tag that is Locked, Selected, Correct, Steer or Resolved. Each state, named
  as in the Figma variant, gives a fill, border, ink and accent token and an opacity; the component keeps its own
  state machine and calls `Resolve(state, fallback)`. Anything the style leaves empty keeps the component's colour.
- The generated design system doc lists every state style under **Components ▸ State styles**.
- **Figma Review window** (**Tools ▸ Design System ▸ Figma ▸ Review…**): every mapped component with Figma and Unity
  side by side as swatches, a state drawn at reduced opacity also shown as drawn, each row's result, and the
  decisions a developer makes: confirm a suggested mapping, mark a component as not used in the game, keep a value
  under an agreed platform rule (only rules that would count are offered), or open Style Proposals to follow Figma.
  Opening it writes nothing (`FigmaSync.Preview`); a decision writes only `component-map.json` or `adaptations.json`
  and regenerates the doc. New agent command `figma-unmap` marks a component as not used, so it is neither compared
  nor suggested again.
- **Pictures in Figma Review.** Each panel shows the Figma picture of every compared variant (saved by
  `Tools/figma_sync.py` under `figma/images/`, named by variant id: `FigmaSync.ImagePath`), and a **Used in the game**
  list: each prefab control or scene object that references the style (`StyleUses`, found by GUID; prefabs read
  through SerializedObject, scenes read as text and never opened), with **Ping**, **Open prefab** (selects the
  control) and **Capture controls**, which renders up to six of them as saved in the prefab, through the same capture
  Style Proposals uses. Nothing is written.
- **The Figma sync compares state styles.** A `component-map.json` entry with `"kind": "state"` maps a Figma
  component set to a `UIStateStyle`: each state is matched to the variant with the same `State` (case and spaces
  ignored) and compared on fill, border (Figma stroke) and ink (label colour) as fields such as `Selected.fill`, under
  the same three-way rule, adaptations and platform rules. States only one side has, and state styles no component
  follows, are listed in the report and the doc. A component named like a state style (`G3/Name tag` →
  `State_G3_NameTag`) is suggested, never confirmed. A platform rule's `fields` entry `fill` covers every state's fill.
- **Figma wins unless a platform rule says otherwise.** A Figma adaptation now counts only when it cites a rule in
  `figma/rules.json` that the designer has agreed and that covers the field; any other difference is listed as
  *follow Figma*, with the reason a mark doesn't count. `figma-mark` takes a required `rule` (and an optional
  `reason` note) and refuses a rule that wouldn't count. The doc's groups are now **matches**, **differs by platform
  rule** and **to do**, with a new **Platform rules** section; the result *to triage* is now *follow Figma*
  (`FigmaResult.FollowFigma`). `FigmaSync.Compare` takes the rules after the adaptations.

## 0.2.0 — 2026-10-23

- **Starter kit for new projects.** **Tools ▸ Design System ▸ Set Up New Project…** builds a project's own theme, type
  scale, rounded shape sprite, button styles (Primary, Secondary, Ghost and a Small variant of Primary), a card panel
  style, settings and a preview prefab from a preset, in the folder you pick. It never overwrites an asset and only
  runs in a project with no theme yet.
- **Presets as data**, shipped in the **Starter Kit** sample: *Neutral* and *Bold*. A project's own `.preset.json`
  under `Assets/` is listed beside them.
- The old **Advanced ▸ Create Starter Kit** (orange, with this game's sprites) is replaced by the wizard.
- Agent commands `starter-presets` and `starter-create`.
- `DesignSystemSettings.GetOrCreate(folder)` and `ForgetCached()`.

## 0.1.0 — 2026-10-23

First release as a package, moved out of ADGBL's `Assets/Scripts/UI/DesignSystem`.

- Theme tokens (colours, sprites, named sizes), type scale, styles and variants for buttons, toggles, panels and drag cards.
- Text roles are data: a project adds its own in the type scale; the built-in roles stay for code that names them.
- Project settings (excluded folders, document title and folder, drag card base class) live in the Design System Settings
  asset; the tools create assets next to it.
- Scan and setup, Check (including text fit), Tidy Up (merge and organise), Style Proposals, Unique Design.
- Generated design system document and Figma sync (three-way compare, component map, adaptation marks).
- Menus are under **Tools ▸ Design System**.
