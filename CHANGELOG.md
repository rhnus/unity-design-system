# Changelog

## 0.1.0 — 2026-10-23

First release as a package, moved out of ADGBL's `Assets/Scripts/UI/DesignSystem`.

- Theme tokens (colours, sprites, named sizes), type scale, styles and variants for buttons, toggles, panels and drag cards.
- Text roles are data: a project adds its own in the type scale; the built-in roles stay for code that names them.
- Project settings (excluded folders, document title and folder, drag card base class) live in the Design System Settings
  asset; the tools create assets next to it.
- Scan and setup, Check (including text fit), Tidy Up (merge and organise), Style Proposals, Unique Design.
- Generated design system document and Figma sync (three-way compare, component map, adaptation marks).
- Menus are under **Tools ▸ Design System**.
