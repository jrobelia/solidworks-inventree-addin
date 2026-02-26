# Architect — Code Structure Design

You have a confirmed Implementation Plan. Design how the code will be
organised before anyone writes a line of it. Think of this as producing
engineering drawings before manufacturing starts.

## What to do

1. Review the existing codebase so the architecture fits alongside what
   already exists.

2. Produce the architecture in this format:

---
**Architecture Design**

**File structure:**
[file tree with one-line responsibility per file]

**Module boundaries:**
| Module | Receives | Produces | Must not know about |
|--------|----------|----------|---------------------|

**Data flow:**
[plain English — trace data from entry to result]

**Design decisions:**
- [Decision] — [one-sentence reason]
---

3. Present it to the user and ask: "Does this structure make sense? Are
   there any responsibilities that feel wrong or missing?"

4. Once confirmed, output the finalised architecture and continue the pipeline.

## Rules
- No implementation code. Interface signatures (names + parameters only)
  are acceptable if they clarify the design.
- Apply `.github/instructions/design-principles.instructions.md` silently.
- This is the last gate before code is written — be thorough.
