---
name: style-review
description: Find buttons, toggles, dropdowns, option cards and drag cards that don't follow the Design System, judge each one (use an existing style, make a special variant, or leave it as a unique one-off), and hand the proposals — with before/after captures and their layout impact — to a designer and developer to approve in Unity's Style Proposals window. Use when asked to find weird-looking or inconsistent buttons or UI controls, to suggest styles for a prefab, screen or folder, to tidy a screen's buttons, or to check UI consistency before release. Never applies anything itself.
---

# Style review

The Design System can scan controls and rank styles by colour distance, but it can't tell a Close button from a
chat Send button, or a deliberate one-off from drift. This skill adds that judgement. The flow is:

1. **Audit** (code): scan the scope, capture every control that no style fits *before*, and *after* each candidate style. Measure whether the control or its neighbours move.
2. **Judge** (you): decide each item and say why.
3. **Prepare** (code): render your decisions.
4. **Review** (people): approve or reject each one in **Tools ▸ Design System ▸ Style Proposals**.
5. **Apply** (people): only their **Apply approved** button edits prefabs and scenes.

You never approve, never apply, and never edit a prefab, scene or style asset directly.

## 0. Preconditions

Run this first. Every step refuses to run in Play mode, because the captures would wake the game's scripts:

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) =>
        result.Log("playing {0}, compiling {1}", UnityEditor.EditorApplication.isPlaying, UnityEditor.EditorApplication.isCompiling);
}
```

If it's playing, ask the user to stop Play mode. Don't stop it yourself, because they may be mid-test.

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

## 1. Pick the scope

Use what the user names:

| They say | Scope |
|---|---|
| a prefab, scene or folder | its asset path, e.g. `Assets/Prefabs/HUD.prefab`, `Assets/UI/Menus` |
| "this", "what I have open" | `open` (the open prefab stage, else the open scenes) |
| "these", "the selected ones" | `selection` |
| "the whole game", "everything" | `project` (slow; say so, and keep `maxItems` at 40) |

If they name nothing, use `open`.

## 2. Audit

Command `audit`, arguments `{ "scope": "<SCOPE>", "maxItems": 25 }`. It returns the run `folder`, `notReviewed`
(looks past the limit), `notCaptured`, and a short line per item with its candidates.

Warnings from the project's own scripts are noise from the temporary copies the audit captures. Real errors mention
`StyleProposals` or `CaptureRig`.

The run folder (`Artifacts/StyleReview/<timestamp>/`, git-ignored) holds `review.json` and the PNGs.

**One item is one look.** Its `others` are the controls that look exactly the same, and your decision applies to all of them. The capture shows the first one, outlined in pink, inside its row or column. Other panels of the screen are hidden, so it's easy to see.

## 3. Look before you judge

Read `review.json` with a short Python summary, not whole. For each item, read `<id>-before.png` and every
candidate image (`<id>-<style>.png`). Fields that matter:

- `name`, `where`: what the control is for. Its label is in the capture.
- `kind`: Button, Toggle · Card, DragCard · Categorize…
- `sameLook`, `others`: how many controls share this exact look, and where.
- `fill`, `label`, `sprite`, `size`: what it looks like now.
- `siblingStyles`: styles already used in the same row. These are a strong hint.
- `parentLayout`: row/column/grid, and whether the layout controls child size.
- `hiddenByDefault`: it sits on a panel shown later, such as a popup or settings.
- `candidates[]`: the closest styles, with `confidence` (Safe/Check/None), `rank` (lower is closer), `keepsStates` (hover and press colours survive), `summary`, `image` and `impact.detail`.

## 4. Judge each item

Pick one decision, then write a `reason` of at most 25 words. Say what's off and why this fixes it, e.g.
*"Close is a primary action in an off-brand dark orange; Button_Primary at its own size changes nothing around it."*

| `decision` | Use when | Also set |
|---|---|---|
| `apply` | An existing style is the right *role* for it | `decisionStyle` (style name); `keepOwnSize: true` to share the look but keep its size |
| `custom` | It belongs to a role but has a special size or shape that recurs (`sameLook` ≥ 2), so a variant should exist | `custom`: `baseStyle`, `name`, and what it keeps: `keepSize`, `keepLabelFont`, `height`, `labelSize`, `square` |
| `unique` | A designer would agree it's deliberately one-of-a-kind: game-world markers, tab headers with underline indicators, art buttons, a single themed control | — |
| `skip` | You can't tell, no style of its kind exists yet (common for drag cards), or it's debug/dev UI | — |

Judge in this order:

1. **What is it for?** Read the label and the hierarchy path. Confirm, Submit, Close and Next are primary actions. Cancel and Back are secondary. A tab, chip, marker or list row isn't an ordinary button, even if a button style is the nearest colour.
2. **Pick by role, never by size alone.** A style whose name carries a use case (`_Send`, `_SimulationSpeed`, `_EventOption`) is for that use case only. Never pick it because its height happens to match. Prefer the role style plus `keepOwnSize`.
3. **Match the row.** If `siblingStyles` shows the row already uses a style of the same role, use that one.
4. **Protect the layout.** Prefer a proposal whose impact is "layout unchanged". If the right style would resize the control, use `keepOwnSize: true`. Only let the layout change when the size *is* the problem, e.g. one 50px button in a row of 56px ones. Then say so in the reason, because a person must tick "accept the layout change".
5. **Small drift means apply; big difference means look harder.** Confidence Check with rank under ~0.5 and the same role is drift: apply. Confidence None means it really looks different. Decide whether it's a special variant (`custom`), a deliberate one-off (`unique`), or something you can't judge (`skip`).
6. **Unique is a design decision, not a shortcut.** It stops the Design System proposing styles for that control for good. Use it only when the capture shows a look no role should have. When in doubt, use `skip` and say what a designer should decide.
7. **Name custom styles by the README pattern:** `Button_Role[_Size][_Icon]_UseCase`, e.g. `Button_Secondary_Marker`. Its `baseStyle` is the role style. Default to `keepSize: true` and `keepLabelFont: true`, so only the colours and shape converge.

## 5. Write the decisions

Edit `review.json` in place with Python. Keep every other field. Set `decision`, `decisionStyle`, `keepOwnSize`,
`custom`, `reason`. Leave `review`, `reviewer`, `acceptLayoutChange` and `applied` alone, because they belong to the people reviewing.

## 6. Prepare, then check your own proposals

Command `prepare`, arguments `{ "folder": "<FOLDER>" }`. Each item comes back with its `decision`, its proposal
image and `layout` (the layout impact).

Read every `<id>-proposal.png` next to its `<id>-before.png`. If a proposal looks wrong, fix the decision and prepare again. Wrong means a clipped label, text the same colour as its fill, a control that grew into its neighbour, or a layout change you didn't intend.

## 7. Hand over

Open the window on the run:

Command `show-proposals`, arguments `{ "folder": "<FOLDER>" }`.

Then tell the user, briefly:

- how many looks you judged, and how many controls they cover in total;
- counts per decision;
- every proposal that **changes the layout**, and why you think it should;
- every **unique** call and every **custom** style you proposed, since those create lasting decisions;
- what you skipped and what a designer needs to decide there;
- if `notReviewed` > 0, that more looks remain, and to run again after this batch is applied.

They approve or reject per item in the window. A layout-changing proposal also needs its acceptance box ticked. **Apply approved** edits and saves the prefabs and scenes, so remind them to commit first.
