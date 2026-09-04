"""Dynamic workflow for unattended /build-afk Cloud builds.

This script is intended to be copied into a per-run directory together with
CHILD_PROMPT.md, WPF_HARNESS.md, and a PLAN.json file.  The orchestrating
Devin session writes PLAN.json and then calls run_workflow on the copied
script.
"""

import asyncio
import json
import os
from pathlib import Path

PLAN_FILE = Path(__file__).parent / "PLAN.json"
PROMPT_FILE = Path(__file__).parent / "CHILD_PROMPT.md"
RESULTS_FILE = Path(__file__).parent / "RESULTS.json"
RUN_DIR = Path(__file__).parent

CHILD_SCHEMA = {
    "type": "object",
    "properties": {
        "status": {"type": "string", "enum": ["COMPLETE", "BLOCKED"]},
        "branch": {"type": "string"},
        "pr_number": {"type": ["integer", "null"]},
        "pr_url": {"type": ["string", "null"]},
        "test_summary": {"type": "string"},
        "review_summary": {"type": "string"},
        "screenshot_paths": {
            "type": "array",
            "items": {"type": "string"},
        },
        "reason": {"type": "string"},
    },
    "required": ["status", "branch", "reason"],
}

TRIAGE_SCHEMA = {
    "type": "object",
    "properties": {
        "valid": {"type": "boolean"},
        "errors": {
            "type": "array",
            "items": {"type": "string"},
        },
    },
    "required": ["valid", "errors"],
}

META = {
    "name": "build-afk",
    "description": "Unattended Cloud build/test/review/PR loop for ready-for-agent issues",
    "phases": [
        {"title": "triage", "detail": "Validate the plan and fail fast on guardrail breaches"},
        {"title": "build", "detail": "Implement each issue in an isolated worktree"},
        {"title": "rollup", "detail": "Collect per-issue status and report"},
    ],
}


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


def _result(issue, status, reason, **kwargs):
    """Return a normalized per-issue result dictionary.

    Optional fields are passed through so the schema only has to be extended
    in one place.
    """
    return {
        "issue_number": issue.get("number"),
        "status": status,
        "branch": issue.get("branch", ""),
        "pr_number": None,
        "pr_url": None,
        "test_summary": "",
        "review_summary": "",
        "screenshot_paths": [],
        "reason": reason,
        **kwargs,
    }


def _build_prompt(template, issue, repo, target_branch, previous_branch, run_dir):
    # str.replace is used instead of str.format because the template contains
    # JSON braces (the child output schema) that would be misinterpreted by
    # str.format.
    mapping = {
        "{repo}": repo,
        "{parent_branch}": issue.get("parent_branch", ""),
        "{target_branch}": target_branch,
        "{previous_branch}": previous_branch or "",
        "{branch}": issue.get("branch", ""),
        "{issue_number}": str(issue.get("number", "")),
        "{issue_title}": issue.get("title", ""),
        "{issue_body}": issue.get("body", ""),
        "{run_dir}": str(run_dir),
    }
    text = template
    for token, value in mapping.items():
        text = text.replace(token, value)
    return text


async def _process_issue(template, repo, issue, previous_result, chained, run_dir):
    number = issue["number"]
    branch = issue.get("branch") or f"build/issue-{number}"

    # In a chained batch the PR base is the previous child's branch.
    previous_branch = ""
    if chained and previous_result:
        previous_branch = previous_result.get("branch", "") or ""

    target = issue.get("target_branch") or issue.get("parent_branch", "")
    if chained and previous_branch:
        target = previous_branch

    prompt = _build_prompt(template, issue, repo, target, previous_branch, run_dir)

    log(f"build-afk: dispatching issue #{number} ({issue.get('title','')}) -> {branch} against {target}")

    result = await agent(
        prompt,
        phase="build",
        schema=CHILD_SCHEMA,
        mode="normal",
        vm_mode="shared",
        label=f"issue-{number}",
    )

    # Child agents may omit optional keys; fill defaults so roll-up code can
    # assume a complete record.
    result.setdefault("status", "BLOCKED")
    result["issue_number"] = number
    result.setdefault("branch", branch)
    result.setdefault("pr_number", None)
    result.setdefault("pr_url", None)
    result.setdefault("test_summary", "")
    result.setdefault("review_summary", "")
    result.setdefault("screenshot_paths", [])
    result.setdefault("reason", "")
    return result


