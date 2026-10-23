# Starter Kit

Presets a new project starts its design system from. Each one is a theme (colours and named sizes), a type scale,
the core button roles (Primary, Secondary, Ghost, and a Small variant of Primary) and a card panel style.

You don't need to import this sample to use them: **Tools ▸ Design System ▸ Set Up New Project…** lists them
straight from the package. Import it when you want a preset of your own:

1. Import **Starter Kit** from the Design System's page in Package Manager.
2. Copy a `.preset.json` file anywhere under `Assets/`, rename it, and change what you like: colours, sizes, the
   type scale, corner radius, the buttons.
3. Run **Set Up New Project…** and pick your preset. It's listed with the package's own presets.

The wizard creates the project's own assets in the folder you pick and never edits the package. After setup,
the theme, type scale and styles are ordinary project assets: change them in their Inspectors or on the Design
System window's Styles tab, as with any design system made by the tools.

## Preset format

| Key | What it sets |
|---|---|
| `kind` | Always `design-system-preset`, so the wizard knows the file is a preset. |
| `name`, `description` | Shown in the wizard. |
| `colors` | Colour tokens, name → `#RRGGBB` or `#RRGGBBAA`. Names are slash paths, e.g. `Brand/Primary`. `Brand/Primary` is the one the wizard's **Brand colour** replaces. |
| `sizes` | Named control sizes. `labelRole` (optional) gives labels at that size their own text role. The first size is the theme's button height. |
| `buttonLabelRole` | The text role every button label uses unless its size says otherwise. |
| `cornerRadius` | Corner radius in canvas pixels for every button and panel style. |
| `typography` | Per text role: `size`, and optionally `style` (`Bold`, `Italic`, …, or `Bold, Italic`). A name that isn't a built-in role adds a role of the project's own. |
| `buttons` | Button styles: `fill` and `label` colour token names, and optionally `size`. `variantOf` makes a variant that follows its parent and overrides only its size. |
| `surfaces` | Panel styles: `fill` and `padding`. |
| `answers` | The right / wrong colour tokens. They're filled in but left off, until **Use for every graded answer** is ticked in the theme. |
