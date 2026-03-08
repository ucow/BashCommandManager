---
name: github-releasing
description: Use when user asks to publish a new GitHub Release, create a release with artifacts, generate version numbers and release notes, or tag a new version
---

# GitHub Releasing

## Overview

Publishing a GitHub Release requires strict step-by-step execution. One missed step (unpushed code, wrong version, missing artifact) creates broken releases that confuse users and waste time.

**Core principle**: Execute steps in exact order. Verify each step before proceeding. Never skip verification.

## When to Use

- User says "publish a release", "create a new release", "release version X"
- User asks to "tag a new version"
- User wants to "build and release" the application
- Need to generate release notes from commits

## Release Workflow

Execute these steps **in exact order**:

### Step 1: Push All Code

```bash
# Check for uncommitted changes
git status

# If uncommitted changes exist, commit them
git add .
git commit -m "chore: prepare for release"

# Push all commits
git push origin HEAD
```

**STOP if push fails.** Fix issues before continuing.

### Step 2: Get Previous Release Info

```bash
# Get latest tag (version)
git describe --tags --abbrev=0

# If no tags exist, version starts at v0.1.0

# Get commits since last release
git log $(git describe --tags --abbrev=0)..HEAD --pretty=format:"%s" --no-merges
```

### Step 3: Generate Version Number

Parse commits since last release and apply **Semantic Versioning**:

| Commit Type | Version Bump | Example |
|-------------|--------------|---------|
| `BREAKING CHANGE:` or `feat!:` | MAJOR (x.y.z → x+1.0.0) | v1.2.3 → v2.0.0 |
| `feat:` | MINOR (x.y.z → x.y+1.0) | v1.2.3 → v1.3.0 |
| `fix:` | PATCH (x.y.z → x.y.z+1) | v1.2.3 → v1.2.4 |
| `chore:`, `docs:`, `style:`, `refactor:` | PATCH (if no feat/fix) | v1.2.3 → v1.2.4 |

**Rules**:
- Start with `v` prefix
- Always 3 numbers (major.minor.patch)
- If multiple commit types, bump the highest level

### Step 4: Generate Release Notes

Group commits by type:

```markdown
## What's New

### Features
- feat: description here
- feat: another feature

### Bug Fixes
- fix: bug fix description

### Chores & Documentation
- chore: maintenance task
- docs: documentation update
```

Include:
- All commits since last release
- Breaking changes section (if any BREAKING CHANGE commits)
- Installation instructions (if applicable)

### Step 5: Build and Package

**REQUIRED**: Use `packaging-release` Skill for this step.

This project has a dedicated `packaging-release` Skill that handles:
- Building the application (dotnet build/publish)
- Creating installers with InnoSetup
- Organizing output artifacts

Invoke the `packaging-release` Skill and follow its instructions to generate the release artifacts.

**Verify artifacts exist** before proceeding:
- Check output directory exists (e.g., `Packaging/InnoSetup/Output/`)
- Verify installer files are present (Full and Lite versions)
- Confirm file sizes are reasonable (> 1MB)

### Step 6: Create Tag

```bash
# Create annotated tag
git tag -a vX.Y.Z -m "Release vX.Y.Z"

# Push tag to remote
git push origin vX.Y.Z
```

### Step 7: Create GitHub Release

Use GitHub CLI (preferred) or API:

```bash
# Create release with artifacts
gh release create vX.Y.Z \
  --title "vX.Y.Z" \
  --notes-file release-notes.md \
  ./artifacts/*.exe \
  ./artifacts/*.zip
```

Or manually via GitHub web:
1. Go to Releases → Draft new release
2. Choose tag: vX.Y.Z
3. Title: vX.Y.Z
4. Paste release notes
5. Upload all artifacts
6. Publish release

### Step 8: Verify Release

Checklist:
- [ ] Release page shows correct version
- [ ] Release notes are complete
- [ ] All artifacts uploaded successfully
- [ ] Tag points to correct commit
- [ ] Can download and install artifact

## Common Mistakes

| Mistake | Why It Happens | Fix |
|---------|---------------|-----|
| Creating release before pushing code | "I'll push after" | **ALWAYS push first** - tag must be on pushed commit |
| Wrong version number | Guessing instead of parsing commits | Always check commits since last release |
| Missing commits in release notes | Only checking recent commits | Use `git log` range from last tag |
| Forgetting to create tag | Focused on GitHub UI | Create tag via CLI before GitHub release |
| Artifacts not uploaded | Release created before build complete | Verify build artifacts exist before creating release |
| Version in app doesn't match tag | Hardcoded version in code | Check version files before building |

## Red Flags - STOP and Fix

- "I'll push the code after creating the release" → Push first
- "The version should be X.Y.Z" (without checking previous) → Check previous release first
- "Let me create the release, then build" → Build first, verify artifacts
- "I'll tag it later" → Tag is required for release
- Current branch is not main/master → Switch to main branch first

## Quick Reference

```bash
# Check what will be released
git log $(git describe --tags --abbrev=0)..HEAD --oneline

# See current version
git describe --tags --abbrev=0

# Dry-run version bump logic
# Count commit types since last release
git log $(git describe --tags --abbrev=0)..HEAD --pretty=format:"%s" | grep -c "^feat:"
git log $(git describe --tags --abbrev=0)..HEAD --pretty=format:"%s" | grep -c "^fix:"
git log $(git describe --tags --abbrev=0)..HEAD --pretty=format:"%s" | grep -c "BREAKING CHANGE"

# Create release with gh CLI
gh release create vX.Y.Z --generate-notes
```

## Version Check Examples

**Example 1**: Previous release v1.2.0, commits since then:
- `feat: add new feature`
- `fix: bug fix`
→ New version: **v1.3.0** (minor bump due to feat)

**Example 2**: Previous release v1.2.0, commits since then:
- `fix: critical bug`
- `docs: update README`
→ New version: **v1.2.1** (patch bump)

**Example 3**: Previous release v1.2.0, commits since then:
- `feat!: breaking API change`
- `fix: some bug`
→ New version: **v2.0.0** (major bump due to breaking change)
