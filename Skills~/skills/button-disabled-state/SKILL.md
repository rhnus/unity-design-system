---
name: button-disabled-state
description: Make a gated button LOOK disabled as well as be disabled, using the Design System's disabled look. Use when a button is dead but still shows its live colour, when asked to grey out or tint a button, when a button should unlock only after the player completes a step's requirements, or when auditing a screen's buttons for consistent disabled states.
---

# Gating a button, and making it look gated

Two halves, and the second is the one that gets forgotten. A button that is correctly `interactable = false` and
still renders full-strength brand colour reads as broken, not as locked.

## Gate with `interactable`, never SetActive

If the design shows the button **greyed**, drive `interactable`. Hiding it with `SetActive` throws away the
affordance: the player cannot see what they are working towards, and a button that appears only once the work is done
never taught them the work was needed.

Reserve `SetActive` for a button whose job is finished or that has swapped for another (Submit becoming Continue).
Those are not gates.

Recompute the gate wherever the screen is repainted, from the same state the screen reads:

```csharp
if (submitButton != null) submitButton.interactable = answers.AllFilled;
```

Never cache "is it unlocked" in a second bool. Two sources for one gate is how they end up disagreeing.

## Make it look disabled: the style's Disabled look

A button with a `StyledButton` gets its disabled look from its **button style**:

- **Disabled look** on (`UIButtonStyle.UsesDisabledLook`): in play, a button that is not interactable, or sits under
  a Canvas Group that isn't, swaps to the style's **Disabled Fill** and **Disabled Label Color** and swaps back when
  it can be used. Nothing else is re-applied, so a size or font a script set is kept.
- **Disabled look** off: the style's **Disabled Tint** multiplies the button's colour through Unity's ColorTint.

The README's **Disabled buttons** section is the rule: primary buttons go light grey. Set it once on each primary
style, never per button.

### Multiply cannot lighten

The trap behind every "the disabled button is brown" report. A tint multiplies, so it can only darken: an orange
primary times `(0.78, 0.78, 0.78, 0.5)` is a faded orange, never grey. A light grey disabled state needs the
Disabled look. Use the Disabled Tint only where the live button is already light or neutral.

## Buttons without a style

A button with no `StyledButton` gets no disabled look. In order of preference:

1. **Give it the right style** through the `style-review` skill, which checks the layout impact before anything
   changes. A button pointing at a style that no longer exists has the same problem: point it at the role's style.
2. If it can't take a style, use the project's own tint component, if it has one, in a **replace** mode with the
   theme's disabled colours. Two things to check in such a component:
   - Unity's ColorTint only tints `Selectable.targetGraphic`. A button built as a transparent hit area with its
     background and label as children has nothing visible there, so a tint must find the graphics that actually draw.
   - A component that captures colours once and restores them fights any script that recolours the button later.
   - It should stand aside on a button whose style has a Disabled look, or the two fight over the same colours.

## Which buttons need it

Only ones a screen actually disables. A disabled look on a button that is always live changes nothing, but a gate
added to it later then works with no extra step, so a primary style carries it for every button.

## Audit a folder's buttons

Find every button and its style straight from the YAML, without opening anything:

```python
import io, os, re
BTN = '4e29b1a8efbd4b44bb3f3716e73f07ff'          # UnityEngine.UI.Button
def guid_of(path): return re.search(r'^guid: (\w{32})', io.open(path + '.meta', encoding='utf-8').read(), re.M).group(1)
styled = guid_of(next(os.path.join(r, f) for r, _, fs in os.walk('.') for f in fs if f == 'StyledButton.cs'))
for root, _, files in os.walk('Assets/UI'):                 # the folder to audit
    for f in files:
        if not f.endswith('.prefab'): continue
        s = io.open(os.path.join(root, f), encoding='utf-8', errors='replace').read()
        docs = re.split(r'^--- !u!(\d+) &(\d+)(?: stripped)?\s*$', s, flags=re.M)
        objs = {docs[i+1]: (docs[i], docs[i+2]) for i in range(1, len(docs) - 2, 3)}
        buttons, style = set(), {}
        for _, (c, b) in objs.items():
            m = re.search(r'm_GameObject: \{fileID: (\d+)\}', b)
            if c != '114' or not m: continue
            if BTN in b: buttons.add(m.group(1))
            if styled in b:
                st = re.search(r'^  style: \{fileID: \d+(?:, guid: (\w+))?', b, re.M)
                style[m.group(1)] = st.group(1) if st and st.group(1) else 'NO STYLE'
        for g in buttons:
            # A GameObject of a nested prefab instance is STRIPPED here and has no m_Name.
            m = re.search(r'm_Name: (.*)', objs[g][1]) if g in objs else None
            print(f'{f:40}{(m.group(1).strip() if m else "(nested)"):32}{style.get(g, "no StyledButton")}')
```

Resolve a style GUID to its asset with `AssetDatabase.GUIDToAssetPath`. A GUID that resolves to nothing is a missing
style.

## Verify

Compiling proves nothing here, and the editor never shows the disabled look, so a prefab is never saved grey. Enter
Play mode, reach the screen with the requirement unmet, and check the button is grey; complete the requirement and
check it returns to its live colour.
