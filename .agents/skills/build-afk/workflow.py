"""Dynamic workflow for unattended /build-afk Cloud builds.

This script is copied into a per-run directory together with
CHILD_PROMPT.md, WPF_HARNESS.md, any reviewer profiles, and a PLAN.json file.
The orchestrating Devin session writes PLAN.json and then calls run_workflow on
this script.
"""

import asyncio
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

PLAN_FILE = Path(__file__).parent / "PLAN.json"
PROMPT_FILE = Path(__file__).parent / "CHILD_PROMPT.md"
RESULTS_FILE = Path(__file__).parent / "RESULTS.json"
RUN_DIR = Path(__file__).parent

# Statuses that mean the child produced reviewable work.
SUCCESS_STATUSES = ("COMPLETE", "COMPLETE_WITH_CONCERNS")

# What kind of intervention unblocks a BLOCKED ticket.
BLOCKED_KINDS = ("context", "capability", "size", "ambiguity")

CHILD_SCHEMA = {
    "type": "object",
    "properties": {
        "status": {"type": "string", "enum": ["COMPLETE", "COMPLETE_WITH_CONCERNS", "BLOCKED"]},
        "branch": {"type": "string"},
        "pr_number": {"type": ["integer", "null"]},
        "pr_url": {"type": ["string", "null"]},
        "pre_build_sha": {"type": ["string", "null"]},
        "worktree_path": {"type": ["string", "null"]},
        "test_summary": {"type": "string"},
        "review_summary": {"type": "string"},
        "screenshot_paths": {
            "type": "array",
            "items": {"type": "string"},
        },
        "concerns": {
            "type": "array",
            "items": {"type": "string"},
        },
        "blocked_kind": {"type": "string", "enum": list(BLOCKED_KINDS)},
        "reason": {"type": "string"},
    },
    "required": ["status", "branch", "reason"],
}

REVIEW_SCHEMA = {
    "type": "object",
    "properties": {"report": {"type": "string"}},
    "required": ["report"],
}

ADJUDICATION_SCHEMA = {
    "type": "object",
    "properties": {
        "status": {"type": "string", "enum": ["PROCEED", "FIX", "BLOCKED"]},
        "findings": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "axis": {"type": "string", "enum": ["Standards", "Spec"]},
                    "text": {"type": "string"},
                    "classification": {"type": "string", "enum": ["auto-fix", "ignore", "BLOCKED"]},
                    "reason": {"type": "string"},
                },
                "required": ["axis", "text", "classification", "reason"],
            },
        },
        "fix_instructions": {"type": "string"},
        "reason": {"type": "string"},
    },
    "required": ["status", "findings", "fix_instructions", "reason"],
}

STACK_SCHEMA = {
    "type": "object",
    "properties": {
        "status": {"type": "string", "enum": ["COMPLETE", "SKIPPED"]},
        "stack_url": {"type": ["string", "null"]},
        "reason": {"type": "string"},
    },
    "required": ["status", "stack_url", "reason"],
}

HARD_BUG_SIGNALS = [
    "intermittent",
    "flaky",
    "sporadic",
    "non-deterministic",
    "race",
    "deadlock",
    "cross-seam",
    "cross seam",
    "no deterministic repro",
    "not deterministic",
    "root cause unknown",
    "root-cause",
    "performance regression",
    "performance issue",
    "slow",
    "hang",
    "hangs",
    "timeout",
]

STANDARDS_REVIEWER_PROMPT = """You are the Standards axis of a two-axis /build-afk review for {{REPO}}.

Set `workdir` to `{{WORKTREE}}` for every `exec` call and `Read` relative paths from that worktree.

Run:
- `git diff {{PRE_BUILD_SHA}}...HEAD`
- `git log {{PRE_BUILD_SHA}}..HEAD --oneline`

Read `docs/agents/coding-standards.md` from the worktree.

Apply the repo's documented coding standards first, then the Fowler smell baseline:
- **Mysterious Name** — a function, variable, or type whose name doesn't reveal what it does or holds.
- **Duplicated Code** — the same logic shape appears in more than one hunk or file.
- **Feature Envy** — a method that reaches into another object more than its own.
- **Data Clumps** — the same few fields or params keep travelling together.
- **Primitive Obsession** — a primitive or string standing in for a domain concept.
- **Repeated Switches** — the same switch/if-cascade on the same type recurs.
- **Shotgun Surgery** — one logical change forces scattered edits across many files.
- **Divergent Change** — one file or module is edited for several unrelated reasons.
- **Speculative Generality** — abstraction, parameters, or hooks added for needs the spec doesn't have.
- **Message Chains** — long a.b().c().d() navigation the caller shouldn't depend on.
- **Middle Man** — a class or function that mostly just delegates onward.
- **Refused Bequest** — a subclass or implementer that ignores or overrides most of what it inherits.

- Cite the standard file and rule for each documented-standard issue.
- A documented standard overrides the baseline.
- Skip anything a tool already enforces.
- Anchor every finding to a `file:line` (or hunk) in the diff.
- Mark documented-standard breaches as RED; baseline smells as YELLOW or GREEN.

Return **only** a JSON object: `{"report": "<single ## Standards block, or GREEN - No Standards issues detected.>"}`. End the report with a verdict line: `**Ready to merge:** Yes | No | With fixes`. Keep the report under 400 words. No `## Spec` section."""

