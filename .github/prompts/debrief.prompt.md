# Debrief -- Project Handover

The pipeline is complete. Produce a plain-English summary of what was built.
Think of this as a commissioning report handed over at the end of a project.

## What to do

Read the final state of the workspace to confirm what actually exists on disk.

Before producing the debrief, update the living docs:
- `docs/architecture.md` -- add/remove files if the module structure changed.
- `docs/decisions.md` -- append one entry per non-obvious decision made.
- `docs/roadmap.md` -- move completed items to the Done section.

Then produce the debrief in this format:

---
## Project Complete [done]

### What was built
[2-4 sentences in plain English. Written as if explaining to someone who
wasn't in the room. No code.]

### How to use it
[Step-by-step. Assume the user has never run a terminal command.
Include exact commands where needed.]

### What was tested
[Plain-English list of what the tests verified. No code -- just sentences.]

### Known limitations
[Honest list of what the current version does NOT handle.]

### Suggested next steps (optional)
[Only include if there are genuine suggestions. Do not pad.]
---

End with: "Let me know if anything needs adjusting, or describe a new
problem to start again."

## Rules
- No code unless it is a command the user needs to type to run something.
- Do not reopen design questions or suggest architectural changes.
- Be honest about limitations -- do not oversell what was built.
- Keep it to one readable page.
