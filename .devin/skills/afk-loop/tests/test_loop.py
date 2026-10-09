import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

PACKAGE = Path(__file__).resolve().parents[1]
STUB = Path(__file__).with_name("stub_tools.py")


class LoopTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="addin afk tests ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.env = dict(os.environ, GIT_AUTHOR_NAME="AFK test", GIT_AUTHOR_EMAIL="afk@example.invalid",
                        GIT_COMMITTER_NAME="AFK test", GIT_COMMITTER_EMAIL="afk@example.invalid",
                        AFK_TEST_ROOT=str(self.root), AFK_REAL_GIT=shutil.which("git"),
                        AFK_TIMEOUT="5", AFK_HEARTBEAT="0.1", AFK_STALE="0.2", AFK_RETRY_DELAYS="[0]")
        self.env.pop("DEVIN_API_KEY", None)
        for name in ("git", "gh", "dotnet", "devin"):
            self.env["AFK_" + name.upper()] = json.dumps([sys.executable, str(STUB), name])
        self.git("init", "--initial-branch=milestone-3")
        (self.root / ".gitignore").write_text(".scratch/\n.worktrees/\n.devin/afk-loop/\n.devin/config.local.json\n", encoding="utf-8")
        (self.root / "AGENTS.md").write_text("Use isolated tests.\n", encoding="utf-8")
        self.git("add", ".gitignore", "AGENTS.md")
        self.git("commit", "-m", "Fixture baseline")

    def git(self, *args):
        return subprocess.check_output([self.env["AFK_REAL_GIT"], *args], cwd=self.root,
                                       env=self.env, stderr=subprocess.STDOUT, text=True).strip()

    def run_loop(self, *args):
        candidates = [parent / "bin" / "bash.exe" for parent in Path(self.env["AFK_REAL_GIT"]).parents]
        bash = next(path for path in candidates if path.is_file()) if os.name == "nt" else Path(shutil.which("bash"))
        return subprocess.run([str(bash), (PACKAGE / "afk-loop.sh").as_posix(), *args], cwd=self.root,
                              env=self.env, text=True, encoding="utf-8", errors="replace",
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)

    def prepare(self):
        result = self.run_loop("--prepare", "--tickets", "50", "--base", "milestone-3")
        self.assertEqual(result.returncode, 0, result.stdout)
        return next((self.root / ".scratch" / "afk-loop").iterdir())

    def approve(self, directory, host=False):
        smoke = self.run_loop("--smoke", directory.name)
        self.assertEqual(smoke.returncode, 0, smoke.stdout)
        approval = directory / "APPROVAL.json"
        data = json.loads(approval.read_text(encoding="utf-8"))
        data.update(confirmed=True, policy_verified=True)
        data["tickets"][0]["seam"] = "Existing Fetch seam, production and Stub adapters; deletion restores orchestration to callers."
        data["tickets"][0]["criteria"] = [{"id": "AC1", "text": "Fetch displays the part.", "kind": "host" if host else "automated"}]
        approval.write_text(json.dumps(data), encoding="utf-8")

    def state(self, directory):
        return json.loads((directory / "STATUS.json").read_text(encoding="utf-8"))

    def calls(self, tool=None):
        lines = (self.root / ".scratch" / "commands.jsonl").read_text(encoding="utf-8").splitlines()
        return [row for row in map(json.loads, lines) if tool is None or row["tool"] == tool]

    def test_prepare_pins_base_and_causes_no_remote_writes(self):
        directory = self.prepare()
        approval = json.loads((directory / "APPROVAL.json").read_text(encoding="utf-8"))
        self.assertEqual(approval["base_branch"], "milestone-3")
        self.assertEqual(approval["base"], self.git("rev-parse", "HEAD"))
        self.assertFalse(approval["confirmed"])
        self.assertEqual(self.calls("devin"), [])
        self.assertFalse(any(row["args"][:2] in (["issue", "edit"], ["pr", "create"])
                             for row in self.calls("gh")))
        self.assertEqual(self.git("status", "--porcelain"), "")

    def test_unconfirmed_run_does_not_launch_a_worker(self):
        directory = self.prepare()
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.calls("devin"), [])

    def test_clean_run_is_local_and_ready_for_host_qa(self):
        directory = self.prepare()
        self.approve(directory, host=True)
        result = self.run_loop("--run", directory.name)
        self.assertEqual(result.returncode, 0, result.stdout)
        ticket = self.state(directory)["tickets"]["50"]
        self.assertEqual(ticket["status"], "ready-for-qa")
        self.assertEqual(ticket["publication"], "not-requested")
        self.assertEqual(ticket["claim"], "not-requested")
        self.assertEqual(ticket["verification"]["criteria"][0]["status"], "host-pending")
        self.assertFalse(any(row["args"][0] == "push" for row in self.calls("git")))
        self.assertFalse(any(row["args"][:2] in (["issue", "edit"], ["pr", "create"])
                             for row in self.calls("gh")))
        self.assertEqual(self.git("status", "--porcelain"), "")

    def test_resume_rejects_missing_evidence(self):
        directory = self.prepare()
        self.approve(directory)
        first = self.run_loop("--run", directory.name)
        self.assertEqual(first.returncode, 0, first.stdout)
        report = Path(self.state(directory)["tickets"]["50"]["verification"]["report_path"])
        report.unlink()
        second = self.run_loop("--resume", directory.name)
        self.assertNotEqual(second.returncode, 0, second.stdout)
        self.assertIn("evidence", second.stdout.lower())

    def test_dead_review_session_falls_back_without_spending_retry_budget(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "dead-resume"
        result = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")

    def test_approved_publication_creates_draft_on_milestone_base(self):
        directory = self.prepare()
        self.approve(directory)
        path = directory / "APPROVAL.json"
        data = json.loads(path.read_text())
        data.update(allow_publish=True, allow_claim=True)
        path.write_text(json.dumps(data), encoding="utf-8")
        result = self.run_loop("--run", directory.name, "--publish", "--claim")
        self.assertEqual(result.returncode, 0, result.stdout)
        ticket = self.state(directory)["tickets"]["50"]
        self.assertEqual(ticket["publication"], "draft-created")
        self.assertEqual(ticket["claim"], "retained")
        create = next(row for row in self.calls("gh") if row["args"][:2] == ["pr", "create"])
        self.assertIn("--draft", create["args"])
        self.assertEqual(create["args"][create["args"].index("--base") + 1], "milestone-3")

    def test_push_failure_remains_visible_and_does_not_create_a_pr(self):
        directory = self.prepare()
        self.approve(directory)
        path = directory / "APPROVAL.json"
        data = json.loads(path.read_text())
        data["allow_publish"] = True
        path.write_text(json.dumps(data), encoding="utf-8")
        self.env["AFK_TEST_SCENARIO"] = "push-fails"
        result = self.run_loop("--run", directory.name, "--publish")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state(directory)["tickets"]["50"]["publication"], "failed")
        self.assertFalse(any(row["args"][:2] == ["pr", "create"] for row in self.calls("gh")))

    def test_empty_crash_resumes_implementation_instead_of_salvaging_a_pass(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "crash"
        first = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertNotEqual(first.returncode, 0)
        self.env["AFK_TEST_SCENARIO"] = "clean"
        second = self.run_loop("--resume", directory.name, "--retries", "0")
        self.assertEqual(second.returncode, 0, second.stdout)
        approval = json.loads((directory / "APPROVAL.json").read_text())
        self.assertNotEqual(self.state(directory)["tickets"]["50"]["head"], approval["base"])

    def test_policy_smoke_is_separate_from_ticket_implementation(self):
        directory = self.prepare()
        result = self.run_loop("--smoke", directory.name)
        self.assertEqual(result.returncode, 0, result.stdout)
        policy = json.loads((directory / "POLICY.json").read_text())
        self.assertTrue(policy["passed"])
        self.assertFalse((directory / "STATUS.json").exists())
        approval = json.loads((directory / "APPROVAL.json").read_text())
        self.assertFalse(approval["confirmed"])
        self.assertTrue(approval["policy_verified"])
        self.assertEqual(self.git("status", "--porcelain"), "")

    def test_other_owner_blocks_implementation(self):
        self.env["AFK_TEST_SCENARIO"] = "other-owner"
        directory = self.prepare()
        self.approve(directory)
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("owner", result.stdout.lower())

    def test_malformed_result_records_a_terminal_blocker(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "malformed-result"
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "blocked")

    def case(self, scenario, *args):
        self.env.pop("AFK_TEST_SCENARIO", None)
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = scenario
        return directory, self.run_loop("--run", directory.name, *args)

    def test_native_and_body_blockers_prevent_preparation(self):
        for scenario in ("native-blocker", "body-blocker"):
            with self.subTest(scenario=scenario):
                self.env["AFK_TEST_SCENARIO"] = scenario
                result = self.run_loop("--prepare", "--tickets", "50")
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.calls("devin"), [])

    def test_dirty_checkout_prevents_preparation(self):
        (self.root / "unrelated.txt").write_text("preserve", encoding="utf-8")
        result = self.run_loop("--prepare", "--tickets", "50")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.root / "unrelated.txt").read_text(), "preserve")

    def test_duplicate_ticket_arguments_are_rejected(self):
        result = self.run_loop("--prepare", "--tickets", "50", "50")
        self.assertNotEqual(result.returncode, 0)

    def test_prior_pr_prevents_new_implementation(self):
        self.env["AFK_TEST_SCENARIO"] = "prior-pr"
        directory = self.prepare()
        self.approve(directory)
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("prior work", result.stdout.lower())

    def test_spec_drift_prevents_dispatch(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_BODY"] = "## Acceptance criteria\n- A new unapproved behavior."
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("specification changed", result.stdout.lower())

    def test_red_baseline_prevents_implementation(self):
        directory, result = self.case("red-baseline")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.state(directory)["baseline"]["passed"])
        self.assertFalse(any("implement" in " ".join(row["args"]) for row in self.calls("devin")))

    def test_wrong_commit_and_empty_report_are_blockers(self):
        for scenario in ("wrong-head", "empty-report", "readonly-edit"):
            with self.subTest(scenario=scenario):
                self.setUp()
                directory, result = self.case(scenario)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "blocked")

    def test_last_review_round_cannot_launch_an_unconfirmable_repair(self):
        directory, result = self.case("persistent-finding", "--rounds", "1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("budget", self.state(directory)["tickets"]["50"]["reason"])
        jobs = list((directory / "50/jobs").iterdir())
        self.assertFalse(any(path.name.startswith("repair-") for path in jobs))

    def test_verification_findings_get_one_fix_and_confirm_cycle(self):
        directory, result = self.case("verify-finding")
        self.assertEqual(result.returncode, 0, result.stdout)
        ticket = self.state(directory)["tickets"]["50"]
        self.assertEqual(ticket["verify_repairs"], 1)
        self.assertEqual(ticket["verification"]["round"], 2)
        self.assertEqual(len(list((directory / "50/jobs").glob("verify-*"))), 2)

    def test_zero_verification_budget_blocks_without_repair(self):
        directory, result = self.case("verify-finding", "--verify-rounds", "0")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(path.name.startswith("repair-") for path in (directory / "50/jobs").iterdir()))

    def test_contract_nudge_preserves_stream_and_does_not_repeat_implementation(self):
        directory, result = self.case("missing-once", "--retries", "1")
        self.assertEqual(result.returncode, 0, result.stdout)
        jobs = sorted((directory / "50/jobs").glob("implement-*"))
        self.assertEqual(len(jobs), 2)
        self.assertTrue(all((path / "stream.log").is_file() for path in jobs))
        self.assertTrue(json.loads((jobs[1] / "job.json").read_text())["salvage"])

    def test_timeout_is_bounded_and_checkpointed(self):
        directory = self.prepare()
        self.approve(directory)
        self.env.update(AFK_TEST_SCENARIO="hang", AFK_TIMEOUT="1")
        result = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "blocked")
        self.assertIn("timeout", (directory / "PROGRESS.md").read_text().lower())

    def test_utf8_bom_result_is_accepted(self):
        directory, result = self.case("bom")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")

    def test_remote_flags_need_separate_approval(self):
        directory = self.prepare()
        self.approve(directory)
        result = self.run_loop("--run", directory.name, "--publish", "--claim")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(row["args"][:2] == ["issue", "edit"] for row in self.calls("gh")))

    def test_prior_work_appearing_after_preparation_blocks_start(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "prior-pr"
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("prior work", result.stdout.lower())

    def test_post_verify_review_findings_cannot_bypass_the_verify_repair_budget(self):
        directory, result = self.case("post-verify-findings")
        self.assertNotEqual(result.returncode, 0)
        repairs = list((directory / "50/jobs").glob("repair-*"))
        self.assertEqual(len(repairs), 1, result.stdout)

    def test_missing_ci_is_pending_not_a_green_publication(self):
        directory = self.prepare()
        self.approve(directory)
        approval = directory / "APPROVAL.json"
        data = json.loads(approval.read_text())
        data["allow_publish"] = True
        approval.write_text(json.dumps(data), encoding="utf-8")
        self.env["AFK_TEST_SCENARIO"] = "no-ci"
        result = self.run_loop("--run", directory.name, "--publish")
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["ci"], "pending")


if __name__ == "__main__":
    unittest.main()
