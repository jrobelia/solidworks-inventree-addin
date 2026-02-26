# Workspace Ground Rules

## Communication
- Always use plain, jargon-free language. The primary user is a mechanical
  engineer, not a software engineer. Assume no prior programming knowledge
  unless demonstrated otherwise.
- When a software concept is unavoidable, explain it in one sentence using a
  physical or mechanical analogy before continuing.
- Never use unexplained acronyms (e.g. write "SOLID principles" not "SOLID").
- Prefer short sentences and bullet points over dense paragraphs.

## Code Quality (applied silently -- never lecture the user about these)
- Every piece of code written in this workspace must follow the design
  principles defined in [.github/instructions/design-principles.instructions.md](.github/instructions/design-principles.instructions.md).
- Apply those principles automatically. Do not explain them to the user unless
  they ask -- just write good code.
- Every function does one thing. Every module has one reason to change.
- Avoid duplication. Avoid complexity that isn't yet needed.
- Name things clearly enough that a comment is rarely necessary.

## Living Documentation
- `docs/roadmap.md` -- feature wish list (one line each). Check before planning.
- `docs/architecture.md` -- module map. Update when files are added/deleted.
- `docs/decisions.md` -- append-only log of non-obvious choices.

## Behaviour
- When the user describes a problem, do not immediately write code. First make
  sure the problem is fully understood.
- When uncertain about requirements, ask -- but batch questions; never ask one
  at a time in a back-and-forth loop.
- The **Orchestrator** agent manages the full engineering lifecycle:
  understand -> plan -> architect -> git branch -> failing tests -> build ->
  code review -> **manual verification in real environment** -> commit -> debrief.
  Use it rather than jumping straight to code.
- Manual verification is a hard gate -- do not commit until the user has
  confirmed the feature works correctly in the real environment.
- The **Debug** agent handles systematic fault-finding on existing code.
- All other pipeline agents are subagent-only -- never invoke them directly.
