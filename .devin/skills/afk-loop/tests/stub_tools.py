import json
import os
from pathlib import Path
import subprocess
import sys
import time

kind, *args = sys.argv[1:]
root = Path(os.environ["AFK_TEST_ROOT"])
scenario = os.environ.get("AFK_TEST_SCENARIO", "clean")
log = root / ".scratch" / "commands.jsonl"
log.parent.mkdir(parents=True, exist_ok=True)
with log.open("a", encoding="utf-8") as stream:
    stream.write(json.dumps({"tool": kind, "args": args, "cwd": os.getcwd()}) + "\n")

if kind == "git":
    if args[0] == "push":
        sys.exit(1 if scenario == "push-fails" else 0)
    sys.exit(subprocess.call([os.environ["AFK_REAL_GIT"], *args]))

if kind == "gh":
    if args[:2] == ["repo", "view"]:
        print(json.dumps({"nameWithOwner": "example/addin"}))
    elif args[:2] == ["issue", "view"]:
        number = int(args[2])
        body = os.environ.get("AFK_TEST_BODY", "## Acceptance criteria\n- Fetch displays the part.")
        if scenario == "body-blocker" and number == 50:
            body += "\n## Blocked by\n#51"
        print(json.dumps({"number": number, "title": "Fetch preview", "body": body,
                          "comments": [], "state": "OPEN", "labels": [],
                          "assignees": [{"login": "someone-else"}] if scenario == "other-owner" else []}))
    elif args[0] == "api":
        if args[1] == "user":
            print("test-owner")
            sys.exit(0)
        if args[1].endswith("/parent"):
            print("HTTP 404: no parent", file=sys.stderr)
            sys.exit(1)
        print(json.dumps([[{"number": 51, "state": "open"}]] if scenario == "native-blocker" else [[]]))
    elif args[:2] == ["pr", "list"]:
        print(json.dumps([{"number": 99, "body": "Closes #50", "headRefName": "build/issue-50"}]
                         if scenario == "prior-pr" else []))
    elif args[:2] == ["pr", "create"]:
        print("https://github.com/example/addin/pull/99")
    elif args[:2] == ["pr", "checks"]:
        if "--json" in args:
            print(json.dumps([] if scenario == "no-ci" else [{"name": "tests", "state": "SUCCESS"}]))
            sys.exit(0)
        sys.exit(1 if scenario == "ci-fails" else 0)
    sys.exit(0)

if kind == "dotnet":
    print("Test Run Successful. Total tests: 1. Passed: 1." if args[0] == "test" else "Format verified.")
    sys.exit(1 if scenario == "red-baseline" else 0)

