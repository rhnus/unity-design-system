---
name: figma-sync
description: Sync the Figma design file into the repo as a reviewed snapshot and compare it with the game's Design System, without changing the game. Fetches the configured Figma component sets with the sync owner's token, runs the three-way compare (Figma now, last accepted sync, Unity now), regenerates docs/design-system/DESIGN_SYSTEM.md, and walks the first-sync triage. Use when asked to sync Figma, pull the latest design, check Unity against Figma, refresh the design system doc for designers, mark a value as adapted for the game, or accept a Figma sync as the new baseline.
---

# Figma sync

The rules this follows are in [pipeline.md](pipeline.md), next to this file; read its "How a sync decides" table once. In short:
**a sync never edits the theme, a style or a prefab.** It writes a snapshot and a report. **Figma wins:** the game
may differ only where it cites a platform rule the designer agreed (`rules.json`); every other difference is a
developer's to-do. Never ask the designer to review or approve game values.

Files, all under `docs/design-system/figma/` (committed, outside `Assets/`):

| File | Written by | Purpose |
|---|---|---|
| `sync-config.json` | people | Figma file key and the section or component-set node ids to read |
| `components.json` | `scripts/figma_sync.py` | Figma now: each component's variants with size, corner, fill, stroke, label |
| `images/<variant id>.png` | `scripts/figma_sync.py` | what each variant looks like, for people without a Figma seat |
| `baseline/components.json` | accept step | Figma at the last accepted sync |
| `component-map.json` | sync suggests, people confirm | Figma component → Unity style, and which variant is resting / hover / disabled |
| `rules.json` | designer and team, agreed once | platform rules: the only reasons the game may differ from Figma |
| `adaptations.json` | developers (via this skill) | values kept, each citing an agreed rule from `rules.json` |

## 0. Preconditions

- **Token.** Only the sync owner runs this. Check `FIGMA_API_KEY` is set and valid without printing it:

  ```powershell
  $t = [Environment]::GetEnvironmentVariable('FIGMA_API_KEY','User'); $key = (Get-Content docs/design-system/figma/sync-config.json | ConvertFrom-Json).fileKey; if (-not $t) { 'not set' } else { (Invoke-RestMethod "https://api.figma.com/v1/files/$key`?depth=1" -Headers @{ 'X-Figma-Token' = $t }).name }
  ```

  It reads the file's name because the token only has the File content scope; `/v1/me` answers 403 for it. Never list
  environment variable names or values: a token pasted into the wrong place shows up there.

  If it isn't set, tell the user how to create one (Figma ▸ Settings ▸ Security, **File content: Read**) and to set it
  themselves with `[Environment]::SetEnvironmentVariable('FIGMA_API_KEY','<token>','User')`. Never ask for the token
  in chat and never write it to a file.
- **Unity** is open and not in Play mode (the compare runs in the editor).
- Keep the Figma file in a paid team: Starter-plan files hit Figma's monthly call cap.
- **First sync in a project:** create `docs/design-system/figma/sync-config.json` with the Figma `fileKey` and the
  `nodes` to read (`{ "fileKey": "…", "nodes": [{ "id": "5559:7193" }] }`). The other files are written by the steps below.

The Design System is a package (`com.nus.design-system`), and `Unity_RunCommand` can't reference package
code, so every step calls its one agent entry point by name. Use this snippet, changing only the command and its JSON
arguments; it logs JSON back, with `"error"` set if the command failed:

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var run = System.Type.GetType("DesignSystemCommands, DesignSystem.Editor").GetMethod("Run");
        result.Log((string)run.Invoke(null, new object[] { "<COMMAND>", @"<JSON ARGS>" }));
    }
}
```

In the verbatim string, write JSON quotes doubled: `@"{ ""scope"": ""open"" }"`.

## 1. Fetch

The fetcher reads the token from the user environment. A process started before the token was set won't see it, so
pass it in for this call:

```powershell
$env:FIGMA_API_KEY = [Environment]::GetEnvironmentVariable('FIGMA_API_KEY','User'); python "${CLAUDE_SKILL_DIR}/scripts/figma_sync.py" fetch
```