SPEC_REVIEWER_PROMPT = """You are the Spec axis of a two-axis /build-afk review for {{REPO}}.

Set `workdir` to `{{WORKTREE}}` for every `exec` call and `Read` relative paths from that worktree.

Run:
- `git diff {{PRE_BUILD_SHA}}...HEAD`
- `git log {{PRE_BUILD_SHA}}..HEAD --oneline`

Review the diff against this issue spec:

SPEC:
{{ISSUE_BODY}}

The build agent's self-report follows. Verify every claim against the diff; a claim not visible in the diff is a finding.

IMPLEMENTER CLAIMS:
{{IMPLEMENTER_CLAIMS}}

If the issue references a parent spec, ADR, or PR, read those for context.
For each finding, quote the spec line that is missing, partial, or mis-implemented, and anchor it to a `file:line` in the diff. Call out scope creep. Do not apply coding-style judgements; those belong in the Standards axis.

Return **only** a JSON object: `{"report": "<single ## Spec block, or GREEN - No Spec issues detected.>"}`. End the report with a verdict line: `**Ready to merge:** Yes | No | With fixes`. Keep the report under 400 words. No `## Standards` section."""

ADJUDICATE_PROMPT = """You are the parent adjudicator for a /build-afk review.

Issue #{{ISSUE_NUMBER}}: {{ISSUE_TITLE}}

Issue body:
{{ISSUE_BODY}}

Standards review:
{{STANDARDS_REVIEW}}

Spec review:
{{SPEC_REVIEW}}

Implementer's own report (verify against the diff; do not trust it):
{{IMPLEMENTER_CLAIMS}}

Diff ({{PRE_BUILD_SHA}}...HEAD):
{{DIFF}}

Commit list:
{{COMMITS}}

Classify **every finding** in the Standards and Spec reviews using this rubric:
- **auto-fix** — safe and small standards/spec gaps and deterministic trivial fixes (rename a symbol, move a method, add a null check, fix a comparison, add a missing assertion, etc.). A fix agent can apply these and run build/test unattended.
- **ignore** — false positives, lintable style nits already covered by `dotnet format`, or reviewer guesses not supported by the diff.
- **BLOCKED** — big seam/architectural risk, ambiguous or contradictory spec, missing domain knowledge, a fix too large or risky for unattended work, or the two review axes contradict each other.

Return **only** a JSON object matching this schema:
```json
{
  "status": "PROCEED" | "FIX" | "BLOCKED",
  "findings": [
    {"axis": "Standards" | "Spec", "text": "<the original finding>", "classification": "auto-fix" | "ignore" | "BLOCKED", "reason": "<why this classification>"}
  ],
  "fix_instructions": "<concrete ordered list if FIX; empty otherwise>",
  "reason": "<overall explanation>"
}
```

If `status` is PROCEED, the issue is complete. If FIX, make `fix_instructions` specific enough for a fix agent and order them: spec gaps and blocking findings first, then standards fixes, then cosmetic items. If BLOCKED, explain the risk."""

FIX_PROMPT = r"""You are a /build-afk fix agent. The build agent completed issue #{{ISSUE_NUMBER}} in the worktree below and opened a draft PR.

Worktree: `{{WORKTREE}}`
Base commit: `{{PRE_BUILD_SHA}}`
Branch: `{{BRANCH}}`
PR URL: `{{PR_URL}}`

The adjudicator's instructions (ordered: spec gaps and blocking findings first, then standards fixes, then cosmetic items):
{{FIX_INSTRUCTIONS}}

Apply the fixes in the given order in the worktree. Do not change the PR base. Keep the worktree in place. Re-run the build and test commands once after the pass, not after each item.

After fixing, run `dotnet format` on changed C# files:
```powershell
$files = (git diff --name-only --diff-filter=AM HEAD) + (git ls-files --others --exclude-standard) |
         Where-Object { $_ -like '*.cs' }
if ($files) {
    $include = foreach ($f in $files) { "--include"; $f }
    dotnet format "Solidworks Inventree Add-In.sln" @include
}
```

Then run the build and test commands:
```powershell
dotnet build "SwInventreeAddin/SwInventreeAddin.csproj" --disable-build-servers
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

If the add-in `bin\Debug\net48\SwInventreeAddin.dll` is locked by SolidWorks, use the test command as the primary compile loop.

Push the updated branch. Return **only** a JSON object matching the build-afk child schema:
```json
{
  "status": "COMPLETE" or "COMPLETE_WITH_CONCERNS" or "BLOCKED",
  "branch": "<actual branch>",
  "pr_number": <integer or null>,
  "pr_url": "<url or null>",
  "pre_build_sha": "<base commit>",
  "worktree_path": "<absolute worktree path>",
  "test_summary": "<one-line result>",
  "review_summary": "<brief note>",
  "screenshot_paths": [],
  "concerns": ["<doubt, when COMPLETE_WITH_CONCERNS>"],
  "blocked_kind": "<context | capability | size | ambiguity — required when BLOCKED>",
  "reason": "<empty when COMPLETE; explanation when BLOCKED>"
}
```"""

