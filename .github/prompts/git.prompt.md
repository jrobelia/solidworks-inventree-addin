# Git Conventions

Use these rules whenever creating a branch or committing in this workspace.

## Branch names
- Lowercase letters and hyphens only
- 40 characters maximum
- Descriptive enough to identify the feature (e.g. `push-revision-to-inventree`)

## Creating a branch
```
git checkout -b [branch-name]
```
Always branch before writing any code. Never work directly on `master`.

## Committing
```
git add -A
git commit -m "[type]: [short summary]

- [what was built or changed]
- [what was tested]"
```

### Type prefixes
| Prefix | Use for |
|---|---|
| `feat:` | New feature |
| `fix:` | Bug fix |
| `refactor:` | Code restructure, no behaviour change |
| `build:` | Build system, packaging, CI |
| `ui:` | Visual or layout change only |
| `process:` | Pipeline, instructions, or workflow change |

### Commit message rules
- Line 1: type prefix + summary, 50 characters maximum
- Blank line
- 2-5 bullet points describing what was built or changed

## Merging to master
Only merge after the manual verification gate (Stage 8) has been passed.
```
git checkout master
git merge [branch-name] --no-ff -m "merge: [feature description]"
```