async def _triage_plan(plan, run_dir):
    prompt = (
        "You are the triage agent for /build-afk. Read PLAN.json in the same "
        f"directory as this prompt ({run_dir}). Validate the plan against the "
        "guardrails below and return a JSON object matching this schema:\n"
        "{\"valid\": true|false, \"errors\": [\"...\"]}\n\n"
        "Guardrails to check:\n"
        "1. plan['parent_branch'] must exist and not be 'main' or 'master'.\n"
        "2. If plan['max'] is present, it must be a positive integer.\n"
        "3. plan['issues'] must be a non-empty list.\n"
        "4. Each issue must have number, title, body, branch, parent_branch, and target_branch.\n"
        "5. The GITHUB_TOKEN environment variable must be set.\n\n"
        "Only report actual problems. If the plan is valid, return valid=true with an empty errors list."
    )
    return await agent(
        prompt,
        phase="triage",
        schema=TRIAGE_SCHEMA,
        mode="lite",
        vm_mode="shared",
        label="triage",
    )


async def main():
    await register_workflow(META)

    plan = _load_plan()
    repo = plan.get("repo") or "github.com/jrobelia/solidworks-inventree-addin"
    parent_branch = plan.get("parent_branch", "")
    issues = plan.get("issues", [])
    chained = plan.get("chained", False)

    # Fail-fast guardrails before any child work begins.
    if not parent_branch:
        raise ValueError("PLAN.json is missing parent_branch")
    if parent_branch in ("main", "master"):
        raise ValueError(f"Refusing to start /build-afk from default branch '{parent_branch}'")
    if not os.environ.get("GITHUB_TOKEN"):
        raise ValueError("GITHUB_TOKEN is not set in the environment")

    triage = await _triage_plan(plan, RUN_DIR)
    if not triage.get("valid"):
        errors = triage.get("errors", [])
        raise ValueError("Plan failed triage: " + "; ".join(errors))

    max_issues = plan.get("max")
    if max_issues is not None:
        if len(issues) > max_issues:
            log(f"build-afk: trimming batch from {len(issues)} to max={max_issues}")
            issues = issues[:max_issues]

    if not issues:
        log("build-afk: no issues in PLAN.json; nothing to do")
        with open(RESULTS_FILE, "w", encoding="utf-8") as f:
            json.dump({"results": [], "summary": "No issues in plan"}, f, indent=2)
        return

    template = _load_prompt_template()
    results = []
    chained_blocked = False

    for issue in issues:
        # Respect an explicit skip flag from pre-flight triage.
        if issue.get("skip"):
            results.append(_result(
                issue,
                "BLOCKED",
                issue.get("skip_reason", "Skipped during triage"),
            ))
            continue

        # In a chained batch, one blocked layer means later layers cannot be
        # safely based on it. Record every remaining ticket as blocked so the
        # rollup still reports them for re-dispatch.
        if chained and chained_blocked:
            results.append(_result(
                issue,
                "BLOCKED",
                "Chained predecessor blocked; cannot determine a safe base branch",
            ))
            continue

        # For independent batches there is no previous branch; for chained
        # batches only the immediately preceding result is relevant.
        previous = None
        if chained and results:
            previous = results[-1]

        result = await _process_issue(template, repo, issue, previous, chained, RUN_DIR)
        results.append(result)

        if result.get("status") == "BLOCKED":
            log(f"build-afk: issue #{issue['number']} blocked - {result.get('reason','')}")
            if chained:
                chained_blocked = True

    summary_lines = [
        f"- #{r.get('issue_number','?')}: {r.get('status')} - {r.get('reason','')}".rstrip()
        for r in results
    ]
    summary = "\n".join(summary_lines) or "No results"

    report = {
        "repo": repo,
        "parent_branch": parent_branch,
        "results": results,
        "summary": summary,
    }

    with open(RESULTS_FILE, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    log("build-afk: run complete")
    for line in summary.splitlines():
        log(line)


asyncio.run(main())