STACK_PROMPT = """You are a /build-afk stacking agent. A chained spec produced the child PRs below. Group them into a GitHub stacked PR series so the whole feature can be reviewed and merged bottom-up.

Parent branch: `{{PARENT_BRANCH}}`
Parent spec: #{{PARENT_SPEC}}

Child PRs (bottom-to-top; the first targets `{{PARENT_BRANCH}}`, each higher one targets the previous child branch):
{{PR_DETAILS}}

Use `/git_stack` if the skill or tool is available. Call it with the PR numbers in bottom-to-top order and a `stack_name` derived from "build-afk spec {{PARENT_SPEC}}".

If `/git_stack` is not available, try `gh` if it is installed (`gh pr edit <number> --base <branch>` can chain bases). If neither is available, report `SKIPPED` and do not fail the batch; the child PRs remain as normal chained PRs.

Return **only** a JSON object matching this schema:
```json
{
  "status": "COMPLETE" or "SKIPPED",
  "stack_url": "<url or null>",
  "reason": "<explanation>"
}
```"""

META = {
    "name": "build-afk",
    "description": "Unattended Cloud build/test/review/PR loop for ready-for-agent issues",
    "phases": [
        {"title": "triage", "detail": "Validate the plan and fail fast on guardrail breaches"},
        {"title": "build", "detail": "Implement each issue in an isolated worktree"},
        {"title": "review", "detail": "Run two independent reviewer agents and an adjudicator"},
        {"title": "fix", "detail": "Apply auto-fixes up to two passes"},
        {"title": "final-review", "detail": "Review the cumulative chained diff against the parent spec"},
        {"title": "stack", "detail": "Group chained PRs into a series"},
        {"title": "rollup", "detail": "Collect per-issue status and report"},
    ],
}


def _fill(template, mapping):
    """Replace all placeholders in one pass so inserted values are not re-processed."""
    if not mapping:
        return template
    pattern = re.compile("|".join(re.escape(k) for k in mapping))
    return pattern.sub(lambda m: mapping[m.group(0)], template)


