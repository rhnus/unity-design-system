# Design System skills for Claude Code

Skills that only make sense in a project using this package. They ship with it, so they're always the version that
matches the package's code. Unity ignores this folder (its name ends in `~`), so it adds nothing to builds.

| Skill | What it does |
|---|---|
| `style-review` | Finds buttons, toggles, option cards and drag cards that don't follow the Design System, judges each, and hands before/after proposals to people in **Style Proposals**. Never applies anything. |
| `figma-sync` | Fetches the Figma file into `docs/design-system/figma/`, compares it with the Design System three ways, and regenerates the design system doc. Never edits a style. Needs Python 3 and a Figma token. |
| `button-disabled-state` | Gates a button with `interactable` and makes it look gated through the style's **Disabled look**. |

The folder is a Claude Code plugin (`.claude-plugin/plugin.json` plus `skills/`), so it installs either way below.

## Install as a plugin (recommended)

The skills then update with the package, and appear as `/nus-design-system:style-review` and so on.

The package's repository is also a marketplace (`.claude-plugin/marketplace.json` at the package root). In the
project that uses the package, run once:

```
claude plugin marketplace add rhnus/unity-design-system
claude plugin install nus-design-system@unity-design-system --scope project
```

`--scope project` writes the plugin into the project's `.claude/settings.json`. Commit that file; each teammate then
runs the `install` line once.

## Or copy the skills into the project

Copy each folder under `skills/` to the project's `.claude/skills/`. Simple, but the copies don't update with the
package: copy them again after upgrading it.

## Requirements

- The Unity MCP server (`Unity_RunCommand`): the skills drive the package through `DesignSystemCommands.Run`.
- `figma-sync` only: Python 3 on the path, and `FIGMA_API_KEY` set by the sync owner. Its fetcher's tests run with
  `python -m pytest skills/figma-sync/scripts/test_figma_sync.py`.
