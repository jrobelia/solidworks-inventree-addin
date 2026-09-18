---
name: domain-voice
description: "Run a domain-voice check on every user-facing message before sending it. Use whenever finalizing a reply, plan or proposal, commit message, ADR, doc, status update, or any prose the user will read for the SolidWorks InvenTree add-in, including answers about a change, feature, PR, commit, or design. Writes in the project's CONTEXT.md language and ASD-STE100 Simplified Technical English."
triggers: ["user", "model"]
---

# Domain voice

Before you send text the user will read, run the voice check. The reader is a domain expert judging your work, not a code reviewer. They need to understand what you propose, why, and what it means for their workflow, without opening the code — and your prose is the only evidence they get: the files you read and the diffs you made are invisible to them. They think in the project's terms and care about the design behind it. Write like the engineer who also wrote the maintenance manual: domain terms used exactly, sentences built to be read once.

## Voice check

1. **Load the domain language.** Read `CONTEXT.md`; if the repo has a `CONTEXT-MAP.md`, follow it to the right file. Use the preferred terms and the "avoid" list: one word per meaning, so once you name a thing the name stays fixed for the whole reply. If no domain doc exists, fall back to the generic watchlist below.
2. **Orient first.** Open with a little context before the detail: name the workflow the change touches (Part Sync, BOM Compare, Create Part) and what is different for the engineer now. Then the specifics. When the reply carries several independent facts, a short bullet list reads faster than a packed paragraph — the same bounds apply inside bullets.
3. **Write in STE.** ASD-STE100 Simplified Technical English for sentence mechanics: one idea per sentence, about 25 words or fewer, present tense, active voice, imperative when telling the engineer what to do.
4. **Pair identifiers with effects.** Code identifiers are fine when they are the natural name the user already uses, but pair each one with what it means for observable behavior and, when relevant, which seam, pattern, or coding-standard rule it serves. "`CanCreatePart` now checks that the validation service is present, so the Task Pane disables Create Part instead of silently doing nothing; the validation logic lives in a dedicated `ICreatePartValidationErrorService`, following the seam-and-adapter pattern the project uses for every Part Sync write."
5. **Cut AI tells.** Run the unslop red-flag audit. Use `references/unslop-patterns.md` when you need the full pattern list.
6. **Add soul.** STE is the floor, not the ceiling: react to facts, have an opinion, use "I" when it fits, acknowledge complexity, vary rhythm inside the length bound.
7. **Stop when** the opener orients the reader, every domain term matches CONTEXT.md, and each sentence is one idea in STE form. The test: a domain expert who never opens the code can say what you propose and why — reading it once, at speed.

## Implementation terms to reframe

If the repo has no domain glossary, avoid these in user-facing prose and replace them with the real concept or a plain description:

`class`, `method`, `function`, `interface`, `service`, `viewmodel`, `controller`, `dependency injection`, `event handler`, `async`, `await`, `null reference`, `exception`, `generic`, `collection`, `repository`, `database`.

Do not delete these from code snippets; only reframe the surrounding explanation.

## Unslop red flags

- **Cut:** puffery, name-dropping, `-ing` phrases without a source, promotional language, vague attributions, "Not just X, but Y", forced rule-of-three, synonym cycling, false ranges, em dashes, mid-sentence colons, bold-as-headers, title case, emojis, curly quotes, chatbot phrases, cutoff disclaimers, sycophancy, filler phrases, hedging, generic conclusions, abstract metaphor nouns, adverbs, passive voice, and fancy synonyms.
- **Prefer:** plain words, concrete numbers and mechanisms, short first.

See `references/unslop-patterns.md` for the full checklist and `references/examples.md` for before/after pairs.

## Scope

Apply this only to natural-language text the user will read. Leave code, file paths, commands, and technical identifiers unchanged. Documents written for agents — skills, AGENTS.md, rules files, and PR descriptions in this repo — belong to their own conventions (`writing-for-agents`, `docs/agents/pr-conventions.md`), not this voice.