if kind == "devin":
    if args[:2] == ["auth", "status"]:
        sys.exit(0)
    if scenario == "dead-resume" and "-r" in args and not (root / ".scratch/dead-session").exists():
        (root / ".scratch/dead-session").write_text("gone", encoding="utf-8")
        print("No session found matching", file=sys.stderr)
        sys.exit(1)
    prompt = Path(args[args.index("--prompt-file") + 1]).read_text(encoding="utf-8")
    job = json.loads(Path(prompt.split("JOB_FILE=", 1)[1].splitlines()[0]).read_text(encoding="utf-8"))
    directory = Path(job["job_dir"])
    phase = job["phase"]
    session_id = args[args.index("-r") + 1] if "-r" in args else "stub-" + directory.name
    if phase == "policy":
        requests = [("exec", {"command": "git status --short"}),
                    ("exec", {"command": "git status && git push --dry-run"}),
                    ("write", {"file_path": job["policy_outside"], "content": "probe"})]
        for name, inputs in requests:
            payload = {"hook_event_name": "PreToolUse", "tool_name": name, "tool_input": inputs, "session_id": session_id}
            subprocess.run([sys.executable, str(Path(job["package_dir"]) / "scripts/guard.py"), str(directory / "job.json")],
                           input=json.dumps(payload), text=True, stdout=subprocess.DEVNULL, check=False)
    (directory / "session.json").write_text(json.dumps({"session_id": session_id}), encoding="utf-8")
    Path(args[args.index("--export") + 1]).write_text(json.dumps({"session_id": session_id}), encoding="utf-8")
    if scenario == "hang" and phase == "implement":
        time.sleep(30)
    if scenario == "crash" and phase == "implement":
        sys.exit(1)
    if phase == "implement" and not job.get("salvage"):
        Path("feature.txt").write_text("Fetch preview\n", encoding="utf-8")
        subprocess.check_call([os.environ["AFK_REAL_GIT"], "add", "feature.txt"])
        subprocess.check_call([os.environ["AFK_REAL_GIT"], "commit", "-m", "Implement Fetch (#50)"], stdout=subprocess.DEVNULL)
    if phase == "repair":
        with Path("feature.txt").open("a", encoding="utf-8") as stream:
            stream.write("Repair\n")
        subprocess.check_call([os.environ["AFK_REAL_GIT"], "add", "feature.txt"])
        subprocess.check_call([os.environ["AFK_REAL_GIT"], "commit", "-m", "Repair Fetch (#50)"], stdout=subprocess.DEVNULL)
    head = subprocess.check_output([os.environ["AFK_REAL_GIT"], "rev-parse", "HEAD"], text=True).strip()
    status = "clean"
    if scenario in {"review-finding", "persistent-finding", "dead-resume"} and phase in {"review-spec", "adjudicate"}:
        if job["round"] == 1 or scenario == "persistent-finding":
            status = "findings"
    if scenario in {"verify-finding", "post-verify-findings"} and phase == "verify" and job["round"] == 1:
        status = "findings"
    if scenario == "post-verify-findings" and phase in {"review-spec", "adjudicate"} and job.get("previous_verification"):
        status = "findings"
    if scenario == "decision" and phase == "implement":
        status = "blocked"
    findings = [{"id": "F1", "anchor": "feature.txt:1", "correction": "Use the approved Fetch wording"}] if status == "findings" else []
    (directory / "report.md").write_text("# Evidence\nFetch preview verified.\n", encoding="utf-8")
    (directory / "evidence.txt").write_text("Observed Fetch preview\n", encoding="utf-8")
    criteria = [{"id": item["id"], "status": "host-pending" if item["kind"] == "host" else "automated",
                 "artifact": str((directory / "evidence.txt").relative_to(Path(job["ticket_dir"]))).replace("\\", "/"),
                 "reason": "Open SolidWorks and check Fetch" if item["kind"] == "host" else ""}
                for item in job["criteria"]] if phase == "verify" else []
    result = {"version": 1, "ticket": job["ticket"], "phase": phase, "round": job["round"],
              "attempt": job["attempt"], "input_head": job["input_head"], "head": head,
              "status": status, "reason": "hitl-required: decide the seam" if status == "blocked" else "",
              "report": "report.md", "findings": findings, "criteria": criteria}
    if scenario == "wrong-head":
        result["head"] = "0" * 40
    if scenario == "empty-report":
        (directory / "report.md").write_text("", encoding="utf-8")
    if scenario == "readonly-edit" and phase == "review-spec":
        Path("feature.txt").write_text("unexpected change", encoding="utf-8")
    if scenario == "missing-verdict" and phase == "implement":
        sys.exit(0)
    if scenario == "missing-once" and phase == "implement" and not (root / ".scratch/nudged").exists():
        (root / ".scratch/nudged").write_text("nudge", encoding="utf-8")
        sys.exit(0)
    if scenario == "malformed-result":
        (directory / "result.json").write_text("{", encoding="utf-8")
        sys.exit(0)
    output = directory / "result.json"
    output.write_text(json.dumps(result), encoding="utf-8-sig" if scenario == "bom" else "utf-8")
    print("Phase complete")
    sys.exit(0)

raise SystemExit("Unknown stub tool")