def _load_plan():
    if not PLAN_FILE.exists():
        raise FileNotFoundError(
            f"PLAN.json not found at {PLAN_FILE}. "
            "The orchestrating session must write PLAN.json before starting the workflow."
        )
    with open(PLAN_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def _load_prompt_template():
    if not PROMPT_FILE.exists():
        raise FileNotFoundError(
            f"CHILD_PROMPT.md not found at {PROMPT_FILE}. "
            "Copy it next to workflow.py before starting the workflow."
        )
    with open(PROMPT_FILE, "r", encoding="utf-8") as f:
        return f.read()


def _load_reviewer_profile(name):
    """Load a reviewer profile from the run directory if the parent copied it."""
    path = RUN_DIR / f"{name}.md"
    if not path.exists():
        return None
    text = path.read_text(encoding="utf-8")
    if text.startswith("---"):
        parts = text.split("---", 2)
        if len(parts) >= 3:
            return parts[2].strip()
    return text


def _result(issue, status, reason, blocked_kind="context", **kwargs):
    """Return a normalized per-issue result dictionary."""
    result = {
        "issue_number": issue.get("number"),
        "status": status,
        "branch": issue.get("branch", ""),
        "pr_number": None,
        "pr_url": None,
        "pre_build_sha": None,
        "worktree_path": None,
        "test_summary": "",
        "review_summary": "",
        "screenshot_paths": [],
        "concerns": [],
        "reason": reason,
        **kwargs,
    }
    if status == "BLOCKED":
        result["blocked_kind"] = blocked_kind
    return result


def _implementer_claims(build_result):
    """Render the build agent's self-report for verification by reviewers."""
    lines = [
        f"test_summary: {build_result.get('test_summary', '')}",
        f"review_summary: {build_result.get('review_summary', '')}",
        f"reason: {build_result.get('reason', '')}",
    ]
    concerns = build_result.get("concerns") or []
    if concerns:
        lines.append("concerns:")
        lines.extend(f"- {c}" for c in concerns)
    return "\n".join(lines)


def _summarize_adjudication(adjudication):
    """Summarize adjudicated findings as text, not character counts."""
    findings = adjudication.get("findings") or []
    groups = {"auto-fix": [], "ignore": [], "BLOCKED": []}
    for finding in findings:
        # An out-of-enum classification surfaces as BLOCKED rather than dropping.
        key = finding.get("classification") if finding.get("classification") in groups else "BLOCKED"
        groups[key].append((finding.get("text") or "").strip()[:120])
    parts = []
    if groups["auto-fix"]:
        parts.append("auto-fixed: " + "; ".join(groups["auto-fix"]))
    if groups["ignore"]:
        parts.append("deferred: " + "; ".join(groups["ignore"]))
    if groups["BLOCKED"]:
        parts.append("blocked: " + "; ".join(groups["BLOCKED"]))
    reason = (adjudication.get("reason") or "").strip()
    if reason:
        parts.append(reason)
    return " | ".join(parts) or "no findings"


def _validate_plan(plan):
    """Deterministic, in-process plan validation."""
    errors = []
    if not isinstance(plan, dict):
        return ["PLAN.json must be a JSON object"]

    repo = plan.get("repo")
    if not repo or not isinstance(repo, str):
        errors.append("Missing or invalid repo")

    parent_branch = plan.get("parent_branch")
    if not parent_branch or not isinstance(parent_branch, str):
        errors.append("Missing or invalid parent_branch")
    elif parent_branch.lower() in ("main", "master"):
        errors.append(f"parent_branch must not be main/master: {parent_branch}")

    max_issues = plan.get("max")
    if max_issues is not None:
        if isinstance(max_issues, bool) or not isinstance(max_issues, int) or max_issues < 1:
            errors.append("max must be a positive integer")

    if plan.get("chained") and not isinstance(plan.get("parent_spec_body"), str):
        errors.append("parent_spec_body is required when chained is true")
    elif plan.get("chained") and not plan.get("parent_spec_body", "").strip():
        errors.append("parent_spec_body must not be empty when chained is true")

    issues = plan.get("issues")
    if not isinstance(issues, list) or not issues:
        errors.append("issues must be a non-empty list")
    else:
        required_fields = ["number", "title", "body", "branch", "parent_branch", "target_branch"]
        for idx, issue in enumerate(issues):
            if not isinstance(issue, dict):
                errors.append(f"issue[{idx}] is not an object")
                continue
            for field in required_fields:
                value = issue.get(field)
                if value is None or (isinstance(value, str) and not value.strip()):
                    errors.append(f"issue[{idx}] missing or empty field: {field}")
            if "number" in issue and not isinstance(issue["number"], int):
                errors.append(f"issue[{idx}] number must be an integer")
            labels = issue.get("labels")
            if labels is not None and not isinstance(labels, list):
                errors.append(f"issue[{idx}] labels must be a list of strings")

    return errors


def _tokenize(text):
    """Tokenize text, preserving hyphenated words and numbers."""
    return re.findall(r"[a-z0-9-]+", (text or "").lower())


def _hard_bug_signals(issue):
    """Return (has_signals, signals) for an issue using token/phrase matching."""
    title_tokens = _tokenize(issue.get("title"))
    body_tokens = _tokenize(issue.get("body"))
    labels = issue.get("labels") or []
    if not isinstance(labels, list):
        labels = []
    label_tokens = [token for label in labels if isinstance(label, str) for token in _tokenize(label)]

    all_tokens = title_tokens + body_tokens + label_tokens
    found = set()

    for signal in HARD_BUG_SIGNALS:
        signal_tokens = signal.split()
        if len(signal_tokens) == 1:
            if signal in all_tokens:
                found.add(signal)
        else:
            # multi-word phrase: look for the exact token sequence
            for i in range(len(all_tokens) - len(signal_tokens) + 1):
                if all_tokens[i : i + len(signal_tokens)] == signal_tokens:
                    found.add(signal)
                    break

    return bool(found), sorted(found)


async def _resolve_agent_mode(plan):
    """Pick the agent mode for build/review/fix agents."""
    explicit = plan.get("agent_mode") or os.environ.get("BUILD_AFK_AGENT_MODE")
    if explicit:
        return explicit

    # Smoke-test the cheaper mode once per workflow run.
    prompt = "Return a JSON object: `{'ok': true}`. Do nothing else."
    try:
        result = await agent(
            prompt,
            phase="smoke",
            schema={"type": "object", "properties": {"ok": {"type": "boolean"}}, "required": ["ok"]},
            mode="swe-1.7-standard",
            vm_mode="shared",
            label="smoke-mode",
        )
        if result.get("ok"):
            return "swe-1.7-standard"
    except Exception:
        pass
    return "normal"


def _build_child_prompt(template, issue, repo, target_branch, previous_branch, run_dir, agent_mode):
    is_hard_bug = issue.get("__is_hard_bug", False)
    signals = issue.get("__hard_bug_signals", [])
    mapping = {
        "{{repo}}": repo,
        "{{parent_branch}}": issue.get("parent_branch", ""),
        "{{target_branch}}": target_branch,
        "{{previous_branch}}": previous_branch or "",
        "{{branch}}": issue.get("branch", ""),
        "{{issue_number}}": str(issue.get("number", "")),
        "{{issue_title}}": issue.get("title", ""),
        "{{issue_body}}": issue.get("body", ""),
        "{{run_dir}}": str(run_dir),
        "{{agent_mode}}": agent_mode,
        "{{is_hard_bug}}": "true" if is_hard_bug else "false",
        "{{hard_bug_signals}}": ", ".join(signals) if signals else "none",
    }
    return _fill(template, mapping)


def _git_output(cwd, *args):
    """Run git in a directory and return stdout; ignore non-zero exits."""
    try:
        result = subprocess.run(
            ["git", "-C", str(cwd), *args],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        return (result.stdout or "") + (result.stderr or "")
    except Exception as ex:
        return f"<git failed: {ex}>"


def _review_input(worktree, pre_build_sha):
    if not worktree or not Path(worktree).exists():
        return {"diff": "<worktree not found>", "commits": "<worktree not found>"}
    return {
        "diff": _git_output(worktree, "diff", f"{pre_build_sha}...HEAD"),
        "commits": _git_output(worktree, "log", f"{pre_build_sha}..HEAD", "--oneline"),
    }


def _remove_worktree(worktree):
    """Remove a git worktree, trying to deregister it before deleting."""
    path = Path(worktree)
    if not path.exists():
        return

    git_file = path / ".git"
    main_repo = None
    if git_file.is_file():
        try:
            for line in git_file.read_text(encoding="utf-8").splitlines():
                if line.startswith("gitdir:"):
                    gitdir = Path(line.split(":", 1)[1].strip()).resolve()
                    # gitdir points to .../repo/.git/worktrees/build-issue-N
                    main_repo = gitdir.parent.parent.parent
                    break
        except Exception:
            pass

    if main_repo and (main_repo / ".git").is_dir():
        _git_output(main_repo, "worktree", "remove", "--force", str(path))
    else:
        shutil.rmtree(path, ignore_errors=True)


def _build_reviewer_prompt(phase, mapping):
    """Prefer the copied .devin/agents profile; fall back to the bundled prompt."""
    profile = _load_reviewer_profile(f"code-review-{phase}")
    if profile:
        base = profile
    else:
        base = STANDARDS_REVIEWER_PROMPT if phase == "standards" else SPEC_REVIEWER_PROMPT
    context = (
        f"\n\nRun all commands with `workdir: \"{mapping['{{WORKTREE}}']}\"`.\n"
        f"PRE_BUILD_SHA: {mapping['{{PRE_BUILD_SHA}}']}\n"
        f"Return only a JSON object: `{{'report': '<your markdown report>'}}`."
    )
    prompt = _fill(base + context, mapping)
    if phase == "spec":
        prompt += (
            f"\n\nSPEC:\n{mapping['{{ISSUE_BODY}}']}"
            "\n\nIMPLEMENTER CLAIMS (the build agent's self-report — verify each claim"
            f" against the diff; do not trust it):\n{mapping.get('{{IMPLEMENTER_CLAIMS}}', '')}"
        )
    return prompt


async def _run_review(phase, issue, build_result, repo, run_dir, agent_mode, claims=None, label_phase=None):
    worktree = build_result.get("worktree_path")
    pre_build_sha = build_result.get("pre_build_sha")
    if not worktree or not pre_build_sha:
        return f"## {phase.capitalize()}\nMissing worktree or pre_build_sha; cannot review."

    review_inputs = _review_input(worktree, pre_build_sha)
    mapping = {
        "{{REPO}}": repo,
        "{{WORKTREE}}": str(worktree),
        "{{PRE_BUILD_SHA}}": pre_build_sha,
        "{{ISSUE_NUMBER}}": str(issue.get("number", "")),
        "{{ISSUE_TITLE}}": issue.get("title", ""),
        "{{ISSUE_BODY}}": issue.get("body", ""),
        "{{DIFF}}": review_inputs["diff"],
        "{{COMMITS}}": review_inputs["commits"],
        "{{IMPLEMENTER_CLAIMS}}": claims if claims is not None else _implementer_claims(build_result),
    }
    prompt = _build_reviewer_prompt(phase, mapping)

    dispatch_phase = label_phase or f"review-{phase}"
    log(f"build-afk: dispatching {dispatch_phase} for #{issue['number']}")
    result = await agent(
        prompt,
        phase=dispatch_phase,
        schema=REVIEW_SCHEMA,
        mode=agent_mode,
        vm_mode="shared",
        label=f"{dispatch_phase}-{issue['number']}",
    )
    return result.get("report", f"## {phase.capitalize()}\n<empty review output>")


async def _adjudicate(build_result, standards_review, spec_review, issue, repo, run_dir, agent_mode):
    worktree = build_result.get("worktree_path")
    pre_build_sha = build_result.get("pre_build_sha")
    review_inputs = _review_input(worktree, pre_build_sha) if (worktree and pre_build_sha) else {"diff": "", "commits": ""}

    mapping = {
        "{{ISSUE_NUMBER}}": str(issue.get("number", "")),
        "{{ISSUE_TITLE}}": issue.get("title", ""),
        "{{ISSUE_BODY}}": issue.get("body", ""),
        "{{STANDARDS_REVIEW}}": standards_review,
        "{{SPEC_REVIEW}}": spec_review,
        "{{IMPLEMENTER_CLAIMS}}": _implementer_claims(build_result),
        "{{DIFF}}": review_inputs["diff"],
        "{{COMMITS}}": review_inputs["commits"],
        "{{PRE_BUILD_SHA}}": pre_build_sha or "",
    }
    prompt = _fill(ADJUDICATE_PROMPT, mapping)

    log(f"build-afk: adjudicating findings for #{issue['number']}")
    return await agent(
        prompt,
        phase="adjudicate",
        schema=ADJUDICATION_SCHEMA,
        mode="lite",
        vm_mode="shared",
        label=f"adjudicate-{issue['number']}",
    )


async def _review_and_adjudicate(issue, build_result, repo, run_dir, agent_mode):
    """Run Standards + Spec review in parallel and adjudicate the findings."""
    standards_review, spec_review = await asyncio.gather(
        _run_review("standards", issue, build_result, repo, run_dir, agent_mode),
        _run_review("spec", issue, build_result, repo, run_dir, agent_mode),
    )
    adjudication = await _adjudicate(build_result, standards_review, spec_review, issue, repo, run_dir, agent_mode)
    return standards_review, spec_review, adjudication


def _normalize_child_result(result, issue_number, defaults=None):
    """Fill a child agent result with the expected defaults and the issue number."""
    if defaults:
        for key in ["branch", "pr_number", "pr_url", "pre_build_sha", "worktree_path"]:
            result.setdefault(key, defaults.get(key))
    result.setdefault("status", "BLOCKED")
    result.setdefault("branch", "")
    result.setdefault("pr_number", None)
    result.setdefault("pr_url", None)
    result.setdefault("pre_build_sha", None)
    result.setdefault("worktree_path", None)
    result.setdefault("test_summary", "")
    result.setdefault("review_summary", "")
    result.setdefault("screenshot_paths", [])
    result.setdefault("concerns", [])
    result.setdefault("reason", "")
    if result["status"] not in (*SUCCESS_STATUSES, "BLOCKED"):
        reason = result.get("reason") or ""
        result["reason"] = f"Child returned unrecognized status {result['status']!r}" + (
            f": {reason}" if reason else ""
        )
        result["status"] = "BLOCKED"
    if result["status"] == "BLOCKED":
        if result.get("blocked_kind") not in BLOCKED_KINDS:
            result["blocked_kind"] = "ambiguity"
    else:
        result.pop("blocked_kind", None)
    result["issue_number"] = issue_number
    return result


async def _fix_issue(build_result, adjudication, issue, repo, run_dir, agent_mode):
    mapping = {
        "{{ISSUE_NUMBER}}": str(issue.get("number", "")),
        "{{WORKTREE}}": build_result.get("worktree_path", ""),
        "{{PRE_BUILD_SHA}}": build_result.get("pre_build_sha", ""),
        "{{BRANCH}}": build_result.get("branch", ""),
        "{{PR_URL}}": build_result.get("pr_url") or "",
        "{{FIX_INSTRUCTIONS}}": adjudication.get("fix_instructions", ""),
    }
    prompt = _fill(FIX_PROMPT, mapping)

    log(f"build-afk: dispatching fix pass for #{issue['number']}")
    result = await agent(
        prompt,
        phase="fix",
        schema=CHILD_SCHEMA,
        mode=agent_mode,
        vm_mode="shared",
        label=f"fix-{issue['number']}",
    )
    return _normalize_child_result(result, issue["number"], defaults=build_result)


async def _process_issue(template, repo, issue, previous_result, chained, run_dir, agent_mode):
    number = issue["number"]
    branch = issue.get("branch") or f"build/issue-{number}"

    previous_branch = ""
    if chained and previous_result:
        previous_branch = previous_result.get("branch", "") or ""

    target = issue.get("target_branch") or issue.get("parent_branch", "")
    if chained and previous_branch:
        target = previous_branch

    prompt = _build_child_prompt(template, issue, repo, target, previous_branch, run_dir, agent_mode)
    log(f"build-afk: dispatching build for issue #{number} ({issue.get('title','')}) -> {branch} against {target}")

    build_result = await agent(
        prompt,
        phase="build",
        schema=CHILD_SCHEMA,
        mode=agent_mode,
        vm_mode="shared",
        label=f"issue-{number}",
    )
    build_result = _normalize_child_result(build_result, number)

    if build_result["status"] not in SUCCESS_STATUSES:
        _remove_worktree(build_result.get("worktree_path"))
        return build_result

    if not build_result.get("pre_build_sha") or not build_result.get("worktree_path"):
        build_result["status"] = "BLOCKED"
        build_result["reason"] = "Build agent did not return pre_build_sha and worktree_path; cannot run independent review."
        return _normalize_child_result(build_result, number)

    # First review pass.
    standards_review, spec_review, adjudication = await _review_and_adjudicate(
        issue, build_result, repo, run_dir, agent_mode
    )

    if adjudication["status"] == "BLOCKED":
        build_result["status"] = "BLOCKED"
        build_result["reason"] = f"Review blocked: {adjudication['reason']}"
        build_result["review_summary"] = _summarize_adjudication(adjudication)
        _remove_worktree(build_result.get("worktree_path"))
        return _normalize_child_result(build_result, number)

    if adjudication["status"] == "FIX":
        first_pass_summary = _summarize_adjudication(adjudication)
        fix_result = await _fix_issue(build_result, adjudication, issue, repo, run_dir, agent_mode)

        if fix_result["status"] == "BLOCKED":
            fix_result["review_summary"] = f"First-pass review: {first_pass_summary}" + (
                f" | {fix_result['review_summary']}" if fix_result.get("review_summary") else ""
            )
            _remove_worktree(fix_result.get("worktree_path"))
            return fix_result

        # Re-review once after the fix; the two-pass cap is enforced below.
        standards_review_fix, spec_review_fix, adjudication_fix = await _review_and_adjudicate(
            issue, fix_result, repo, run_dir, agent_mode
        )

        if adjudication_fix["status"] in ("FIX", "BLOCKED"):
            fix_result["status"] = "BLOCKED"
            if adjudication_fix["status"] == "FIX":
                fix_result["reason"] = f"Review-fix loop did not converge after two passes: {adjudication_fix['reason']}"
            else:
                fix_result["reason"] = f"Re-review blocked: {adjudication_fix['reason']}"
            fix_result["review_summary"] = (
                f"Fixed: {first_pass_summary} | Re-review: {_summarize_adjudication(adjudication_fix)}"
            )
            _remove_worktree(fix_result.get("worktree_path"))
            return _normalize_child_result(fix_result, number)

        fix_result["review_summary"] = (
            f"Fixed: {first_pass_summary} | Re-review: {_summarize_adjudication(adjudication_fix)}"
        )
        if not chained:
            _remove_worktree(fix_result.get("worktree_path"))
        return fix_result

    build_result["review_summary"] = _summarize_adjudication(adjudication)
    if not chained:
        _remove_worktree(build_result.get("worktree_path"))
    return build_result


async def _process_stack(plan, results, repo, parent_branch, agent_mode):
    chained = plan.get("chained", False)
    if not chained:
        return None

    complete_results = [r for r in results if r.get("status") in SUCCESS_STATUSES and r.get("pr_number")]
    if len(complete_results) < 2:
        log("build-afk: not enough chained PRs to stack")
        return None

    parent_spec = plan.get("parent_spec") or complete_results[0].get("issue_number")
    pr_details = "\n".join(
        f"- #{r.get('pr_number')} ({r.get('branch')})" for r in complete_results
    )
    pr_numbers = ", ".join(str(r.get("pr_number")) for r in complete_results)

    mapping = {
        "{{PARENT_BRANCH}}": parent_branch,
        "{{PARENT_SPEC}}": str(parent_spec),
        "{{PR_DETAILS}}": pr_details,
        "{{PR_NUMBERS}}": pr_numbers,
    }
    prompt = _fill(STACK_PROMPT, mapping)

    log(f"build-afk: dispatching stack creation for spec #{parent_spec}")
    result = await agent(
        prompt,
        phase="stack",
        schema=STACK_SCHEMA,
        mode="lite",
        vm_mode="shared",
        label=f"stack-{parent_spec}",
    )
    return _normalize_stack_result(result)


def _normalize_stack_result(result):
    result.setdefault("status", "SKIPPED")
    result.setdefault("stack_url", None)
    result.setdefault("reason", "")
    return result


async def _run_final_review(plan, results, repo, parent_branch, run_dir, agent_mode):
    """Spec-axis review of the cumulative chained diff (parent_branch...top-of-stack).

    Each chained child passed its own review; this pass checks the whole stacked
    diff against the parent spec so per-child-clean-but-composition-wrong work
    cannot slip through. Returns None when there is nothing to review.
    """
    top_result = next(
        (
            r
            for r in reversed(results)
            if r.get("status") in SUCCESS_STATUSES and r.get("worktree_path")
        ),
        None,
    )
    if not top_result:
        return None

    parent_spec = plan.get("parent_spec") or top_result.get("issue_number")
    pseudo_issue = {
        "number": parent_spec,
        "title": f"spec #{parent_spec}",
        "body": plan.get("parent_spec_body") or "",
    }
    build_like = dict(top_result)
    build_like["pre_build_sha"] = parent_branch

    claims = "\n\n".join(
        f"Issue #{r.get('issue_number')} ({r.get('branch')}):\n{_implementer_claims(r)}"
        for r in results
        if r.get("status") in SUCCESS_STATUSES
    )

    report = await _run_review(
        "spec", pseudo_issue, build_like, repo, run_dir, agent_mode,
        claims=claims, label_phase="final-review",
    )
    adjudication = await _adjudicate(
        build_like,
        "<Standards axis was reviewed per child; final review is spec-axis only.>",
        report,
        pseudo_issue,
        repo,
        run_dir,
        agent_mode,
    )

    if adjudication["status"] == "FIX":
        fix_result = await _fix_issue(build_like, adjudication, pseudo_issue, repo, run_dir, agent_mode)
        if fix_result["status"] in SUCCESS_STATUSES:
            build_like.update(fix_result)
            # The diff base for the final review is always the parent branch.
            build_like["pre_build_sha"] = parent_branch
            report = await _run_review(
                "spec", pseudo_issue, build_like, repo, run_dir, agent_mode,
                claims=claims, label_phase="final-review",
            )
            adjudication = await _adjudicate(
                build_like,
                "<Standards axis was reviewed per child; final review is spec-axis only.>",
                report,
                pseudo_issue,
                repo,
                run_dir,
                agent_mode,
            )

    status = "COMPLETE" if adjudication["status"] == "PROCEED" else "BLOCKED"
    return {
        "status": status,
        "report": report,
        "reason": adjudication.get("reason", ""),
        "findings_summary": _summarize_adjudication(adjudication),
    }


async def main():
    await register_workflow(META)

    plan = _load_plan()
    validation_errors = _validate_plan(plan)
    if validation_errors:
        raise ValueError("PLAN.json failed validation: " + "; ".join(validation_errors))

    repo = plan.get("repo") or "github.com/jrobelia/solidworks-inventree-addin"
    parent_branch = plan["parent_branch"]
    issues = plan.get("issues", [])
    chained = plan.get("chained", False)

    if not os.environ.get("GITHUB_TOKEN"):
        raise ValueError("GITHUB_TOKEN is not set in the environment")

    # Pre-compute hard-bug signals and trim the batch before any child is dispatched.
    for issue in issues:
        is_hard_bug, signals = _hard_bug_signals(issue)
        issue["__is_hard_bug"] = is_hard_bug
        issue["__hard_bug_signals"] = signals

    max_issues = plan.get("max")
    if max_issues is not None:
        if len(issues) > max_issues:
            log(f"build-afk: trimming batch from {len(issues)} to max={max_issues}")
            issues = issues[:max_issues]

    agent_mode = await _resolve_agent_mode(plan)

    if not issues:
        log("build-afk: no issues in PLAN.json; nothing to do")
        with open(RESULTS_FILE, "w", encoding="utf-8") as f:
            json.dump({"results": [], "summary": "No issues in plan"}, f, indent=2)
        return

    template = _load_prompt_template()
    results = []
    chained_blocked = False

    for issue in issues:
        if issue.get("skip"):
            results.append(_result(
                issue,
                "BLOCKED",
                issue.get("skip_reason", "Skipped during pre-flight"),
            ))
            if chained:
                chained_blocked = True
            continue

        if chained and chained_blocked:
            results.append(_result(
                issue,
                "BLOCKED",
                "Chained predecessor blocked; cannot determine a safe base branch",
            ))
            continue

        previous = None
        if chained and results:
            previous = results[-1]

        result = await _process_issue(template, repo, issue, previous, chained, RUN_DIR, agent_mode)
        results.append(result)

        if result.get("status") == "BLOCKED":
            log(f"build-afk: issue #{issue['number']} blocked - {result.get('reason','')}")
            if chained:
                chained_blocked = True

    final_review = None
    stack_result = None
    if chained and not chained_blocked:
        final_review = await _run_final_review(
            plan, results, repo, parent_branch, RUN_DIR, agent_mode
        )
        if not final_review or final_review.get("status") != "BLOCKED":
            stack_result = await _process_stack(plan, results, repo, parent_branch, agent_mode)

    # Chained runs keep successful worktrees alive for the final review; clean
    # up whatever remains now.
    for r in results:
        if r.get("worktree_path"):
            _remove_worktree(r["worktree_path"])

    summary_lines = [
        f"- #{r.get('issue_number','?')}: {r.get('status')} - {r.get('reason','')}".rstrip()
        for r in results
    ]
    if final_review:
        summary_lines.append(f"- final-review: {final_review.get('status')} - {final_review.get('reason','')}")
    if stack_result:
        summary_lines.append(f"- stack: {stack_result.get('status')} - {stack_result.get('reason','')}")
    summary = "\n".join(summary_lines) or "No results"

    report = {
        "repo": repo,
        "parent_branch": parent_branch,
        "results": results,
    }
    if final_review:
        report["final_review"] = final_review
    if stack_result:
        report["stack"] = stack_result
    report["summary"] = summary

    with open(RESULTS_FILE, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    log("build-afk: run complete")
    for line in summary.splitlines():
        log(line)


asyncio.run(main())
