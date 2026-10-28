# Design System

Define how buttons, toggles and panels look once, and every one follows: colours, shapes, corners, fonts and sizes.

## Quick start

**The theme holds the defaults. Each style follows the theme unless you override a field. Each button uses a style.**

Open **Tools ▸ Design System ▸ Open Design System**. A **Next:** line at the top always names the one thing worth doing now, with a **Go** button. It has three tabs:

- **Overview** sets the project up.
  - **Scan project** previews exactly what will change, for buttons, toggles and panels. Then **Style N**.
  - A **panel** is any visible shape Image (a sliced sprite, or a flat colour) at least 40 px on its smaller side that isn't part of a control: cards, windows, backgrounds, overlays. Pictures, progress fills, masks that don't draw and thin dividers are left alone.
  - Buttons whose image is a picture (icons, characters, checkboxes) are left alone.
  - A **toggle group** is styled as one: only when a single style matches every toggle in it. Otherwise it waits in **Review… ▸ By toggle group**, where **Apply** gives the whole group one style. When the selected toggle is drawn in its own colour, **Make consistent** turns that colour into the style's On state, so nothing changes on screen.
  - New buttons and toggles are styled automatically with their kind's default (setup picks the most-used style of each).
- **Styles** is where the look is aligned.
  - At the top are the **theme defaults**: **Button height** and **Button text** (role and size). Every style set to *Use theme* follows them.
  - Below is one card per style. Collapsed, it shows what the style gives its buttons and where each value comes from: *(theme)*, or the style's own setting. Expanded, you edit fill, label colour, corner, text and height in place.
  - Once you have toggle styles, a **All · Button · Toggle** filter appears above the cards. A toggle card's swatch shows Off, then On.
  - **Tidy up styles** has two tabs. **Merge** folds near-duplicates of one colour into a single style. **Organise** arranges the button styles into roles, sizes and icon variants and renames them to match (see *How styles are organised*).
