# Figma → Unity pipeline

**Status:** component sync built; frame builder and reviewed token import still to build (see Build order).
Sections marked *Exists* describe code in the repo today. Owner: rhnus. How to run it: the `figma-sync` skill.

## Goal

Designers keep designing in Figma. Developers implement in Unity with the exact tokens, component styles and
reference screenshots, **without a Figma licence**. The game's Design System — tuned from the Figma mockups and
then adjusted to suit the game — is never overwritten by a sync.

## Principles

1. **One sync, many readers.** One person with a Full Figma seat runs the sync. It writes a snapshot into the
   repo. Everyone else reads the snapshot and never calls Figma.
2. **Compare first, change never.** A sync writes the snapshot and a report. It does not edit the theme, a style
   or a prefab.
3. **Game adaptations are decisions, not drift.** A value the game changed on purpose is marked and kept until a
   person says otherwise.
4. **People approve every visible change,** through the review tools that already exist.
5. **No new cost.** Uses the existing Full seat, a free Figma plugin as fallback, and git.

## Roles

| Role | Does | Figma licence |
|---|---|---|
| Designer | Designs with the agreed names (see Naming). Asks for a sync after library or screen changes. Reviews `DESIGN_SYSTEM.md` with their AI agent and decides each difference. | Existing seat |
| Sync owner | Runs `figma-sync`, reviews the report, opens a PR with the snapshot. | Existing Full seat (Pro team) |
| Developer | Reads the snapshot: tokens, component map, frame specs and screenshots. Approves proposals. | None |

Keep design files in a paid Figma team. Figma limits API and MCP calls on Starter-plan files and
for View/Collab seats to a handful per month; see
<https://developers.figma.com/docs/figma-mcp-server/rate-limits-access/>.

## The snapshot

*Exists.* Committed under `docs/design-system/figma/`, outside `Assets/` so Unity doesn't import it:

```
docs/design-system/
  DESIGN_SYSTEM.md          generated handback document (see Handback)
  tokens.json               the theme's colours, named sizes and type scale, for seeding Figma
  figma/
    sync-config.json        Figma file key and the section / component-set node ids to read
    components.json         Figma now: each component's variants with size, corner, fill, stroke, label font and colour
    images/                 a PNG of every variant, named by variant id (5560:1771 is 5560-1771.png)
    baseline/components.json  Figma at the last accepted sync
    component-map.json      Figma component → Unity style; resting, hover and disabled variants; confirmed or suggested
    rules.json              the platform rules agreed with the designer: the only reasons the game may differ
    adaptations.json        values the game keeps, each citing a rule from rules.json, with who marked it
```

`the skill's scripts/figma_sync.py` writes `components.json` from Figma's file API with the sync owner's token; it is sorted, so a
re-sync with no Figma change produces no diff. `FigmaSync.Run()` (Unity, `Editor/FigmaSync.cs`) compares and
regenerates the document. **Accepting** a reviewed sync copies `components.json` to `baseline/`.

Developers review in Unity with **Tools ▸ Design System ▸ Figma ▸ Review…** (`Editor/FigmaReviewWindow.cs`): Figma and
Unity as swatches row by row, and buttons to confirm a mapping, mark a component not used, keep a value under an
agreed rule, or open Style Proposals. Each panel shows the Figma picture of the compared variants and, under **Used
in the game**, every prefab control or scene object using the style, with Ping, Open prefab and a capture of the
control as saved (`Editor/StyleUses.cs`; scenes are read as text, never opened). Opening it writes nothing; each
decision writes only `component-map.json` or `adaptations.json`.

Compared fields per mapped style: fill, corner radius, height, label colour, label size, label font (family and
weight). A **state style** (map entry `"kind": "state"`, for components such as the G3 name tag whose look follows
game state) is compared state by state: each Unity state against the Figma variant with the same `State`, on fill,
border (stroke) and ink (label colour), as `Selected.fill` and so on. A state only one side has is listed, not
compared. Only a variant's own fill and stroke and its first text are read, so a coloured child layer such as the
sticky note's tape isn't compared yet. Frame specs and screenshots come later, with the frame builder.

