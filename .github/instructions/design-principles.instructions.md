---
applyTo: "**"
---

# Software Design Principles Reference

Shared reference for pipeline agents. Not user-facing. Apply these silently
and automatically in every architectural and implementation decision.

---

## Code Structure

**Single Responsibility (SRP)** — every function and module does one thing
and has one reason to change. Check: if describing it requires the word "and",
split it.

**Open/Closed (OCP)** — add capability by adding new code, not by modifying
working code. Extend through parameters or composition, not edits.

**Liskov Substitution (LSP)** — any subtype must work wherever its parent is
used, without surprising callers. If a subtype breaks the parent's contract,
the hierarchy is wrong.

**Interface Segregation (ISP)** — prefer several small, focused interfaces
over one large general-purpose one. Modules should not depend on things they
don't use.

**Dependency Inversion (DIP)** — high-level logic depends on abstractions,
not on concrete details. This is what makes code testable and swappable.

---

## Code Quality

**Don't Repeat Yourself (DRY)** — every piece of logic has one authoritative
home. Duplication means a future change requires finding every copy.

**Keep It Simple (KISS)** — the simplest correct solution is the right one.
Complexity without a concrete justification is debt.

**You Aren't Gonna Need It (YAGNI)** — build only what the current requirement
demands. Speculative abstractions almost always guess the future wrong.

**Separation of Concerns** — keep input/output, validation, logic, and
configuration in separate units. Mixing them makes each harder to change and test.

**Fail Fast** — validate inputs at the boundary. Raise clear, specific errors
immediately rather than letting bad data propagate silently into core logic.

**Composition Over Inheritance** — build behaviour by combining small components
rather than deep class hierarchies. Composition is easier to test and swap.

**Explicit Over Implicit** — no magic numbers, no hidden global state, no side
effects disguised as queries. Behaviour should be readable from the code itself.

---

## Workflow Methodology

**Test-Driven Development (RED → GREEN → REFACTOR)**
- RED: write a failing test that defines the expected behaviour before any
  implementation exists. Confirm it fails for the right reason.
- GREEN: write the minimum code needed to make the test pass. Nothing more.
- REFACTOR: clean up the passing code without changing what it does.
  Run tests after every change to confirm nothing broke.

**Verification Before Completion** — do not declare something done by
assertion. Prove it: run the full test suite, run the program against a real
input, check for warnings. Evidence, not confidence.

**Systematic Debugging (Reproduce → Root Cause → Fix Minimally → Verify)**
- Reproduce the failure reliably before investigating.
- Trace the root cause before touching any code.
- Apply the smallest fix that resolves the root cause.
- Verify the fix works and that no other tests broke.