It also saves a PNG of every variant under `figma/images/` (one more API call, then plain downloads), which the
Figma Review window shows beside the game's controls. `fetch --no-images` skips them; `images` alone refreshes them for
the current snapshot without rewriting it. If either reports 403, 404 or 429, relay its hint; don't retry in a loop.

To add Figma sections, put their node ids in `sync-config.json` (from "Copy link to selection": `node-id=5559-7193`
is `5559:7193`).

## 2. Compare

Command `figma-compare`, arguments `{}`. It writes suggestions for unmapped components into `component-map.json`
(unconfirmed), regenerates `DESIGN_SYSTEM.md`, and returns the file and version, `hasBaseline`, a `summary`, every
compared row (style, field, Figma now, baseline, Unity, result) and the unmapped components with suggestions.

For a developer in Unity, point them at **Tools ▸ Design System ▸ Figma ▸ Review…**: the same result as swatches,
with buttons for every decision below. To share the result outside Unity, point people at the regenerated doc's
**At a glance** section: every compared value sits in
one of three groups, **matches**, **differs by platform rule** (with the rule) and **to do** (what a developer
changes). A row's `markProblem` says why a mark doesn't count.

## 3. Confirm the map

For each suggested entry, check it makes sense: the Figma component's description, its name, and the Unity style's
uses. A suggestion only means "same fill, closest otherwise", and its note lists what matched. When you and the user
agree, confirm it with command `figma-confirm`, arguments `{ "figma": "<Figma component name or id>" }` (add
`"style": "<Unity style>"` to map it to a different style). For a component the game doesn't use, command
`figma-unmap`, arguments `{ "figma": "<name or id>" }`: it is neither compared nor suggested again. A suggestion with `"kind": "state"` maps the component to a
state style (`State_G3_*`): its note lists the states both sides share, and the compare's `stateGaps` lists the rest.
A state style in `unmappedStateStyles` has no Figma component: report it as Unity only, don't guess a mapping. Don't delete a map entry by hand: the next sync suggests it again;
use `figma-unmap` instead.

## 4. Settle the differences (developer work)

Every row that isn't *in sync* or *adapted by platform rule* follows Figma. Present them to the user grouped by
style; for each the only question is **does an agreed rule in `rules.json` cover it?**

- *Yes* → mark it with the rule: command `figma-mark`, arguments
  `{ "style": "Button_Primary", "field": "height", "rule": "touch-target", "reason": "optional note", "by": "<your name>" }`.
  It refuses a rule that is missing, not agreed, or doesn't cover the field, and writes nothing.
- *No* → leave it unmarked. It stays on the to-do list until someone changes the style through **Style Proposals**
  (never edit a style directly from this skill).
- *No rule fits, and Figma's value looks unworkable in the game* → that's the only case to raise with the designer.

Never add `agreedBy` to a rule yourself, and never write a rule to make a row go away: rules are agreed with the
designer. A new rule goes in `rules.json` without `agreedBy`, as a proposal for the next rules session.

Command `figma-unmark`, arguments `{ "style": "...", "field": "..." }`, removes a mark.

## 5. Accept the baseline

Only after the user has reviewed the snapshot diff and settled the differences:

Command `figma-accept`, arguments `{}`. It copies `components.json` to `baseline/components.json` and compares again,
so the doc reflects the new baseline. From now on, only new Figma changes appear.

## 6. Hand over

Tell the user, briefly:

- the Figma file and version synced, and how many components and variants;
- counts per result (in sync, follow Figma, adapted by rule, design updates, both changed);
- design updates and differences to fix by name — these are the developers' to-do;
- marks that don't count, and proposed rules waiting for the rules session;
- unmapped components worth mapping;
- that `docs/design-system/DESIGN_SYSTEM.md` is ready for the designer's AI agent (it opens with the agent's brief).

List the files to commit (everything under `docs/design-system/`). Don't commit unless asked; don't create the
Azure DevOps review task unless asked.

## Seeding Figma from Unity

`docs/design-system/tokens.json` (written with the doc) holds the theme's colours, named sizes and type scale as W3C
design tokens. A designer can import it into Figma with the free **Tokens Studio** plugin to create variables and text
styles with the game's names. Once Figma components use those variables, later syncs compare named tokens.
