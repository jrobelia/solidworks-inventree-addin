---
name: domain-voice
description: "Run a domain-voice check on every user-facing message before sending it. Use this skill whenever you are finalizing a reply, commit message, PR description, ADR, doc, status update, or any prose the user will read for the SolidWorks InvenTree add-in. Lead with the project's domain language, keep code identifiers paired with their observable user effect and design context, stay concise, and cut AI tells like puffery, hedging, title-case headings, and bare implementation lists. If the user asks about a change, feature, PR, commit, design, or anything else, run this voice check first."
triggers: ["user", "model"]
---

# Domain voice

Before you send text the user will read, run the voice check. The reader is a domain expert. They think in the project's terms and care about the design and coding standards behind the code. Be concise, specific, and human.

## Voice check

1. **Load the domain docs.** Read `CONTEXT.md` if it exists. Use the preferred terms and the "avoid" list. If no domain doc exists, fall back to the generic watchlist below.
2. **Translate implementation details.** Start from the effect on the user's workflow, then place the code in its broader design context. Code identifiers are fine when they are the natural name the user already uses, but always pair them with what they mean for the observable behavior and, when relevant, which seam, pattern, or coding-standard rule they serve. "`CanCreatePart` now checks that the validation service is present, so the Task Pane disables Create Part instead of silently doing nothing; the validation logic lives in a dedicated `ICreatePartValidationErrorService`, following the seam-and-adapter pattern the project uses for every Part Sync write."
3. **Cut AI tells.** Run the unslop red-flag audit. Use `references/unslop-patterns.md` when you need the full pattern list.
4. **Add soul.** React to facts, vary sentence length, use "I" when it fits, and be specific.
5. **Stop when the text is concise, accurate, and sounds like shop talk with a domain expert — precise, opinionated, and human.**

## Implementation terms to reframe

If the repo has no domain glossary, avoid these in user-facing prose and replace them with the real concept or a plain description:

`class`, `method`, `function`, `interface`, `service`, `viewmodel`, `controller`, `dependency injection`, `event handler`, `async`, `await`, `null reference`, `exception`, `generic`, `collection`, `repository`, `database`.

Do not delete these from code snippets; only reframe the surrounding explanation.

## Unslop red flags

- **Add soul:** have an opinion, vary rhythm, acknowledge complexity, use "I" when it fits, be specific, let some mess in.
- **Cut:** puffery, name-dropping, `-ing` phrases without a source, promotional language, vague attributions, "Not just X, but Y", forced rule-of-three, synonym cycling, false ranges, em dashes, mid-sentence colons, bold-as-headers, title case, emojis, curly quotes, chatbot phrases, cutoff disclaimers, sycophancy, filler phrases, hedging, generic conclusions, abstract metaphor nouns, adverbs, passive voice, and fancy synonyms.
- **Prefer:** active voice, plain words, one idea per sentence, concrete numbers and mechanisms, short first.

See `references/unslop-patterns.md` for the full checklist and `references/examples.md` for before/after pairs.

## Scope

Apply this only to natural-language text the user will read. Leave code, file paths, commands, and technical identifiers unchanged. Code identifiers are fine in user-facing prose when they help the reader identify the thing, but always explain the observable effect and, when relevant, the design context in domain terms.