## How a sync decides (three-way compare)

For every token and every mapped style property, the sync looks at three values:

- **Figma now** — from this sync.
- **Baseline** — Figma's value at the last sync.
- **Unity now** — the theme or style asset in the repo.

**Figma wins.** The game may differ from Figma only where it cites a platform rule the designer agreed once
(`rules.json`). Every other difference is work for a developer, and nothing waits on the designer.

| Figma since last sync | Unity vs baseline | Result | What happens |
|---|---|---|---|
| unchanged | same | in sync | nothing |
| unchanged | different, no agreed rule | **changed in Unity: follow Figma** | a developer brings the game back to Figma, or cites a rule |
| unchanged | different, agreed rule | **adapted by platform rule** | kept; listed with its rule |
| changed | same | **design update to apply** | a developer applies it through Style Proposals |
| changed | different, no agreed rule | **both changed: follow Figma** | a developer applies Figma's new value, or cites a rule |
| changed | agreed rule | **adapted by rule, Figma moved** | reported; the rule still wins while it covers the new value |
| new in Figma | — | **addition** | proposed new token or style |
| removed in Figma | — | **removal** | reported only; nothing is deleted in Unity |

### The first sync

There is no baseline yet. The first sync writes the snapshot, changes nothing in Unity, and lists every difference no
agreed rule covers as **follow Figma**. A developer either changes the game or cites a rule; once settled, the
snapshot is accepted as the baseline, and after that only new Figma changes show up.

## Platform rules

`rules.json` holds the reasons the game may differ from Figma: limits of the platform, not taste. Each has an `id`,
what it says, the `fields` it may cover (empty covers any), and `agreedBy` and `date`. They are agreed with the
designer once, in a short session, and changed only by the designer or the team together. A rule without
`agreedBy` is a proposal for that session: it shows in the doc and covers nothing.

## Adaptation marks

`adaptations.json` records one line per kept value: style, field, the **rule** it cites, the Unity value, the
Figma value at the time, an optional note, and who marked it. A mark counts only while its rule exists, is agreed
and covers the field (`FigmaSync.CheckMark`); otherwise the value is listed as *follow Figma* with the reason the
mark doesn't count. `figma-mark` refuses a rule that wouldn't count. Removing a line hands the value back to Figma.

*Exists, related:* a style can be **Locked** (`UIComponentStyle.Locked`,
`Packages/com.nus.design-system/Runtime/UIComponentStyle.cs:126`); a variant keeps its own **overrides** while
following its parent (`StyleVariants.IsOverridden`, `Editor/StyleVariants.cs:36`). Adaptation marks follow the same
idea at token and field level.

## How changes land

| Change | Route | Notes |
|---|---|---|
| Token value | Theme diff review (to build) | Until then, don't run the direct import on a Figma export: it overwrites matching colours on the spot (`DesignTokenJson.Import`, `Editor/DesignTokenJson.cs:56`) |
| Style field (fill, radius, height, text role) | **Style Proposals** window | Before/after captures; layout changes need an extra tick (`StyleProposals.Ready`, `Editor/StyleProposals.cs:637`) |
| New component | Proposed new style or variant | Named by the Organise scheme `Button_Role_Size_Icon_UseCase` |
| New screen | Frame builder makes a new prefab from `spec.json` | Existing prefabs are never rebuilt |
| Existing screen | Screenshot comparison and text-fit report only | Fixes go through normal review |

A style change reaches every prefab that uses the style, so the review shows each affected screen before Apply.

## Handback: the design system document

