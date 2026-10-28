# Moving this repository to another GitHub account

For the day the repository moves, for example from a personal account to an enterprise organisation. Done this way,
nothing breaks: projects pinned to a tag keep installing, published history keeps its commit ids, and ADGBL keeps
publishing.

## What must never change

Projects and settings files refer to these names, not to the account:

| Name | Where it's set |
|---|---|
| package `com.nus.design-system` | `package.json` `name` |
| plugin `nus-design-system` | `Skills~/.claude-plugin/plugin.json` `name` |
| marketplace `unity-design-system` | `.claude-plugin/marketplace.json` `name` |
| release tags `vX.Y.Z` | the repository's tags |

Never delete or move a tag, never force-push `main`, and never delete the old repository while a project still
points at it.

## 1. Move the repository

**Preferred: GitHub's transfer** (repository Settings ▸ General ▸ Danger Zone ▸ **Transfer**, by an owner of both
accounts). It keeps history, tags, issues, pull requests and stars, and GitHub redirects the old URL for clone, fetch
and push, so every existing project keeps working. Don't create a new repository with the old name afterwards: that
ends the redirect.

**If a transfer isn't allowed** (some enterprise accounts accept no transfers from personal accounts), mirror it:

```
git clone --mirror https://github.com/<old-owner>/<repo>.git
cd <repo>.git
git push --mirror https://github.com/<new-owner>/<repo>.git
```

Then **archive** the old repository (Settings ▸ Archive), never delete it: archived, it stays readable, so pinned
projects keep installing until they move. Add a line to its README pointing at the new one.

## 2. Point the package at its new home

1. In `package.json`, change `repository.url`. `Tools~/publish.py` reads it, so this is the only change publishing
   needs, in ADGBL and in every clone.
2. Find the remaining mentions and update them: `grep -rn "<old-owner>/" .` (README, CONTRIBUTING, `Skills~/README.md`).
3. In each clone: `git remote set-url origin <new URL>`. ADGBL has no remote for it; step 1 is enough there.
4. Release a patch from the new home (`python Tools~/publish.py release X.Y.Z`), so the newest tag is on it.

Commit ids don't change in either route, so ADGBL's publish records stay valid and the next publish is as fast as
before.

## 3. Move each project (at its own pace)

Old URLs keep working through the redirect or the archive, so this can happen whenever a project next upgrades:

- **The package:** in `Packages/manifest.json`, change the URL's owner, keeping the tag:
  `"com.nus.design-system": "https://github.com/<new-owner>/unity-design-system.git#vX.Y.Z"`.
- **The skills:** the marketplace's name doesn't change, so `enabledPlugins` entries stay as they are. Re-point the
  marketplace at the new address: remove it and add it again (`/plugin` in Claude Code, or
  `claude plugin marketplace remove unity-design-system` then `claude plugin marketplace add <new-owner>/unity-design-system`).
  If the project's `.claude/settings.json` lists it under `extraKnownMarketplaces`, update the URL there too.

## Enterprise accounts

- **Private repositories** need credentials to install: Package Manager and Claude Code both use the machine's git
  credentials (Git Credential Manager on Windows). Check a teammate can `git clone` the new URL before projects move.
- **SAML single sign-on**: each person's token or SSH key must be authorised for the organisation.
- **Branch protection and `CODEOWNERS`** don't come across in a mirror; set them again on the new repository.
