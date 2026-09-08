"""Unit tests for build-afk workflow.py helpers and dispatch gating.

workflow.py executes asyncio.run(main()) at import time, so this module loads it
with importlib while stubbing the runtime-provided globals (agent,
register_workflow, log) into builtins. The import-time run aborts inside
_load_plan() because no PLAN.json sits next to workflow.py; every function is
defined by then, so the module object is still usable for tests.

Run from the skill directory:
    python -m pytest evals/test_workflow.py
"""

import asyncio
import builtins
import importlib.util
import json
from pathlib import Path

import pytest

SKILL_DIR = Path(__file__).resolve().parents[1]
WORKFLOW_PATH = SKILL_DIR / "workflow.py"


async def _noop_async(*args, **kwargs):
    return None


def _missing_agent(*args, **kwargs):
    raise AssertionError("agent() must be stubbed in this test")


@pytest.fixture()
def wf():
    """Load workflow.py as a module with the runtime globals stubbed."""
    builtins.register_workflow = _noop_async
    builtins.log = lambda *a, **k: None
    builtins.agent = _missing_agent
    spec = importlib.util.spec_from_file_location("build_afk_workflow", WORKFLOW_PATH)
    mod = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(mod)
    except FileNotFoundError:
        pass  # main() aborts at _load_plan(); all functions are defined by then
    return mod


def _issue(n):
    return {
        "number": n,
        "title": f"ticket {n}",
        "body": f"body for ticket {n}",
        "branch": f"build/issue-{n}",
        "parent_branch": "feature/x",
        "target_branch": "feature/x",
    }


def _base_plan(chained=False):
    plan = {
        "repo": "github.com/example/repo",
        "parent_branch": "feature/x",
        "agent_mode": "test-mode",
        "issues": [_issue(1), _issue(2)],
    }
    if chained:
        plan["chained"] = True
        plan["parent_spec"] = 100
        plan["parent_spec_body"] = "the parent spec body"
    return plan


def _write_plan(tmp_path, wf, plan):
    plan_path = tmp_path / "PLAN.json"
    plan_path.write_text(json.dumps(plan), encoding="utf-8")
    results_path = tmp_path / "RESULTS.json"
    wf.PLAN_FILE = plan_path
    wf.RESULTS_FILE = results_path
    wf.PROMPT_FILE = SKILL_DIR / "CHILD_PROMPT.md"
    return results_path


def _agent_stub(build_status="COMPLETE"):
    calls = []

    async def stub(prompt, phase=None, **kwargs):
        calls.append(phase)
        if phase == "build":
            return {
                "status": build_status,
                "branch": "build/issue-x",
                "pr_number": 42,
                "pr_url": "https://example.test/pr/42",
                "worktree_path": "C:/devin/worktrees/nonexistent-test-wt",
                "pre_build_sha": "abc123",
            }
        if phase == "final-review" or (phase or "").startswith("review"):
            return {"report": "GREEN - no issues"}
        if phase == "adjudicate":
            return {"status": "PROCEED", "findings": [], "fix_instructions": "", "reason": "clean"}
        if phase == "stack":
            return {"status": "COMPLETE", "stack_url": None, "reason": ""}
        return {}

    return stub, calls


# -- result normalization ----------------------------------------------------


def test_complete_with_concerns_survives_normalization(wf):
    result = wf._normalize_child_result(
        {"status": "COMPLETE_WITH_CONCERNS", "concerns": ["edge case around retries"]},
        issue_number=7,
    )
    assert result["status"] == "COMPLETE_WITH_CONCERNS"
    assert result["concerns"] == ["edge case around retries"]
    assert result["issue_number"] == 7
    assert "blocked_kind" not in result


def test_blocked_kind_required_on_blocked_and_removed_otherwise(wf):
    defaulted = wf._normalize_child_result({"status": "BLOCKED", "reason": "stuck"}, 1)
    assert defaulted["blocked_kind"] in wf.BLOCKED_KINDS

    explicit = wf._normalize_child_result(
        {"status": "BLOCKED", "reason": "needs info", "blocked_kind": "context"}, 2
    )
    assert explicit["blocked_kind"] == "context"

    invalid = wf._normalize_child_result(
        {"status": "BLOCKED", "reason": "x", "blocked_kind": "bogus"}, 3
    )
    assert invalid["blocked_kind"] in wf.BLOCKED_KINDS

    complete = wf._normalize_child_result({"status": "COMPLETE", "blocked_kind": "size"}, 4)
    assert "blocked_kind" not in complete


# -- prompt construction ------------------------------------------------------


def _review_mapping(wf):
    return {
        "{{REPO}}": "repo",
        "{{WORKTREE}}": "C:/wt",
        "{{PRE_BUILD_SHA}}": "abc",
        "{{ISSUE_NUMBER}}": "5",
        "{{ISSUE_TITLE}}": "t",
        "{{ISSUE_BODY}}": "the spec text",
        "{{DIFF}}": "d",
        "{{COMMITS}}": "c",
        "{{IMPLEMENTER_CLAIMS}}": "test_summary: 539 passed\nconcerns:\n- flaky timing",
    }


def test_spec_review_prompt_carries_implementer_claims(wf):
    prompt = wf._build_reviewer_prompt("spec", _review_mapping(wf))
    assert "IMPLEMENTER CLAIMS" in prompt
    assert "flaky timing" in prompt
    assert "the spec text" in prompt


def test_adjudicator_prompt_orders_fix_instructions(wf):
    prompt = wf.ADJUDICATE_PROMPT.lower()
    assert "spec" in prompt and "cosmetic" in prompt
    assert "order" in prompt


