# Contributing to the Design System

The source is <https://github.com/rhnus/unity-design-system>. ADGBL embeds the same package and stays in step through
`Tools~/publish.py` (see the README's **Releasing and contributing**).

## Never edit an installed copy

In a project that installs the package by git URL, its files sit in `Library/PackageCache` and the skills in Claude
Code's plugin cache. Both are overwritten on the next update. Change the repository instead.

## Send a change

1. **Branch or fork** <https://github.com/rhnus/unity-design-system>.
2. **Try it in your project** before opening a pull request. Point the project at your clone instead of the tag, in
   `Packages/manifest.json` (don't commit this line):
   ```json
   "com.nus.design-system": "file:C:/path/to/your/clone"
   ```
   For a skill change, add your clone as a marketplace and install from it:
   `claude plugin marketplace add C:/path/to/your/clone`.
3. **Describe the change** under `## Unreleased` at the top of `CHANGELOG.md`.
4. **Open a pull request** to `main`. Keep it to one change; say what it looks like before and after when it changes
   how a control looks.

Rules the package keeps (the README has the detail): it never references a project's code; every file outside a
folder ending in `~` has a `.meta`; styles change through Style Proposals, never by hand in a project.

## Urgent fix (maintainers)

For a fix that can't wait for the normal review, from ADGBL or a clone of the repository:

```
# fix it, describe it under "## Unreleased" in CHANGELOG.md, commit
python Tools~/publish.py release 0.3.1
```

`release` pulls first, bumps `package.json` and the plugin's version, dates the CHANGELOG entry, commits only those
files, publishes and tags `v0.3.1`. Then each project moves to it:

- **The package:** change `#v0.3.0` to `#v0.3.1` in `Packages/manifest.json`.
- **The skills:** update the plugin (`/plugin` in Claude Code, or `claude plugin update nus-design-system@unity-design-system`).

**A fix to a skill's wording only** doesn't need a release: commit it and `python Tools~/publish.py push`. Projects
get it at their next plugin update. A skill change that needs new package code waits for the release that ships it.

## Versions

`MAJOR.MINOR.PATCH`. **Patch** for fixes. **Minor** for new styles, fields or commands that existing projects can
ignore. **Major** when a project has to change something to upgrade (a renamed field, a removed style), with the
steps in the CHANGELOG entry.