After every sync, and on demand, the pipeline writes **`docs/design-system/DESIGN_SYSTEM.md`** with
`tokens.json` beside it. It is the single record of **what the game ships and why it differs from Figma**. Figma
stays the record of design intent. The file is generated from the Unity assets, so it can't drift; nobody edits it
by hand.

*Exists (first version):* `DesignSystemDoc` (`Packages/com.nus.design-system/Editor/DesignSystemDoc.cs`), menu
**Tools ▸ Design System ▸ Write Design System Doc**. Until the first sync its Figma columns show the name
each item should have in Figma, and every status reads "not compared".

| Section | Content |
|---|---|
| For the designer's AI agent | A brief any agent with a Figma connector can follow: compare, report, don't edit the library |
| Naming scheme | How a Unity name maps to a Figma variable, text style or component variant |
| Theme | Colours, named sizes, type scale, shapes, right/wrong colours: Unity value, Figma name, Figma value, status |
| Components | Every style: the Figma component it should match, the style it follows, its own settings, values, how many controls use it, status |
| Platform rules | The agreed rules, and proposals waiting for the next rules session |
| Adapted for the game | Kept values with the rule they cite, and any mark that doesn't count |
| Since the last baseline | What changed on either side since the sync before |
| At a glance (first, at the top) | Every compared value in three groups: **matches**, **differs by platform rule** (with the rule), **to do** (what a developer changes, each with its action) |

### The designer's side

The designer's only job is designing in Figma. They are never asked to review or approve the game.

1. **Once:** agree the platform rules in a short session. After that, applying them is developer work.
2. **Optionally:** read the doc, or give it to an AI agent with the Figma connector. The brief at the top tells it to
   compare and report only; our Pro plan has no Figma branching, so it never edits library components, and draft
   fixes go on a separate **Unity review** page.
3. **Rarely:** a developer raises a Figma value that looks unworkable in the game and that no rule covers.

### What a developer does with each difference

- *Follow Figma*: change the style through Style Proposals; the next sync sees them match.
- *Cite a rule*: `figma-mark` with an agreed rule id; the value is kept and listed with its rule.
- *No rule fits, and Figma looks wrong in the game*: ask the designer. Their answer is a Figma edit or a new rule.

```
designer edits Figma ──sync──▶ snapshot ──▶ DESIGN_SYSTEM.md (To do)
        ▲                                         │
        │ rare: "no rule fits"                    ▼
        └──────────────────────── developer: follow Figma, or cite an agreed rule
                                                  │
                                                  └─▶ next sync: in sync, or adapted by rule
```

## Naming (designer ↔ Unity)

- Component sets use variant properties that match Unity style names: `Button` with Role=Primary, Size=Small,
  Icon=true ↔ `Button_Primary_Small_Icon`.
- Text styles are named after the type-scale roles (`Button`, `Body`, `BodySmall`, …).
- Variables are named after theme tokens (`Brand/Primary`, `Text/OnPrimary`).

Anything that doesn't match is listed as unmapped; nothing is guessed.

## Seeding Figma from Unity

*Exists.* `docs/design-system/tokens.json` holds the theme's colours, named sizes (`Size/Small` = 40px) and type scale
(`Typography/Button` = Outfit 600, 20px) as W3C design tokens. A designer imports it with the free **Tokens Studio**
plugin to create Figma variables and text styles with the game's names. Once components use those variables, later
syncs can compare named tokens as well as component values. The Unity importer reads colours only, so sizes and type
never flow back in through it.

## Fallback without the API

A designer exports tokens with the free **Tokens Studio** plugin and frames as PNG from Figma, into the snapshot
folder. The rest of the pipeline is unchanged.

## Safety

- No Figma token in the repo; the sync owner keeps theirs in the `FIGMA_API_KEY` user environment variable.
  `.mcp.json` only references it.
- Read-only token scopes (File content: Read, Dev resources: Read). The sync never writes to Figma.
- The snapshot arrives as a PR. Rolling back a sync is reverting its commit.
