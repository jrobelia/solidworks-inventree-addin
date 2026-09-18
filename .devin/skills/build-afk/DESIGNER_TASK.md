# /build-afk designer task template

Dispatch one per ticket during the up-front design pass (cap 2 concurrent). Fill every `{{slot}}`; the designer profile body already carries the declaration format and the flag rules — this template carries the ticket and its context.

`{{blocker_context}}` carries the outcomes of the tickets this one was blocked on plus any spec rulings confirmed at the batch gate that govern this ticket — write rulings as settled spec (a parent↔child contradiction or precedence call the maintainer already ruled), not as open questions. Write `None.` when empty.

```
run_subagent(
  profile: "build-designer",
  is_background: false,             # or background with cap 2 once tool grants exist
  title: "Seam for ticket {{ticket}}",
  task: <this file, slots filled>
)
```

---

You are designing the seam for ticket #{{ticket}}: {{ticket_title}}

## Ticket

The body and comments below are the spec — read all of it.

```
{{ticket_body_and_comments}}
```

{{blocker_context}}

## Read first

- `docs/agents/coding-standards.md` `## Module Design` — the declaration format and the design tests (deletion test, interface-as-test-surface, two-adapters, YAGNI) your proposal must satisfy.
- `.agents/skills/codebase-design/SKILL.md` — the deep-module vocabulary. When the seam sits on a dependency cluster, read `DEEPENING.md` beside it; when two candidate interfaces both look deep, `DESIGN-IT-TWICE.md`.

Skills cannot be invoked from inside a subagent — these are files to read, and the vocabulary travels with them.

## Return

Only the declaration — the orchestrator persists it verbatim to `seams/{{ticket}}.md`:

```
## Module Design
- Public interface: <everything a caller must know>
- Production adapter: <concrete implementation at the seam>
- Test adapter: <the Stub*/fake at the same seam>
- Deletion test: <the complexity that lands back on callers if deleted>
- Flag: routine | architectural — <one-line reason>

### Candidates (only when architectural with two equally-good seams)
<each candidate under the same declaration fields>
```
