# RALPH

Domain glossary for the RALPH skill. Defines the terms specific to RALPH's autonomous issue implementation workflow.

## Language

**AFK agent**:
An autonomous implementation agent that works through `ready-for-agent` issues without human supervision between issues. RALPH is the Windows-compatible AFK agent. Sandcastle is the Linux/Docker AFK agent. Both consume the same MPS pipeline output.
_Avoid_: "bot", "AI agent" (too generic), "Sandcastle" (use only when referring to the Linux/Docker path specifically)

**MPS pipeline**:
The end-to-end workflow from planning to implementation: `to-spec` → `to-tickets` → `triage` (human-in-loop MPS skills) → AFK agent (RALPH or Sandcastle).
_Avoid_: "workflow", "process"

**Implement subagent**:
A fresh Devin subagent dispatched by the RALPH orchestrator to implement a single issue using TDD. Receives the issue body, parent PRD, relevant file snippets, and implement-prompt.md instructions. Produces exactly one commit per issue.
_Avoid_: "coding agent", "implementer"

**Review subagent**:
A fresh Devin subagent dispatched by the RALPH orchestrator after the implement subagent commits. Reads the full issue diff (from pre-implement SHA to HEAD), the issue body, and coding-standards.md. Makes fixes directly on the branch if issues are found, then commits.
_Avoid_: "reviewer", "code review agent"

**Pre-implement SHA**:
The git commit hash captured immediately before the implement subagent runs (`git rev-parse HEAD`). Passed to every review subagent call for the same issue so the reviewer always diffs against the correct base — stable across multiple review loop iterations regardless of how many review commits stack on top.
_Avoid_: "base SHA", "start SHA"

**Review loop**:
The iteration of review subagent calls that follows each implement subagent commit. Repeats on `STATUS: REVISED` up to a maximum of 3 iterations. Terminates on `STATUS: CLEAN`. If the maximum is reached without a clean pass, the issue is flagged `needs-info` and RALPH moves to the next issue.
_Avoid_: "review cycle", "feedback loop"

**Coding standards**:
A project-specific file at `docs/agents/coding-standards.md` in each project repo. Defines the rules the review subagent enforces. Bootstrapped from coding-standards.template.md when first running RALPH in a new project and confirmed by the user before any implementation begins.
_Avoid_: "style guide" (too narrow), "linting rules" (too specific)

## Relationships

- The **MPS pipeline** ends with an **AFK agent**
- The RALPH orchestrator dispatches one **implement subagent** per issue, then enters a **review loop** of **review subagent** calls
- The **pre-implement SHA** is captured once per issue and passed to every **review subagent** in the **review loop**
- **Coding standards** live in the project repo; the **review subagent** reads them on each call