- **Check** is the safety net. Run it after any change and before committing. It finds:
  - labels that no longer fit their button;
  - layouts that fight a size;
  - pictures styled as shapes;
  - sprites the styles draw with that live in a third-party folder (the fix moves them into the design system's folder; every reference follows);
  - colours that were deleted;
  - buttons that can't take the style height;
  - toggle groups whose toggles don't share one style (one-click fix);
  - a script that holds a toggle's background while its style sets an On colour, so the two would fight;
  - card and tab styles whose On state is missing or barely visible;
  - toggles still styled with a button style (the fix keeps their look).
  - content text that doesn't fit the control showing it (see **Text fit** below).

  Every issue has **Select** and, where possible, a one-click **Fix**. After every change, the open scene or prefab is checked on its own, and the **Next:** line reports what it finds; run the full check before committing.

**Which style uses which font?** The Styles tab says on every card, e.g. *Button · Outfit-SemiBold · 20 px (theme)*. The **type scale** Inspector lists the button styles using each role.

You don't need any new art. Sliced sprites with colour baked in (like an orange `MainButton.png`) still follow colour tokens, through **Recolor**.

## How styles are organised

A button style answers three questions, and its name says the answers in order:

```
Button _ Primary _ Small _ Icon _ Send
 kind     role     size    shape  use case (only when it's genuinely special)
```

| Part | What it means | Where it lives |
|---|---|---|
| **Role** | The look: fill, shape, corner, label colour. *Primary* is the main action, *Secondary* the alternative, *Ghost* the quiet one. | The role's own style, e.g. `Button_Primary`. |
| **Size** | One of the theme's named sizes: **Regular** (56), **Small** (40), **XSmall** (28). Regular is left out of the name. A size can also set the label's text role, so small buttons get small text. | **UI Theme ▸ Sizes**. Change a height there and every style at that size follows. |
| **Icon** | Square: the width follows the height. | The style's **Square** setting. |
| **Use case** | Only for a style that really differs, e.g. the 50 px chat *Send* button. A one-off height with no use case is named by its pixels (`_H50`) until someone decides. | — |

**Width is not a style matter.** Layout groups and the buttons themselves decide it. Two styles that differ only in width are the same style, so *Organise* merges them. The exceptions are a role's minimum width and an icon button's square shape.

**Sizes and icons are variants of their role** (Primary ▸ Primary Small ▸ Primary Small Icon). Change the role's fill, and all its sizes change with it. Anything a variant does differently (its own corner, label size, and so on) is kept as an override; the style's Inspector shows it with a revert button.

**Adding a button style:** start from the role (its card's ⋯ ▸ **Create variant…**), pick a size, and tick Square if it's an icon. Then name it with the pattern above. If no named size fits, ask whether the design should use one before adding a pixel height.

**Organise** (Tidy Up Styles ▸ Organise) does this for existing styles. It proposes a place and a name for each style, with these rules:

- Applying the ticked rows never changes how a button looks. Linking a style to its role keeps every difference as an override.
- A row that would change the screen starts unticked and lists what changes: a merge that drops a width, or a snap of a one-off height to a named size.
- A row with a warning is skipped. For example, two styles heading for the same name need a use-case word.

Commit first: merges move buttons in prefabs and scenes.

## Style proposals (finding odd-looking controls)

Setup styles every control that a style fits with **no visible change**. Style proposals handle the rest: controls that look a bit off, or that look nothing like any style.

1. Ask Claude to **review the styles** of a prefab, folder, scene or what you have open. The `style-review` skill finds the controls and captures each one in its real row, before and after every candidate style. It then decides, per look:
   - **use a style**, optionally keeping the control's own size so nothing moves;
   - **make a special variant** of a role, for a recurring special size or shape;
   - **unique by design**, for a deliberate one-off;
   - **leave it**, when a designer should decide.
2. Open **Tools ▸ Design System ▸ Style Proposals**. Each card shows *Now* and *Proposed* side by side, the reason, and whether the layout changes.
3. **Approve** or **Reject** each card. A proposal that moves or resizes anything also needs **I've checked the layout change** ticked.
4. **Apply approved** is the only step that edits prefabs and scenes. Commit first.

- **One card is one look.** The decision applies to every control that looks exactly the same, and the card lists them.
- **"Unique by design"** adds a **Unique Design** component (with the reason and who decided). Setup, Style Review and future proposals then leave that control alone. Remove the component to reconsider it.
- **Captures are made in Edit mode** from hidden copies that are never saved, so nothing in the scene changes. Every step refuses to run in Play mode.
- Runs are kept in `Artifacts/StyleReview/` (not committed).

## Shortcuts outside the window

| Where | What |
|---|---|
| Hierarchy right-click ▸ **Design System** | **Style Children** styles every button and toggle under the selection that a style already matches with no visible change, then opens Review on the rest. It works in a scene or an open prefab. Controls inside prefab instances are styled in the prefab that defines them; it asks first only then, because those files are saved. Changes in the open scene are one undo step. **Apply Style…** (searchable picker), **Remove Style** and **Select Style Asset** work on buttons, toggles, input fields and dropdowns, including those inside the selection. |
| Scene view overlay **Style status** (or Design System ⋯ ▸ Show style status) | Outlines buttons and toggles: green = styled, amber = not styled, red = has an issue, grey = picture. Hover one for its style or problem. **Style these** styles every not-styled one a style matches, the same way as Style Children. |
| Style Inspector | A real preview, with **Before / Now** after an edit. 🔒 **Lock** an approved style. **Find everywhere** lists every file using it. **Import spec from Figma…** is under Rarely needed. |
| Tools ▸ Design System ▸ **Write Design System Doc** | Writes `docs/design-system/DESIGN_SYSTEM.md` and `tokens.json`: every token and style, the Figma name each should match, and how many controls use it. It opens with a brief for a designer's AI agent, so a designer can give it to an agent with a Figma connector and get a comparison with the Figma library. See `docs/figma-unity-pipeline.md`. |
| Builds | The check runs before each build. Interactive builds ask before continuing if it finds problems; batch builds only log them. Turn it off in Settings ▸ Check Before Build. |

## Editing a style

The Inspector shows the everyday fields first: **Shape**, **Fill Mode**, **Fill**, **Corner Radius**, label colour and font, and size. Everything else is under **Advanced**.

- **Corner Radius** is in pixels, the number Figma shows. The Pixels Per Unit Multiplier is worked out for you.
- **Fill Mode**:
  - **Tint (white sprites)** multiplies the colour over the sprite.
  - **Recolor (coloured sprites)** replaces the sprite's own colour and keeps its shading and shadow.
  - If you pick the wrong one, the Inspector says so and offers a one-click fix.
- **Drag card styles** cover the fixed core of a draggable card: the **Handle** (frame, inner panel on a two-tone handle, grip icon colour, width) and the body as the Look. What a card displays (ID chip, duration, count) stays its own. Any component whose class derives from one named `DraggableItem` is a drag card; its parts are found by name (`Draggable Icon Group` / `Grip` for the handle, `Body` / `BG` / `ActivityBG` for the body, `Label` for the title). The card script keeps every behaviour; the style only paints.
  - **Converging the families:** pick one style as the standard (Styles ▸ ⋯ ▸ **Set as the standard drag card**, marked ★). The Check tab then notes how every other card family differs from it. **Follow the standard handle** makes a family take the handle from the standard, now and whenever it changes, and keeps its own body and text. Any other field can be set to follow later, one at a time, by reverting it in the style's Inspector.
  - **Cards without a handle** (the older LO4 cards) get a Check note; its fix builds the standard handle as the card's first child. That moves the card's content, so it's never applied unasked.
- **Panel styles** show only **Look** and **Layout** (padding and spacing for a layout group on the panel): panels have no label, hover or size of their own, so a panel style never touches them.
- **Toggle styles:** pick the **Toggle type** first; the Inspector shows only what that type needs. Look and Label are the Off state; **When on** sets only what changes (fill, text colour; shape for checkboxes and radios), and the preview shows Off and On side by side. The **Checkmark** section appears for checkboxes and radios. A new toggle style keeps each toggle's own height and label font.
- **On colours work with game code** that resets a toggle's listeners or sets it silently (quiz options): the toggle notices its value changed and shows the matching look.
- **Grow with text** (under Height) makes the height a minimum: a control whose text needs more room grows to show it. Use it for option cards and other controls that carry sentences rather than a word or two.
- **Text fit:** in **Advanced ▸ Settings ▸ Text fit**, each rule names content files, a field and the control it appears in, for example `QualityLO5*.json`, `points[].options[].label`, the site audit option card, at most 3 lines. Check measures every string at that control's label width, font and size:
  - text taller than a fixed-height control is a problem;
  - text over the line limit is a warning for the content's author.
- **Height / Width / Min Width** work inside layout groups and Content Size Fitters. Sizes go through a Layout Element, which is added where a layout needs one. Stretched axes and grid cells are left alone.

## Sharp rounded corners

A sliced corner is drawn at *border ÷ (pixels per unit × Pixels Per Unit Multiplier)* canvas pixels. When that is larger than the corner art in the sprite, the corner is upscaled and looks blurry or stepped, and it gets worse on a 2× screen or when the panel is scaled up (an enlarged phone, a hover lift).

- **The rule:** a corner never draws larger than its source art. `CornerSharpness.Upscale(image)` gives canvas pixels per source pixel: above 1 is blurry everywhere; ½ or less stays crisp on a 2× screen and when scaled.
- **Rounded Square is retired. Use Semi Rounded at a Pixels Per Unit Multiplier of 2** — a 13px corner, crisp on a 2× screen — wherever a panel, card or button used it. Rounded Square's 68px art is too small for the panels it was stretched over. The theme keeps its Shape/Rounded Square token only so old references still resolve; don't pick it for a new style.
- **Aim for a multiplier that draws the art at half size or less** — typically 2 to 3 for a 100 PPU shape. A corner drawn 1:1 is the edge of acceptable and already looks soft on most screens.
- **Fix it with the sprite, not by lowering the multiplier.** A lower multiplier gives a bigger corner from the same few pixels, which is what makes it blurry. A higher one sharpens it but also shrinks the radius. To keep the radius, use a sprite with larger corner art and the multiplier that draws the same radius from it (`CornerSharpness.MultiplierFor`):
  - up to a 13px radius: **Semi Rounded** at a multiplier of 2 or more;
  - larger radii and pills: **Circle - 256**.
- **Convert by the curve, not the slice border.** The visible corner is the curve the art draws, which is often narrower than its border: Semi Rounded draws a 26px curve in a 30px border, Rounded Square 20px in 32px, Circle - 256 the full 128px. Converting by the border rounds the corner further than it was.
- **A new sprite can resize the box.** A sliced Image reports a min and preferred size from its sprite's borders and pixel size. Where a parent layout group controls the Image's size, or a Content Size Fitter sits on it, swapping the sprite changes those numbers and the box grows or shrinks (a bubble gains empty padding, a button panel runs off screen). Measure `LayoutUtility` before and after, and leave such an Image alone or give it a Layout Element that fixes its size first.
- **Unity's built-in UISprite and Background draw a 7.5px curve** (inside a 10px border, 1.5px in from the edge) at 200 pixels per unit, so any multiplier below 1 upscales them. Don't use them for rounded panels.
- A style with **Corner Radius** set works out the multiplier for you. Pick a **Shape** whose corner art is at least twice that radius.

## Disabled buttons

- **The rule:** a primary button that can't be used yet is light grey, as in the Figma G3/Action button's Disabled state: fill **State/Disabled/Fill** (#E6E6E6) and label **State/Disabled/Ink** (#A3A3A3). It never shows as a faded or darker orange. Every `Button_Primary` style has this switched on.
- **Disabled look** (on a button style) turns it on. In play, a button whose Button, or a Canvas Group above it, is not interactable swaps to the disabled fill and label colour, and swaps back when it can be used again. The Disabled Tint is not multiplied over it.
- **Why a look and not a tint:** the Disabled Tint multiplies the button's own colour, so it can only darken. Orange times any tint is a muddy orange or brown, never grey.
- **Gate with `interactable`.** Hiding a gated button with SetActive throws away what the player is working towards.
- A recoloured shape keeps its shading, so the orange border becomes a slightly darker grey, close to Figma's #D0D0D0.
- **In the editor** a button saved non-interactable still shows its live colours, so prefabs are never saved grey.

## Right / wrong

One look for every graded answer, set once in the **UI Theme ▸ Right / wrong** section: a fill, a border and a mark (tick / cross) for each verdict. Tick **Use for every graded answer** to switch it on; until then every component keeps its own colours.

- Components paint the parts they already have — no new fields on prefabs. A quiz option puts the fill on its result background, the border on its outline and check disc, and the mark in its selection box; an option card puts the fill on its selected graphic, the border on its deco and the mark as the deco sprite; a drag card puts the fill (and border) on its validation overlay or background.
- Clearing a verdict gives every part its own colour and mark back.
- Connected so far: `QuizOptionView` (PopupQuiz, ConversationQuiz, HorizontalOptionQuiz, LO3 meeting minutes), `OptionCardBase` (Gantt quiz, SiteWalk, task-event and conversation option cards), `CategorizeDragCard`, `WBSActivityDragItem`. Other scripts with their own right/wrong colours can be connected the same way: ask `AnswerFeedback.TryGet(correct, out AnswerLook look)` and use its colours when it returns true.
- The theme is found on its own: it loads with the first styled control in a scene.

## State styles

For components whose look follows the game, not the pointer: a name tag that is *Locked*, *Selected*, *Correct*,
*Steer* or *Resolved*, a drop slot that goes *Live*, a clause that is *Struck*. Create one with **Create ▸ Design
System ▸ State Style**.

- Name each state exactly as the Figma variant does (`State=Steer` → `Steer`). Case doesn't matter.
- Each state picks theme tokens for **Fill**, **Border**, **Ink** (text) and **Accent** (the component's extra part:
  a grip, a tape strip, a connector), and an **Opacity**.
- The component keeps its own state machine and asks `style.Resolve("Steer", fallback)`. Any token left empty, and
  any state the style doesn't list, keeps the component's own colour, so assigning a half-finished style changes
  nothing it doesn't name.
- One style serves every instance of the component; a token edit restyles every style that uses it.
- **Write Design System Doc** lists each state style and its tokens under *Components ▸ State styles*.

## Layouts and Content Size Fitters

The system never adds a Content Size Fitter and never changes one by itself. It warns about the three setups that break layouts. Each warning appears on the button's **StyledButton** Inspector and in the setup report, with a one-click, undoable fix:

- **A fitter fighting a parent layout group** that already controls the same axis.
- **A fitter with nothing to measure** (no layout group or Layout Element), so the button shrinks to its sprite.
- **A fitter on a stretched axis**, so the fitter and the anchors fight.

## For developers

```
UITheme (colour + sprite tokens)  ← Figma design-token JSON
  ├─ UIButtonStyle   → StyledButton  on a Button
  ├─ UIToggleStyle   → StyledToggle  on a Toggle (On/Off look, checkmark)
  ├─ UISurfaceStyle  → StyledSurface on a panel Image (Look + layout padding)
  └─ any colour      → StyledColor   on any Graphic or text
TypographyScale (text roles)        → StyledText    on a TMP text
```

- **When styles apply:** in the editor, and once on `Awake` at runtime, never on re-enable. Code that recolours a button at runtime keeps its colour.
- **Toggles** update only their On/Off colours when the value changes, from the Toggle's own event. A new toggle style keeps each toggle's authored height.
- **New kinds:** add an `IComponentKind` to `ComponentKinds.All`, plus a `UIComponentStyle` subclass and a `StyledElement` subclass (`StyledSelectable` for clickable controls). A kind with `IsControl` false hides label, size and hover everywhere. The tools pick it up from the registry.
- **State tints** multiply over the fill on the Image. Code that resets a Button's `ColorBlock` (the tutorial nodes do) can't wipe its colour.
- **Prefab instances** in scenes follow their prefab. The tools never write overrides onto them.
- **Disabled labels:** add `ButtonDisabledTint` with only the label in its targets.
- **Headless setup:** `Unity.exe -batchmode -projectPath . -executeMethod DesignSystemAutomation.AutoSetupBatch`.
- **Big projects:** adopt one area at a time with **Advanced ▸ Settings ▸ Included Paths**.
- **The Advanced menu** holds the optional extras: settings, a preview prefab of every style, saving styles into prefab files, and refreshing recolour materials.

## Reusing in another project

This folder is a Unity package, `com.nus.design-system`, published at <https://github.com/rhnus/unity-design-system>. Add it to another project in **Package Manager ▸ + ▸ Add package from git URL…** with a release tag:

```
https://github.com/rhnus/unity-design-system.git#v0.3.0
```

Or copy the folder into the project's `Packages/` folder to edit it there. It is two asmdefs, `DesignSystem` and `DesignSystem.Editor`, that depend only on uGUI (with TextMeshPro) and, in the editor, Newtonsoft JSON; Package Manager installs both. The package's code is read-only in a project that installs it by git URL; the project's design system lives in its own assets.

Requirements: Unity 6 (built on 6000.3).

**Claude Code skills** for the package (style review, Figma sync, disabled buttons) ship in `Skills~/`, which Unity
ignores. It is a Claude Code plugin; [Skills~/README.md](Skills~/README.md) says how to install it in a project.

Then pick the start that fits:

- **A new project, or one with little UI:** **Tools ▸ Design System ▸ Set Up New Project…**. Pick a preset
  (*Neutral* or *Bold*), a brand colour, a font and a folder, and it creates the project's own theme (colours and
  Regular / Small / XSmall sizes), type scale, button styles (Primary, Secondary, Ghost, Primary Small) and a card panel
  style, plus settings and a preview prefab. New buttons get the Primary style from then on.
- **A project with UI already:** open **Tools ▸ Design System ▸ Open Design System** and **Scan**. It creates styles
  from how the existing buttons, toggles and panels look, and styles them with no visible change.

**Your own preset:** import the **Starter Kit** sample (Package Manager ▸ Design System ▸ Samples), copy a
`.preset.json` anywhere under `Assets/` and change it. The wizard lists it beside the package's presets. The sample's
README describes every key.

Making it yours:
1. **Design System Settings** (Advanced ▸ Settings) holds the project's choices: excluded folders, the generated
   document's title and folder, and the base class that makes a component a drag card (empty turns drag cards off).
   The tools create the theme, type scale and styles in the folder that holds the settings asset (`Assets/DesignSystem`
   until there is one).
2. **Features that stay off until used:** right/wrong answer colours (the theme's *Use for every graded answer*
   switch), drag cards (no matching base class, no drag cards), Figma sync (no `figma/sync-config.json`, no sync).
3. **Text roles are data.** The type scale asset lists its roles; **Add a role** at the bottom of its Inspector creates one
   of the project's own (starting from an existing role's look), and texts, styles, the theme and named sizes pick it
   from the same dropdown as the built-in ones. The built-in roles (the `TextRole` list in code) are always present, so
   code that names them, such as `TextRole.Button`, keeps working.

Driving it from scripts and AI agents: `DesignSystemCommands.Run(command, json)` takes a command name (`info`,
`write-doc`, `audit`, `prepare`, `propose-for`, `show-proposals`, `figma-compare`, `figma-confirm`, `figma-mark`,
`figma-unmark`, `figma-accept`, `starter-presets`, `starter-create`) and JSON arguments, and returns JSON. Code that can't reference the package assemblies,
such as Unity's AI command runner, calls it by name: `Type.GetType("DesignSystemCommands, DesignSystem.Editor")`.

## Releasing

The package is developed embedded in ADGBL (`ADGBL/Packages/com.nus.design-system`) and published to
<https://github.com/rhnus/unity-design-system>, whose history is this folder's. To release:

1. Bump `version` in `package.json` **and** `Skills~/.claude-plugin/plugin.json`, and date the CHANGELOG entry.
2. Commit in ADGBL, then from the ADGBL repository root (the folder above `ADGBL/`):
   ```
   git subtree push --prefix=ADGBL/Packages/com.nus.design-system https://github.com/rhnus/unity-design-system.git main
   ```
3. Tag the published commit `vX.Y.Z` in the Design System repository and push the tag.

Never commit to the Design System repository directly: a commit made there, and not in ADGBL, makes the next
`subtree push` fail. Every file in the package needs a `.meta`, because a package installed from git is read-only.
