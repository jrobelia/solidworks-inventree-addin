# Plan -- Implementation Plan

You have a confirmed Problem Brief. Produce a step-by-step implementation
plan in plain English. Do not write any code.

## What to do

1. Check the existing codebase for anything relevant to this problem so the
   plan builds on what exists rather than duplicating it.

2. Break the problem into distinct responsibilities -- what are the separate
   components and what does each one do?

3. Produce the plan in this format:

---
**Implementation Plan**

**Summary:** [one sentence describing what will be built]

**Steps:**
1. [Step name] -- [what and why, 2-4 sentences, no code]
2. ...

**Trade-offs and decisions:**
- [Decision made] -- [reason]

**Risks and open questions:**
- [Risk or open question, if any]
---

4. Present it to the user and ask: "Does this plan make sense? Would you
   like to change anything before we move on?"

5. Once confirmed, output the finalised plan and continue the pipeline.

## Rules
- No code.
- Apply the design principles from `.github/instructions/design-principles.instructions.md`
  silently -- don't explain them to the user.
- Keep each step to 2-4 sentences.