def test_adjudicator_receives_implementer_claims(wf):
    assert "{{IMPLEMENTER_CLAIMS}}" in wf.ADJUDICATE_PROMPT


# -- review summary -----------------------------------------------------------


def test_review_summary_uses_findings_not_char_counts(wf):
    adjudication = {
        "status": "FIX",
        "findings": [
            {
                "axis": "Spec",
                "text": "missing progress reporting",
                "classification": "auto-fix",
                "reason": "spec line 4",
            },
            {
                "axis": "Standards",
                "text": "magic number",
                "classification": "ignore",
                "reason": "style only",
            },
        ],
        "fix_instructions": "add progress",
        "reason": "one gap",
    }
    summary = wf._summarize_adjudication(adjudication)
    assert "missing progress reporting" in summary
    assert "chars" not in summary


# -- end-to-end gating through main() -----------------------------------------


def test_final_review_runs_for_unblocked_chain(tmp_path, wf, monkeypatch):
    monkeypatch.setenv("GITHUB_TOKEN", "x")
    results_path = _write_plan(tmp_path, wf, _base_plan(chained=True))
    stub, calls = _agent_stub()
    wf.agent = stub

    asyncio.run(wf.main())

    assert "final-review" in calls
    assert "stack" in calls
    report = json.loads(results_path.read_text(encoding="utf-8"))
    assert report["final_review"]["status"] == "COMPLETE"
    assert "stack" in report


def test_final_review_skipped_when_chain_blocked(tmp_path, wf, monkeypatch):
    monkeypatch.setenv("GITHUB_TOKEN", "x")
    results_path = _write_plan(tmp_path, wf, _base_plan(chained=True))
    stub, calls = _agent_stub(build_status="BLOCKED")
    wf.agent = stub

    asyncio.run(wf.main())

    assert "final-review" not in calls
    assert "stack" not in calls
    report = json.loads(results_path.read_text(encoding="utf-8"))
    assert all(r["status"] == "BLOCKED" for r in report["results"])
    assert "final_review" not in report
    assert report["results"][0]["blocked_kind"] == "ambiguity"
    assert report["results"][1]["blocked_kind"] == "context"


def test_no_final_review_for_independent_batch(tmp_path, wf, monkeypatch):
    monkeypatch.setenv("GITHUB_TOKEN", "x")
    results_path = _write_plan(tmp_path, wf, _base_plan(chained=False))
    stub, calls = _agent_stub()
    wf.agent = stub

    asyncio.run(wf.main())

    assert "final-review" not in calls
    assert "stack" not in calls
    report = json.loads(results_path.read_text(encoding="utf-8"))
    assert "final_review" not in report


def test_chained_plan_requires_parent_spec_body(tmp_path, wf, monkeypatch):
    monkeypatch.setenv("GITHUB_TOKEN", "x")
    plan = _base_plan(chained=True)
    del plan["parent_spec_body"]
    _write_plan(tmp_path, wf, plan)
    wf.agent = _agent_stub()[0]

    with pytest.raises(ValueError, match="parent_spec_body"):
        asyncio.run(wf.main())


def test_skipped_child_blocks_the_chain(tmp_path, wf, monkeypatch):
    monkeypatch.setenv("GITHUB_TOKEN", "x")
    plan = _base_plan(chained=True)
    plan["issues"][0]["skip"] = True
    plan["issues"][0]["skip_reason"] = "pre-flight"
    results_path = _write_plan(tmp_path, wf, plan)
    stub, calls = _agent_stub()
    wf.agent = stub

    asyncio.run(wf.main())

    report = json.loads(results_path.read_text(encoding="utf-8"))
    assert report["results"][0]["status"] == "BLOCKED"
    assert report["results"][0]["blocked_kind"] == "context"
    assert report["results"][1]["status"] == "BLOCKED"
    assert "final_review" not in report
    assert "stack" not in report


def test_unrecognized_status_coerced_to_blocked(wf):
    result = wf._normalize_child_result({"status": "DONE"}, 9)
    assert result["status"] == "BLOCKED"
    assert result["blocked_kind"] == "ambiguity"
    assert "unrecognized status" in result["reason"]


def test_fix_pass_preserves_first_pass_findings_in_review_summary(tmp_path, wf, monkeypatch):
    monkeypatch.setenv("GITHUB_TOKEN", "x")
    plan = _base_plan(chained=False)
    plan["issues"] = plan["issues"][:1]
    results_path = _write_plan(tmp_path, wf, plan)

    adjudicate_calls = 0

    async def stub(prompt, phase=None, **kwargs):
        nonlocal adjudicate_calls
        if phase == "build":
            return {
                "status": "COMPLETE",
                "branch": "build/issue-1",
                "worktree_path": "C:/devin/worktrees/nonexistent-test-wt",
                "pre_build_sha": "abc123",
            }
        if (phase or "").startswith("review"):
            return {"report": "missing progress reporting"}
        if phase == "adjudicate":
            adjudicate_calls += 1
            if adjudicate_calls == 1:
                return {
                    "status": "FIX",
                    "findings": [
                        {
                            "axis": "Spec",
                            "text": "missing progress reporting",
                            "classification": "auto-fix",
                            "reason": "spec line 4",
                        }
                    ],
                    "fix_instructions": "add progress reporting",
                    "reason": "one gap",
                }
            return {"status": "PROCEED", "findings": [], "fix_instructions": "", "reason": "clean"}
        if phase == "fix":
            return {"status": "COMPLETE"}
        return {}

    wf.agent = stub
    asyncio.run(wf.main())

    report = json.loads(results_path.read_text(encoding="utf-8"))
    summary = report["results"][0]["review_summary"]
    assert "missing progress reporting" in summary
    assert "chars" not in summary
