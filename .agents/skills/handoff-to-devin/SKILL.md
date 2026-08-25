---
name: handoff-to-devin
description: Compact the current conversation into a handoff summary and start a new Devin Cloud session to continue the work.
disable-model-invocation: true
triggers: ["user"]
---

# Handoff to Devin

`/handoff-to-devin` compacts the current conversation into a handoff summary and starts a new Devin Cloud session to continue the work.

## Usage

`/handoff-to-devin <what the next session should focus on>`

## Steps

1. **Summarize the conversation.** Write a concise handoff document covering:
   - The goal of the work.
   - What has been done.
   - Open questions or blockers.
   - The next concrete step.
2. **Include a "suggested skills" section.** Name any repo skills the next agent should invoke (e.g. `build`, `qa`, `solidworks-inventree-testing`).
3. **Reference existing artifacts.** Do not duplicate content already captured in specs, plans, ADRs, issues, commits, diffs, or PRs. Point to them by path or URL.
4. **Redact secrets.** Remove API keys, passwords, PII, or session-specific tokens from the summary.
5. **Capture the current branch.** Add a `Working branch:` line near the top of the handoff summary. If the working branch is a local feature branch, push it to origin before starting the handoff so the child session can fetch it.
6. **Start the Devin Cloud session.** Call `devin_session_create` with a single session:
   - `prompt`: the handoff summary. Include an explicit first instruction for the child session: `Start by running: git fetch origin && git checkout <working-branch>`.
   - `repos`: `["jrobelia/solidworks-inventree-addin"]` if not already inherited.
   - `platform`: `"windows"` when the work touches the SolidWorks add-in; otherwise inherit from the parent session.
   - `tags`: include `["handoff"]`.
   - `title`: a short, descriptive title based on the user's argument.
7. **Return the new session URL** to the user.

## Notes

- This skill creates a new **cloud** Devin session. Devin Local sessions inside Devin Desktop cannot be spawned programmatically from a skill. To continue in Devin Local, paste the handoff summary into a new Devin Desktop session, or invoke `/handoff-to-devin` from within Devin Local (the local session is already open; only the cloud counterpart will be created).
- If the conversation is already in a cloud Devin session, `devin_session_create` starts a child session on a fresh VM.
