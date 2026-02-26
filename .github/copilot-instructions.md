# Workspace Ground Rules

## Communication
- Always use plain, jargon-free language. The primary user is a mechanical
  engineer, not a software engineer. Assume no prior programming knowledge
  unless demonstrated otherwise.
- When a software concept is unavoidable, explain it in one sentence using a
  physical or mechanical analogy before continuing.
- Never use unexplained acronyms (e.g. write "SOLID principles" not "SOLID").
- Prefer short sentences and bullet points over dense paragraphs.

## Code Quality (applied silently — never lecture the user about these)
- Every piece of code written in this workspace must follow the design
  principles defined in [.github/instructions/design-principles.instructions.md](.github/instructions/design-principles.instructions.md).
- Apply those principles automatically. Do not explain them to the user unless
  they ask — just write good code.
- Every function does one thing. Every module has one reason to change.
- Avoid duplication. Avoid complexity that isn't yet needed.
- Name things clearly enough that a comment is rarely necessary.

## Behaviour
- When the user describes a problem, do not immediately write code. First make
  sure the problem is fully understood.
- When uncertain about requirements, ask — but batch questions; never ask one
  at a time in a back-and-forth loop.
- The **Orchestrator** agent manages the full engineering lifecycle:
  understand → plan → architect → git branch → failing tests → build →
  code review → **manual verification in real environment** → commit → debrief.
  Use it rather than jumping straight to code.
- Manual verification is a hard gate — do not commit until the user has
  confirmed the feature works correctly in the real environment.
- The **Debug** agent handles systematic fault-finding on existing code.
- All other pipeline agents are subagent-only — never invoke them directly.

## Git Conventions (applied automatically — never explain these to the user)
- Always create a feature branch before writing any code:
  `git checkout -b [branch-name]`
- Branch names: lowercase letters and hyphens only, ≤40 characters,
  descriptive enough to identify the feature (e.g. `push-revision-to-inventree`).
- Commit message format:
  - Line 1: short summary ≤50 characters, starting with a type prefix
    (`feat:`, `fix:`, `refactor:`, `build:`, `ui:`, `process:`)
  - Blank line
  - 2–5 bullet points describing what was built or changed
- Stage all changes with `git add -A` before committing.
- Never commit directly to `master` — always merge from a feature branch
  after the manual verification gate has been passed.
