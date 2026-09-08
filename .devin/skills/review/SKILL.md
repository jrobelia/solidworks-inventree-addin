---
name: review
description: "Two-axis code review (Standards + Spec) over a committed diff from a fixed REVIEW_BASE, followed by an adjudicated fix-and-reverify loop. Invoke when /build or /fix reaches its review step, or when asked to 'run /review' on a committed branch, PR, or diff against a spec."
---

# /review

The shared two-axis review seam. `/build` and `/fix` call it so the review-and-fix rules live in one place; it also runs standalone on any diff.

## Interface

Inputs the caller supplies:

- `REVIEW_BASE` — the git ref the diff is measured from (SHA, branch, or tag). Required.
- `SPEC_SOURCE` — what the diff is judged against: an issue number (`#N`), spec text, or a file path. Pass `none` only when no spec exists. Required.
- `AXES` — `both` (default) or `spec`. `spec` runs only the Spec axis; `/build`'s per-ticket check uses it.

Outputs the caller consumes:

- `REVIEW_STATUS` — `clean` (no findings), `resolved` (findings fixed and re-verified), `deferred` (proceed; YELLOW/GREEN items recorded), `escalated` (a RED too large to fix became a follow-up issue — stop), or `capped` (two-pass cap reached — stop and ask the user).
- `REVIEW_NOTES` — the markdown the caller pastes into the PR's `### Review notes` and `### Deferred and follow-up issues` sections.

### Acting on `REVIEW_STATUS`

Callers proceed on `clean`, `resolved`, or `deferred`; stop for the user on `escalated`; and ask the user on `capped`. `REVIEW_NOTES` must reach the PR body under `### Review notes` and `### Deferred and follow-up issues`.

The production adapters at this seam are the `review-standards` and `review-spec` subagent profiles in `.devin/agents/`. This skill owns adjudication, fixing, and re-verification — the profiles only report findings.

## Process

Do not move to the next step until the current step's condition holds.

1. **Resolve inputs.** `git rev-parse REVIEW_BASE` must resolve and `git diff REVIEW_BASE...HEAD` must be non-empty — a bad ref or empty diff fails here, not inside subagents. If `SPEC_SOURCE` is an issue number, fetch the body per `docs/agents/issue-tracker.md`; if it is a path, read the file.
2. **Optional lint sweep.** If the repo has a lint configuration (`.editorconfig`, `dotnet format`, StyleCop rules, `package.json` lint scripts, etc.), run it now. Fix or auto-fix any mechanical style findings. Do not dispatch reviewers for issues a tool can already find — re-run lint until it is clean or until any remaining lint issue is a real review item.
3. **Dispatch the axes.** Preferred: `run_subagent` with profiles `review-standards` and `review-spec` in parallel (`is_background=true`), passing `REVIEW_BASE`; pass the `SPEC_SOURCE` contents to the Spec axis as its `SPEC:` block. `AXES: spec` dispatches only `review-spec`; `SPEC_SOURCE: none` skips it and records "no spec available". Fallback: if `run_subagent` is unavailable or denied, run `subagent_general` in the foreground — read the profile file from `.devin/agents/` and use its full text as the task, appended with the same inputs. The Devin cloud child-session fallback is deliberately dropped; `build-afk` covers unattended review.
4. **Aggregate.** Collect the `## Standards` and `## Spec` blocks verbatim and keep the axes separate — one axis must never mask the other.
5. **Adjudicate every finding:**
   1. **Verify it against the code.** Subagent findings are opinions, not tasks; skip findings that are factually wrong or contradict the spec.
   2. **Classify within its axis:**
      - **RED** — a hard spec gap (Spec) or documented hard-standards violation (Standards). Fix it in-session only if it is mechanical and does not touch a public seam, module boundary, or behavior. Examples: assertion-style rewrites, one-line delegations to an existing ViewModel method, null-check additions, or local renames that match the spec. If the RED changes a public seam, a module's boundary, or any runtime behavior beyond the immediate fix, create a follow-up issue, link it as a blocking dependency on the PR, record it in `REVIEW_NOTES`, and return `escalated`.
      - **YELLOW** — a real quality or partial-spec issue. Propose a fix; ask the user when the rework is large or the trade-off is unclear. A YELLOW the user explicitly accepts is deferred — record it and the reason in `REVIEW_NOTES`. A YELLOW that is just "professionalize / make reusable / add monitoring" with no spec usage is Speculative Generality — record it, do not loop on it.
      - **GREEN** — style or cosmetic. Auto-fix if it is a trivial one-line change. Otherwise record it in `REVIEW_NOTES` and do **not** spend a re-review pass on it.
   3. **Re-run the build, test, and lint commands** from `docs/agents/coding-standards.md` `## Build & Test Commands` (and any project lint configuration) after every fix.
   4. **Re-review the changed areas** after every fix that touches code or docs — required even for small diffs. Re-dispatch the relevant axis against the same `REVIEW_BASE`. Cap the fix → test → re-review cycle at **two passes**; a finding still unresolved after two passes returns `capped` — stop and ask the user.
6. **Return** `REVIEW_STATUS` and `REVIEW_NOTES`. Notes must list each finding's disposition (fixed / deferred-with-reason / escalated-to-issue) so the PR body shows an audit trail.
