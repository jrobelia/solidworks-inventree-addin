"""Dynamic workflow for unattended /build-afk Cloud builds.

This script is intended to be copied into a per-run directory together with
CHILD_PROMPT.md and a PLAN.json file.  The orchestrating Devin session writes
PLAN.json and then calls run_workflow on the copied script.
"""

import asyncio
import json
from pathlib import Path

PLAN_FILE = Path(__file__).parent / "PLAN.json"
PROMPT_FILE = Path(__file__).parent / "CHILD_PROMPT.md"
RESULTS_FILE = Path(__file__).parent / "RESULTS.json"

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

META = {
    "name": "build-afk",
    "description": "Unattended Cloud build/test/review/PR loop for ready-for-agent issues",
    "phases": [
        {"title": "triage", "detail": "Validate plan and skip blocked tickets"},
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


def _build_prompt(template, issue, repo, target_branch, previous_branch):
    mapping = {
        "{repo}": repo,
        "{parent_branch}": issue.get("parent_branch", ""),
        "{target_branch}": target_branch,
        "{previous_branch}": previous_branch or "",
        "{branch}": issue.get("branch", ""),
        "{issue_number}": str(issue.get("number", "")),
        "{issue_title}": issue.get("title", ""),
        "{issue_body}": issue.get("body", ""),
    }
    text = template
    for token, value in mapping.items():
        text = text.replace(token, value)
    return text


async def _process_issue(template, repo, issue, previous_result, chained):
    number = issue["number"]
    branch = issue.get("branch") or f"build/issue-{number}"
    target = issue.get("target_branch")
    if chained and previous_result and previous_result.get("branch"):
        target = previous_result["branch"]
    if not target:
        target = issue.get("parent_branch", "")

    previous_branch = previous_result.get("branch", "") if previous_result else ""
    prompt = _build_prompt(template, issue, repo, target, previous_branch)

    log(f"build-afk: dispatching issue #{number} ({issue.get('title','')}) -> {branch} against {target}")

    result = await agent(
        prompt,
        phase="build",
        schema=CHILD_SCHEMA,
        mode="normal",
        vm_mode="shared",
        label=f"issue-{number}",
    )

    # Normalize missing fields and tag with the issue number for rollups.
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


async def main():
    await register_workflow(META)

    plan = _load_plan()
    repo = plan.get("repo") or "github.com/jrobelia/solidworks-inventree-addin"
    issues = plan.get("issues", [])
    chained = plan.get("chained", False)

    if not issues:
        log("build-afk: no issues in PLAN.json; nothing to do")
        with open(RESULTS_FILE, "w", encoding="utf-8") as f:
            json.dump({"results": [], "summary": "No issues in plan"}, f, indent=2)
        return

    template = _load_prompt_template()
    results = []

    for issue in issues:
        # Respect an explicit skip flag from pre-flight triage.
        if issue.get("skip"):
            results.append({
                "issue_number": issue["number"],
                "status": "BLOCKED",
                "branch": issue.get("branch", ""),
                "pr_number": None,
                "pr_url": None,
                "test_summary": "",
                "review_summary": "",
                "screenshot_paths": [],
                "reason": issue.get("skip_reason", "Skipped during triage"),
            })
            continue

        previous = results[-1] if results else None
        result = await _process_issue(template, repo, issue, previous, chained)
        results.append(result)

        if result.get("status") == "BLOCKED":
            log(f"build-afk: issue #{issue['number']} blocked - {result.get('reason','')}")
            # Keep processing remaining independent tickets; for chained specs the
            # next agent will inherit the previous branch even if it is BLOCKED,
            # so we also mark a chained break to avoid stacking on a failed branch.
            if chained:
                log("build-afk: stopping chained run because one layer is blocked")
                break

    summary_lines = [
        f"- #{r.get('issue_number','?')}: {r.get('status')} - {r.get('reason','')}".rstrip()
        for r in results
    ]
    summary = "\n".join(summary_lines) or "No results"

    report = {
        "repo": repo,
        "parent_branch": plan.get("parent_branch"),
        "results": results,
        "summary": summary,
    }

    with open(RESULTS_FILE, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    log("build-afk: run complete")
    for line in summary.splitlines():
        log(line)


asyncio.run(main())
