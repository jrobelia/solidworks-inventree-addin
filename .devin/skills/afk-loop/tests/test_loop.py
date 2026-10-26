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

FAST = os.environ.get("AFK_TEST_FAST") == "1"


def slow(case):
    return unittest.skipIf(FAST, "end-to-end lifecycle skipped; run without AFK_TEST_FAST before committing")(case)


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
        for name in list(self.env):
            if name.startswith("MODEL_"):
                self.env.pop(name)
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

    def commit_files(self, files, message):
        for name, content in files.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
            self.git("add", "--", name)
        self.git("commit", "-m", message)
        return self.git("rev-parse", "HEAD")

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

    def approve(self, directory, host=False, verified=True):
        if verified:
            smoke = self.run_loop("--smoke", directory.name)
            self.assertEqual(smoke.returncode, 0, smoke.stdout)
        approval = directory / "APPROVAL.json"
        data = json.loads(approval.read_text(encoding="utf-8"))
        data.update(confirmed=True, policy_verified=verified)
        data["tickets"][0]["seam"] = "Existing Fetch seam, production and Stub adapters; deletion restores orchestration to callers."
        data["tickets"][0]["criteria"] = [{"id": "AC1", "text": "Fetch displays the part.", "kind": "host" if host else "automated"}]
        approval.write_text(json.dumps(data), encoding="utf-8")

    def approve_reference(self, directory, commit):
        approval = directory / "APPROVAL.json"
        data = json.loads(approval.read_text(encoding="utf-8"))
        data["tickets"][0]["prior_work_references"] = [
            {"commit": commit, "reason": "Approved ADR design reference, not an implementation."}]
        approval.write_text(json.dumps(data), encoding="utf-8")

    def auth_paused_run(self):
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.env["DEVIN_API_KEY"] = "test-only-override"
        blocked = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertEqual(blocked.returncode, 2, blocked.stdout)
        self.assertIn("DEVIN_API_KEY", blocked.stdout)
        self.env.pop("DEVIN_API_KEY")
        return directory

    def assert_prior_work_blocked(self, directory, message="Prior work"):
        result = self.run_loop("--run", directory.name)
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertIn(message, result.stdout)
        self.assertEqual(self.calls("devin"), [])

    def state(self, directory):
        return json.loads((directory / "STATUS.json").read_text(encoding="utf-8"))

    def calls(self, tool=None):
        lines = (self.root / ".scratch" / "commands.jsonl").read_text(encoding="utf-8").splitlines()
        return [row for row in map(json.loads, lines) if tool is None or row["tool"] == tool]

    def worker_models(self):
        models = []
        for call in self.calls("devin"):
            args = call["args"]
            if "--prompt-file" in args:
                job = Path(args[args.index("--prompt-file") + 1]).with_name("job.json")
                phase = json.loads(job.read_text(encoding="utf-8"))["phase"]
                models.append((phase, args[args.index("--model") + 1]))
        return models

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

    @slow
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
        progress = (directory / "PROGRESS.md").read_text(encoding="utf-8")
        self.assertIn("tickets #50", progress)
        self.assertIn("implement r1 a1 -> clean", progress)

    @slow
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

    @slow
    def test_dead_review_session_falls_back_without_spending_retry_budget(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "dead-resume"
        result = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")

    @slow
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

    @slow
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

    @slow
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

    @slow
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

    @slow
    def test_run_probes_policy_inline_when_unverified(self):
        directory = self.prepare()
        self.approve(directory, verified=False)
        result = self.run_loop("--run", directory.name)
        self.assertEqual(result.returncode, 0, result.stdout)
        approval = json.loads((directory / "APPROVAL.json").read_text())
        self.assertTrue(approval["policy_verified"])
        self.assertTrue(json.loads((directory / "POLICY.json").read_text())["passed"])
        state = self.state(directory)
        self.assertTrue(state["tickets"]["0"]["sessions"]["policy"])
        self.assertEqual(state["tickets"]["50"]["status"], "ready-for-qa")
        dispatched = [row for row in self.calls("devin") if "--prompt-file" in row["args"]]
        self.assertIn("policy-r1-", dispatched[0]["args"][-1])
        self.assertTrue(any("implement-r1-" in row["args"][-1] for row in dispatched[1:]))
        self.assertEqual(self.worker_models(), [
            ("policy", "swe-2-medium"),
            ("implement", "swe-2-high"),
            ("review-spec", "swe-2-max"),
            ("review-standards", "swe-2-high"),
            ("adjudicate", "swe-2-high"),
            ("verify", "swe-2-high"),
        ])

    @slow
    def test_inline_policy_check_survives_auth_pause_and_quota_resume(self):
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.env["DEVIN_API_KEY"] = "test-only-override"
        blocked = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertEqual(blocked.returncode, 2, blocked.stdout)
        self.assertIn("DEVIN_API_KEY", blocked.stdout)
        self.assertEqual(self.calls("devin"), [])
        self.env.pop("DEVIN_API_KEY")
        self.env.update(AFK_TEST_SCENARIO="rate-limit-always", AFK_RATE_LIMIT_MARGIN="0",
                        AFK_RATE_LIMIT_FLOOR="0", AFK_RATE_LIMIT_MAX_WAIT="1")
        limited = self.run_loop("--resume", directory.name, "--retries", "0")
        self.assertEqual(limited.returncode, 3, limited.stdout)
        self.assertTrue(json.loads((directory / "POLICY.json").read_text())["passed"])
        self.env["AFK_TEST_SCENARIO"] = "clean"
        resumed = self.run_loop("--resume", directory.name, "--retries", "0")
        self.assertEqual(resumed.returncode, 0, resumed.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")
        self.assertEqual(sum(phase == "policy" for phase, _ in self.worker_models()), 1)

    def test_resume_still_rejects_human_contract_changes(self):
        directory = self.auth_paused_run()
        approval = directory / "APPROVAL.json"
        original = approval.read_text(encoding="utf-8")
        for field in ("criteria", "seam", "snapshot", "allow_publish"):
            with self.subTest(field=field):
                data = json.loads(original)
                data["policy_verified"] = True
                ticket = data["tickets"][0]
                if field == "criteria":
                    ticket["criteria"][0]["text"] = "A different requirement."
                elif field == "seam":
                    ticket["seam"] = "A different public interface."
                elif field == "snapshot":
                    ticket["snapshot"]["ticket"]["body"] = "A different supplied specification."
                else:
                    data["allow_publish"] = True
                approval.write_text(json.dumps(data), encoding="utf-8")
                result = self.run_loop("--resume", directory.name, "--retries", "0")
                self.assertEqual(result.returncode, 2, result.stdout)
                self.assertIn("Approved delivery contract changed", result.stdout)
                self.assertEqual(self.calls("devin"), [])

    def test_resume_still_rejects_package_changes(self):
        directory = self.auth_paused_run()
        path = directory / "STATUS.json"
        state = self.state(directory)
        state["package_hash"] = "0" * 64
        path.write_text(json.dumps(state), encoding="utf-8")
        result = self.run_loop("--resume", directory.name, "--retries", "0")
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertIn("AFK package changed", result.stdout)
        self.assertEqual(self.calls("devin"), [])

    def test_policy_flag_without_proof_cannot_launch_implementation(self):
        directory = self.prepare()
        self.approve(directory, verified=False)
        path = directory / "APPROVAL.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        data["policy_verified"] = True
        path.write_text(json.dumps(data), encoding="utf-8")
        result = self.run_loop("--run", directory.name)
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertIn("POLICY.json", result.stdout)
        self.assertEqual(self.state(directory)["tickets"], {})
        self.assertEqual(self.calls("devin"), [])

    @slow
    def test_group_model_overrides_remain_supported(self):
        self.env.update(MODEL_IMPL="custom-impl", MODEL_REVIEW="custom-review",
                        MODEL_POLICY="", MODEL_REVIEW_SPEC="", MODEL_REVIEW_STANDARDS="",
                        MODEL_ADJUDICATE="", MODEL_VERIFY="")
        directory, result = self.case("review-finding")
        self.assertEqual(result.returncode, 0, result.stdout)
        expected = {"policy": "custom-impl", "implement": "custom-impl", "repair": "custom-impl",
                    "verify": "custom-impl", "review-spec": "custom-review",
                    "review-standards": "custom-review", "adjudicate": "custom-review"}
        for phase, model in self.worker_models():
            with self.subTest(phase=phase):
                self.assertEqual(model, expected[phase])
        self.assertEqual([model for phase, model in self.worker_models() if phase == "repair"], ["custom-impl"])

    @slow
    def test_specific_model_overrides_win_over_groups(self):
        self.env.update(MODEL_IMPL="custom-impl", MODEL_REVIEW="custom-review",
                        MODEL_POLICY="custom-policy", MODEL_REVIEW_SPEC="custom-spec",
                        MODEL_REVIEW_STANDARDS="custom-standards", MODEL_ADJUDICATE="custom-adjudicate",
                        MODEL_VERIFY="custom-verify", MODEL_REPAIR_FINAL="custom-final")
        directory, result = self.case("verify-finding")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.worker_models(), [
            ("policy", "custom-policy"),
            ("implement", "custom-impl"),
            ("review-spec", "custom-spec"),
            ("review-standards", "custom-standards"),
            ("adjudicate", "custom-adjudicate"),
            ("verify", "custom-verify"),
            ("repair", "custom-final"),
            ("review-spec", "custom-spec"),
            ("review-standards", "custom-standards"),
            ("adjudicate", "custom-adjudicate"),
            ("verify", "custom-verify"),
        ])

    @slow
    def test_last_regular_repair_uses_max(self):
        directory, result = self.case("persistent-finding")
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertIn("budget", self.state(directory)["tickets"]["50"]["reason"])
        self.assertEqual([model for phase, model in self.worker_models() if phase == "repair"],
                         ["swe-2-high", "swe-2-max"])

    @slow
    def test_other_owner_blocks_implementation(self):
        self.env["AFK_TEST_SCENARIO"] = "other-owner"
        directory = self.prepare()
        self.approve(directory)
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("owner", result.stdout.lower())

    @slow
    def test_malformed_result_records_a_terminal_blocker(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "malformed-result"
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "blocked")
        self.assertIn("result invalid", (directory / "PROGRESS.md").read_text(encoding="utf-8"))

    @slow
    def test_invalid_result_nudges_its_own_session_before_retrying(self):
        directory, result = self.case("malformed-once", "--retries", "1")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")
        self.assertTrue(any("-r" in row["args"] for row in self.calls("devin")))
        self.assertIn("result invalid", (directory / "PROGRESS.md").read_text(encoding="utf-8"))
        jobs = sorted((directory / "50/jobs").glob("implement-*"))
        self.assertEqual(len(jobs), 2)
        self.assertTrue(json.loads((jobs[1] / "job.json").read_text())["salvage"])
        self.assertEqual([model for phase, model in self.worker_models() if phase == "implement"],
                         ["swe-2-high", "swe-2-high"])

    @slow
    def test_rate_limit_waits_and_resumes_without_spending_retries(self):
        self.env.update(AFK_RATE_LIMIT_MARGIN="0", AFK_RATE_LIMIT_FLOOR="0")
        directory, result = self.case("rate-limit-once", "--retries", "0")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")
        self.assertIn("waiting without spending a retry", (directory / "PROGRESS.md").read_text(encoding="utf-8"))

    @slow
    def test_rate_limit_window_can_end_the_run_for_a_later_resume(self):
        self.env.update(AFK_RATE_LIMIT_MARGIN="0", AFK_RATE_LIMIT_FLOOR="0", AFK_RATE_LIMIT_MAX_WAIT="1")
        directory, result = self.case("rate-limit-always", "--retries", "0")
        self.assertEqual(result.returncode, 3, result.stdout)
        self.assertEqual(self.state(directory)["status"], "rate-limited")
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "rate-limited")
        self.assertIn("--resume", (directory / "PROGRESS.md").read_text(encoding="utf-8"))

    @slow
    def test_resume_replays_the_recorded_verdict_once(self):
        directory, first = self.case("clean")
        self.assertEqual(first.returncode, 0, first.stdout)
        second = self.run_loop("--resume", directory.name)
        self.assertEqual(second.returncode, 0, second.stdout)
        self.assertIn("replaying recorded verdict", second.stdout)
        self.assertEqual((directory / "PROGRESS.md").read_text(encoding="utf-8").count("Terminal #50: ready-for-qa"), 1)

    @slow
    def test_worker_config_permits_the_file_search_tool(self):
        directory, result = self.case("clean")
        self.assertEqual(result.returncode, 0, result.stdout)
        configs = list((self.root / ".devin" / "afk-loop").rglob("worker.json"))
        self.assertTrue(configs)
        allow = json.loads(configs[0].read_text(encoding="utf-8"))["permissions"]["allow"]
        self.assertIn("glob", allow)

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
        self.assertIn("unrelated.txt", result.stdout)
        self.assertEqual((self.root / "unrelated.txt").read_text(), "preserve")

    def test_duplicate_ticket_arguments_are_rejected(self):
        result = self.run_loop("--prepare", "--tickets", "50", "50")
        self.assertNotEqual(result.returncode, 0)

    @slow
    def test_prior_pr_prevents_new_implementation(self):
        self.env["AFK_TEST_SCENARIO"] = "prior-pr"
        directory = self.prepare()
        self.approve(directory)
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("prior work", result.stdout.lower())

    @slow
    def test_approved_adr_reference_does_not_block_implementation(self):
        commit = self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"},
                                   "docs(adr): Fetch design (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, commit)
        result = self.run_loop("--run", directory.name)
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")
        approval = json.loads((directory / "APPROVAL.json").read_text(encoding="utf-8"))
        self.assertIn("Fetch design (#50)", approval["tickets"][0]["prior_work"]["ancestry"])
        self.assertEqual(self.git("status", "--porcelain"), "")

    def test_unapproved_adr_reference_still_blocks(self):
        self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"},
                          "docs(adr): Fetch design (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.assert_prior_work_blocked(directory)

    def test_reference_approval_does_not_cover_other_adr_commits(self):
        reference = self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"},
                                      "docs(adr): Fetch design (#50)")
        self.commit_files({"docs/adr/0051-other.md": "# Other Fetch design\n"},
                          "docs(adr): other Fetch design (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, reference)
        self.assert_prior_work_blocked(directory)

    def test_mixed_documentation_and_code_commit_cannot_be_exempted(self):
        commit = self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n",
                                    "feature.cs": "// Implemented Fetch\n"},
                                   "docs: design and implementation (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, commit)
        self.assert_prior_work_blocked(directory, "may only add or modify docs/adr/*.md")

    def test_worker_prompt_markdown_cannot_be_exempted(self):
        commit = self.commit_files({".devin/skills/example/SKILL.md": "# Worker instructions\n"},
                                   "docs: worker instructions (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, commit)
        self.assert_prior_work_blocked(directory, "may only add or modify docs/adr/*.md")

    def test_reference_does_not_hide_an_implementation_in_stored_or_live_history(self):
        reference = self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"},
                                      "docs(adr): Fetch design (#50)")
        self.commit_files({"feature.cs": "// Implemented Fetch\n"}, "feat: implement Fetch (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, reference)
        self.assert_prior_work_blocked(directory)
        approval = directory / "APPROVAL.json"
        data = json.loads(approval.read_text(encoding="utf-8"))
        data["tickets"][0]["prior_work"]["ancestry"] = ""
        approval.write_text(json.dumps(data), encoding="utf-8")
        self.assert_prior_work_blocked(directory)

    def test_reference_does_not_hide_new_branches_or_prs(self):
        reference = self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"},
                                      "docs(adr): Fetch design (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, reference)
        self.git("branch", "afk/50")
        self.assert_prior_work_blocked(directory)
        self.git("branch", "-d", "afk/50")
        self.env["AFK_TEST_SCENARIO"] = "prior-pr"
        self.assert_prior_work_blocked(directory)

    def test_reference_requires_a_full_commit_id_and_reason(self):
        reference = self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"},
                                      "docs(adr): Fetch design (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, reference[:7])
        self.assert_prior_work_blocked(directory, "full commit id and reason")
        self.approve_reference(directory, reference)
        approval = directory / "APPROVAL.json"
        data = json.loads(approval.read_text(encoding="utf-8"))
        data["tickets"][0]["prior_work_references"][0]["reason"] = ""
        approval.write_text(json.dumps(data), encoding="utf-8")
        self.assert_prior_work_blocked(directory, "full commit id and reason")

    def test_merge_commit_cannot_be_exempted(self):
        self.git("checkout", "-b", "reference-side")
        self.commit_files({"docs/adr/0050-fetch.md": "# Fetch design\n"}, "docs: Fetch design")
        self.git("checkout", "milestone-3")
        self.commit_files({"docs/adr/0051-other.md": "# Other design\n"}, "docs: other design")
        self.git("merge", "--no-ff", "reference-side", "-m", "docs: merged design (#50)")
        directory = self.prepare()
        self.approve(directory, verified=False)
        self.approve_reference(directory, self.git("rev-parse", "HEAD"))
        self.assert_prior_work_blocked(directory, "merge commit cannot be approved")

    @slow
    def test_spec_drift_prevents_dispatch(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_BODY"] = "## Acceptance criteria\n- A new unapproved behavior."
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("specification changed", result.stdout.lower())

    @slow
    def test_red_baseline_prevents_implementation(self):
        directory, result = self.case("red-baseline")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.state(directory)["baseline"]["passed"])
        self.assertFalse(any("implement" in " ".join(row["args"]) for row in self.calls("devin")))

    @slow
    def test_wrong_commit_and_empty_report_are_blockers(self):
        for scenario in ("wrong-head", "empty-report", "readonly-edit"):
            with self.subTest(scenario=scenario):
                self.setUp()
                directory, result = self.case(scenario)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "blocked")

    @slow
    def test_last_review_round_cannot_launch_an_unconfirmable_repair(self):
        directory, result = self.case("persistent-finding", "--rounds", "1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("budget", self.state(directory)["tickets"]["50"]["reason"])
        jobs = list((directory / "50/jobs").iterdir())
        self.assertFalse(any(path.name.startswith("repair-") for path in jobs))

    @slow
    def test_verification_findings_get_one_fix_and_confirm_cycle(self):
        directory, result = self.case("verify-finding")
        self.assertEqual(result.returncode, 0, result.stdout)
        ticket = self.state(directory)["tickets"]["50"]
        self.assertEqual(ticket["verify_repairs"], 1)
        self.assertEqual(ticket["verification"]["round"], 2)
        self.assertEqual(len(list((directory / "50/jobs").glob("verify-*"))), 2)
        self.assertEqual([model for phase, model in self.worker_models() if phase == "repair"], ["swe-2-max"])

    @slow
    def test_zero_verification_budget_blocks_without_repair(self):
        directory, result = self.case("verify-finding", "--verify-rounds", "0")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(path.name.startswith("repair-") for path in (directory / "50/jobs").iterdir()))

    @slow
    def test_contract_nudge_preserves_stream_and_does_not_repeat_implementation(self):
        directory, result = self.case("missing-once", "--retries", "1")
        self.assertEqual(result.returncode, 0, result.stdout)
        jobs = sorted((directory / "50/jobs").glob("implement-*"))
        self.assertEqual(len(jobs), 2)
        self.assertTrue(all((path / "stream.log").is_file() for path in jobs))
        self.assertTrue(json.loads((jobs[1] / "job.json").read_text())["salvage"])

    @slow
    def test_timeout_is_bounded_and_checkpointed(self):
        directory = self.prepare()
        self.approve(directory)
        self.env.update(AFK_TEST_SCENARIO="hang", AFK_TIMEOUT="1")
        result = self.run_loop("--run", directory.name, "--retries", "0")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "blocked")
        self.assertIn("timeout", (directory / "PROGRESS.md").read_text().lower())

    @slow
    def test_utf8_bom_result_is_accepted(self):
        directory, result = self.case("bom")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.state(directory)["tickets"]["50"]["status"], "ready-for-qa")

    def test_remote_flags_need_separate_approval(self):
        directory = self.prepare()
        self.approve(directory, verified=False)
        result = self.run_loop("--run", directory.name, "--publish", "--claim")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(row["args"][:2] == ["issue", "edit"] for row in self.calls("gh")))

    @slow
    def test_prior_work_appearing_after_preparation_blocks_start(self):
        directory = self.prepare()
        self.approve(directory)
        self.env["AFK_TEST_SCENARIO"] = "prior-pr"
        result = self.run_loop("--run", directory.name)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn("prior work", result.stdout.lower())

    @slow
    def test_post_verify_review_findings_cannot_bypass_the_verify_repair_budget(self):
        directory, result = self.case("post-verify-findings")
        self.assertNotEqual(result.returncode, 0)
        repairs = list((directory / "50/jobs").glob("repair-*"))
        self.assertEqual(len(repairs), 1, result.stdout)

    @slow
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
