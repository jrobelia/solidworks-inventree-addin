# Stage 6 — Build Instructions
<!-- Used by: Orchestrator (Stage 6 of the pipeline) -->
<!-- When to use: After the user has approved the failing test list (Stage 5 gate). -->
<!-- The orchestrator executes these instructions directly using its own tools. -->

Implement code until every failing test passes (GREEN), then clean it up (REFACTOR).
You do not write tests here — tests were written in Stage 5.

---

## Step 1 — Read before writing

Before touching any files:
- Read every test file produced in Stage 5.
- Understand exactly what each test expects: inputs, outputs, exceptions.
- Cross-reference with the approved architecture to confirm file locations
  and module responsibilities.

---

## Step 2 — GREEN: make every test pass

Work through tests in dependency order (lowest-level modules first).

For each module:
1. Write the minimum implementation that makes its tests pass — nothing more.
2. Run the full test suite with `dotnet test`.
3. If tests fail, read the failure output, fix only what is failing, re-run.
4. Move on only when all tests for that module pass.

Repeat until `dotnet test` shows 0 failures.

---

## Step 3 — REFACTOR: clean up without breaking anything

With all tests green, improve code quality — without changing behaviour.

Check against [design-principles.instructions.md](.github/instructions/design-principles.instructions.md):
- Extract duplicated logic into one shared function.
- Rename anything whose name does not clearly describe its purpose.
- Break up any function that does more than one thing.
- Remove dead code and unnecessary complexity.

Run the full test suite after every change to confirm nothing broke.

---

## Step 4 — Final check

Run `dotnet test` one last time. All tests must pass before proceeding.
Check for build warnings that indicate real problems (not pre-existing noise).

---

## Step 5 — Report to the user

Produce a plain-English build summary:

**Files created / changed:**
- `path/to/file.cs` — one sentence describing its role

**Tests passing:** X / Y

**Known limitations:** anything not yet handled that the user should know about.

---

## Design standards (applied silently, never explained to user)

- Each function does exactly one thing and is named for what it does.
- No logic is duplicated — shared behaviour lives in one place.
- Inputs are validated at the boundary; errors are raised immediately.
- No speculative features — only what the approved plan requires.
- Follow the approved architecture exactly; do not add files or responsibilities
  that were not in the design.
- If you discover a genuine conflict or impossibility, stop and explain it in
  plain English before proceeding.
