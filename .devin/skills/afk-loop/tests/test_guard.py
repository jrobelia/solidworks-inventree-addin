import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

GUARD = Path(__file__).resolve().parents[1] / "scripts" / "guard.py"


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="afk guard ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.worktree = self.root / "worktree"
        self.directory = self.root / "job"
        self.worktree.mkdir()
        self.directory.mkdir()
        self.job = self.directory / "job.json"
        self.data = {"worktree": str(self.worktree), "job_dir": str(self.directory), "writable": True}
        self.job.write_text(json.dumps(self.data), encoding="utf-8")

    def invoke(self, name, inputs, event="PreToolUse"):
        result = subprocess.run([sys.executable, str(GUARD), str(self.job)],
                                input=json.dumps({"hook_event_name": event, "tool_name": name,
                                                  "tool_input": inputs, "session_id": "exact-session"}),
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=self.worktree)
        self.assertIn(result.returncode, (0, 2), result.stderr)
        return json.loads(result.stdout)

    def test_plain_git_status_is_allowed(self):
        self.assertEqual(self.invoke("exec", {"command": "git status --short"})["decision"], "approve")

    def test_chained_remote_command_is_blocked(self):
        self.assertEqual(self.invoke("exec", {"command": "git status && git push"})["decision"], "block")

    def test_worker_cannot_change_job_authority(self):
        self.assertEqual(self.invoke("write", {"file_path": str(self.job)})["decision"], "block")

    def test_readonly_worker_can_write_report_but_not_source(self):
        self.data["writable"] = False
        self.job.write_text(json.dumps(self.data), encoding="utf-8")
        self.assertEqual(self.invoke("write", {"file_path": str(self.directory / "report.md")})["decision"], "approve")
        self.assertEqual(self.invoke("write", {"file_path": str(self.worktree / "feature.cs")})["decision"], "block")

    def test_session_start_records_only_its_own_session(self):
        self.invoke("", {}, "SessionStart")
        self.assertEqual(json.loads((self.directory / "session.json").read_text())["session_id"], "exact-session")

    def test_capture_copy_is_scoped_to_a_png_and_this_job(self):
        self.data["writable"] = False
        self.job.write_text(json.dumps(self.data), encoding="utf-8")
        source = self.worktree / "capture.png"
        source.write_bytes(b"PNG fixture")
        target = self.directory / "capture.png"
        self.assertEqual(self.invoke("exec", {"command": f'cp "{source}" "{target}"'})["decision"], "approve")
        self.assertEqual(self.invoke("exec", {"command": f'cp "{source}" "{self.root / "outside.png"}"'})["decision"], "block")

    def test_invalid_hook_arguments_fail_closed(self):
        self.assertEqual(self.invoke("exec", [])["decision"], "block")

    def test_host_build_and_output_overrides_are_blocked(self):
        commands = ['dotnet build --help', 'dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers -p:OutputPath=bin']
        for command in commands:
            with self.subTest(command=command):
                self.assertEqual(self.invoke("exec", {"command": command})["decision"], "block")

    def test_blanket_staging_and_remote_actions_are_blocked(self):
        for command in ("git add .", "git add -A", "git push", "gh pr create", "git commit --amend", "git -C other status"):
            with self.subTest(command=command):
                self.assertEqual(self.invoke("exec", {"command": command})["decision"], "block")


if __name__ == "__main__":
    unittest.main()
