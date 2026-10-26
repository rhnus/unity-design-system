# Changelog

## Unreleased

- **State styles** (`UIStateStyle`, Create ▸ Design System ▸ State Style) for components whose look follows game
  state rather than pointer input: a name tag that is Locked, Selected, Correct, Steer or Resolved. Each state, named
  as in the Figma variant, gives a fill, border, ink and accent token and an opacity; the component keeps its own
  state machine and calls `Resolve(state, fallback)`. Anything the style leaves empty keeps the component's colour.
- The generated design system doc lists every state style under **Components ▸ State styles**.

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
