---
name: build
description: "Implement a spec or set of tickets with TDD and code-review, then open a draft PR for /qa."
disable-model-invocation: true
---

# Build

`/build` implements a parent spec or a set of tickets for the SolidWorks InvenTree Add-In, TDD-first, with code review, and produces a draft PR that `/qa` can pick up.

`grill-with-docs → to-spec → to-tickets → build → qa → triage`

## When to run

Run when the user says `/build`, "build this spec", or gives a parent issue/spec and child tickets. It replaces `/implement` for this repo.

## Guardrails

- Refuse to run on `main` or `master`.
- Refuse to overwrite an existing branch.
- Cap at 3–5 tickets per invocation. Ask the user to split larger batches.
- The target PR base is the branch `/build` was invoked from.

## Before implementation

1. Capture context:
   - `git branch --show-current` → `PARENT_BRANCH`.
   - `git remote get-url origin` → `GITHUB_REPO`.
   - `gh api user --jq .login` → `GITHUB_USER`.
   - Read `docs/agents/issue-tracker.md` and `docs/agents/triage-labels.md` for tracker conventions.
   - Read `CONTEXT.md` and `docs/agents/domain.md` for domain vocabulary.
   - Read `docs/agents/coding-standards.md` for build/test commands and standards.
2. Fetch each ticket with `gh issue view <number> --comments`.
3. Fetch the parent spec if it is a separate issue/PRD.
4. Propose seams and test slices in domain language; confirm with the user.
5. Build the branch name:
   - Single ticket: `build/issue-<number>`.
   - Batch: `build/spec-<parent-number>-<ticket-count>` (e.g. `build/spec-44-3`).
   - Check `git branch --list <name>` and increment the trailing `-N` suffix until the name is free.
6. Create and check out the branch from `PARENT_BRANCH`.

## Per-ticket TDD loop

For each ticket in dependency order:

1. Identify the public seams. Use domain terms from `CONTEXT.md`.
2. Write the failing test first (`red`) at the chosen seam.
3. Run the targeted test(s) and confirm they fail.
4. Write the minimal implementation to make them pass (`green`).
5. Run the targeted tests again and confirm they pass.
6. Run `dotnet build "Solidworks Inventree Add-In.sln"`.
7. Run `dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj"`.
8. Commit with a clear message referencing the issue.

One ticket, one commit. Do not batch commits.

## After all tickets

1. Run the full build and test commands again.
2. Run `/code-review` over the full diff against the commit before `/build` started (`git diff <pre-build-sha>...HEAD`).
3. Classify every finding:
   - **RED / BLOCKING** — spec gap, broken functionality, or hard standards violation. Fix, rerun `/code-review`, and rerun tests. Repeat up to two fix loops.
   - **YELLOW / MAJOR** — real code-quality or standards issue. Auto-fix if simple; stop and ask the user if it would require large rework.
   - **GREEN / MINOR** — style or cosmetic. Auto-fix if trivial; otherwise note under `### Review notes` in the PR body.
4. If a RED blocker is too large to safely fix, create a follow-up issue, label it `needs-triage`, link it as a blocking dependency on the PR, and stop without merging.

## Review diff size

If the diff exceeds ~500 changed lines, split `/code-review` into per-ticket or per-module passes and synthesize the findings. If a single pass still exceeds the budget, fall back to a main-session review.

## PR

1. Push the branch.
2. Create a **draft** PR to `PARENT_BRANCH`.
3. Body must include:
   - `Closes #<ticket>` lines for each child ticket.
   - `Closes #<parent>` if the batch completes the parent spec.
   - Acceptance criteria copied from the tickets.
   - Build and test commands run.
   - Changed GUI flows and edge cases.
   - `### Review notes` from `/code-review`.
   - `Run /qa on this branch.`
4. Do not merge. Hand off to `/qa`.
