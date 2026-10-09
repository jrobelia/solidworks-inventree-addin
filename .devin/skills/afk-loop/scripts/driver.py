import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import queue
import threading
import time
import signal
import math
import uuid

PACKAGE = Path(__file__).resolve().parents[1]


class Blocked(Exception):
    pass


def stamp():
    return datetime.now(timezone.utc).isoformat()


def load(path):
    with Path(path).open(encoding="utf-8-sig") as stream:
        return json.load(stream)


def save(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def digest(data):
    return hashlib.sha256(json.dumps(data, sort_keys=True, ensure_ascii=False).encode("utf-8")).hexdigest()


def tool(name):
    override = os.environ.get("AFK_" + name.upper())
    if override:
        command = json.loads(override)
        if not isinstance(command, list) or not command or any(not isinstance(part, str) for part in command):
            raise Blocked("AFK tool override must be a JSON array of command arguments")
        return command
    if name == "devin":
        if os.environ.get("DEVIN"):
            return [os.environ["DEVIN"]]
        bundled = Path(os.environ.get("LOCALAPPDATA", "")) / "Programs/Devin/resources/app/extensions/windsurf/devin/bin/devin.exe"
        if bundled.is_file():
            return [str(bundled)]
    executable = shutil.which(name)
    if not executable:
        raise Blocked("Required executable is missing: " + name)
    return [executable]


def capture(name, args, cwd, required=True):
    result = subprocess.run([*tool(name), *args], cwd=cwd, stdin=subprocess.DEVNULL,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            text=True, encoding="utf-8", errors="replace", timeout=120)
    if required and result.returncode:
        raise Blocked(name + " " + " ".join(args[:3]) + " failed: " + result.stderr.strip()[-1000:])
    return result


def git(root, *args):
    return capture("git", list(args), root).stdout.strip()


def clean(root):
    if git(root, "status", "--porcelain"):
        raise Blocked("Working tree is dirty; preserve existing work and ask the maintainer")


def issue(root, number):
    data = json.loads(capture("gh", ["issue", "view", str(number), "--json",
                                   "number,title,body,comments,state,labels,assignees"], root).stdout)
    if data.get("number") != number:
        raise Blocked("Issue response does not match the requested ticket")
    return data


def section_numbers(body, heading):
    match = re.search(r"(?im)^##\s+" + re.escape(heading) + r"\s*\r?$", body)
    if not match:
        return []
    section = re.split(r"(?m)^##\s", body[match.end():], maxsplit=1)[0]
    numbers = re.findall(r"#(\d+)|/issues/(\d+)", section)
    if section.strip() and not numbers and section.strip().lower() not in {"none", "none.", "- none"}:
        raise Blocked("Unresolved " + heading + " reference; issue numbers need confirmation")
    return sorted({int(left or right) for left, right in numbers})


def specification(root, number):
    ticket = issue(root, number)
    native = capture("gh", ["api", f"repos/{repository(root)}/issues/{number}/dependencies/blocked_by",
                             "--paginate", "--slurp"], root, required=False)
    if native.returncode and "404" not in native.stderr:
        raise Blocked("Cannot resolve native issue dependencies")
    rows = json.loads(native.stdout) if not native.returncode else []
    rows = [item for page in rows for item in (page if isinstance(page, list) else [page])]
    blockers = set(section_numbers(ticket.get("body", ""), "Blocked by"))
    blockers.update(row["number"] for row in rows)
    context = [issue(root, item) for item in sorted(blockers)]
    parents = set(section_numbers(ticket.get("body", ""), "Parent"))
    parent = capture("gh", ["api", f"repos/{repository(root)}/issues/{number}/parent"], root, required=False)
    if not parent.returncode:
        parents.add(json.loads(parent.stdout)["number"])
    elif "404" not in parent.stderr:
        raise Blocked("Cannot resolve the parent specification")
    context.extend(issue(root, item) for item in sorted(parents - blockers))
    for data in context:
        if data["number"] in blockers and data.get("state", "").upper() != "CLOSED":
            raise Blocked(f"Ticket #{number} is blocked by open issue #{data['number']}")
    if ticket.get("state", "").upper() != "OPEN":
        raise Blocked(f"Ticket #{number} is not open")
    for data in [ticket, *context]:
        data.pop("assignees", None)
    return {"ticket": ticket, "context": context, "parents": sorted(parents), "blockers": sorted(blockers)}


def repository(root):
    data = json.loads(capture("gh", ["repo", "view", "--json", "nameWithOwner"], root).stdout)
    name = data.get("nameWithOwner", "")
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", name):
        raise Blocked("Cannot resolve the GitHub repository")
    return name


def prior_work(root, number, base):
    candidates = json.loads(capture("gh", ["pr", "list", "--state", "open", "--search",
                                          f"Closes #{number}", "--json", "number,body,headRefName"], root).stdout)
    prs = [row for row in candidates if re.search(r"(?i)\b(?:closes|fixes|resolves)\s+#" + str(number) + r"\b", row.get("body", ""))]
    names = git(root, "branch", "--all", "--format=%(refname:short)").splitlines()
    pattern = r"(?:^|/)(?:(?:build|fix)/issue-" + str(number) + r"(?:$|-)|afk/" + str(number) + r"$)"
    branches = "\n".join(name for name in names if re.search(pattern, name))
    ancestry = git(root, "log", base, "--oneline", "--grep", f"#{number}")
    ancestry = "\n".join(line for line in ancestry.splitlines() if re.search(r"#" + str(number) + r"\b", line))
    return {"prs": prs, "branches": branches, "ancestry": ancestry}


def prepare(root, args):
    clean(root)
    branch = args.base or git(root, "branch", "--show-current")
    if not branch or branch.startswith("-"):
        raise Blocked("A named base branch is required")
    base = git(root, "rev-parse", "--verify", branch + "^{commit}")
    run = "afk-" + datetime.now().strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:8]
    directory = root / ".scratch/afk-loop" / run
    tickets = []
    for number in args.tickets:
        spec = specification(root, number)
        tickets.append({"number": number, "snapshot": spec, "snapshot_hash": digest(spec),
                        "prior_work": prior_work(root, number, base),
                        "seam": "", "criteria": []})
    approval = {"version": 1, "run": run, "root": str(root), "repository": repository(root),
                "base_branch": branch, "base": base, "created": stamp(), "confirmed": False,
                "policy_verified": False, "allow_publish": False, "allow_claim": False, "tickets": tickets}
    save(directory / "APPROVAL.json", approval)
    print("Prepared " + run + "; confirm playback, seams, criteria, and policy before --run")
    return 0


def file_hash(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def package_hash():
    return digest({str(path.relative_to(PACKAGE)): file_hash(path)
                   for path in sorted(PACKAGE.rglob("*"))
                   if path.is_file() and path.suffix in {".py", ".md", ".sh", ".ps1"}
                   and not {"tests", "__pycache__"}.intersection(path.relative_to(PACKAGE).parts)})


class RepoLock:
    def __init__(self, root):
        common = Path(git(root, "rev-parse", "--git-common-dir"))
        self.path = (root / common).resolve() / "afk-loop.lock"

    def __enter__(self):
        self.stream = self.path.open("a+b")
        if self.path.stat().st_size == 0:
            self.stream.write(b"0")
            self.stream.flush()
        self.stream.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.stream.close()
            raise Blocked("Another local AFK loop holds the repository lock")
        return self

    def __exit__(self, *unused):
        self.stream.close()


def terminate(process):
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
    else:
        os.killpg(process.pid, signal.SIGKILL)
    if process.poll() is None:
        process.kill()
    process.wait()


def stream_command(command, cwd, output, log):
    timeout = float(os.environ.get("AFK_TIMEOUT", "7200"))
    tick = float(os.environ.get("AFK_HEARTBEAT", "300"))
    stale = float(os.environ.get("AFK_STALE", "900"))
    if any(not math.isfinite(value) or value <= 0 for value in (timeout, tick, stale)):
        raise Blocked("Timeout, heartbeat, and stale thresholds must be positive")
    kwargs = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    process = subprocess.Popen(command, cwd=cwd, stdin=subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, **kwargs)
    lines = queue.Queue()

    def read_output():
        try:
            for line in iter(process.stdout.readline, b""):
                lines.put(line.decode("utf-8", errors="replace"))
        finally:
            lines.put(None)

    reader = threading.Thread(target=read_output, daemon=True)
    reader.start()
    started = last = heartbeat = time.monotonic()
    done = timed_out = False
    try:
        with Path(output).open("x", encoding="utf-8", newline="\n") as stream:
            while not done:
                now = time.monotonic()
                if now - started >= timeout and process.poll() is None:
                    timed_out = True
                    log("Dispatch timeout; terminating only the spawned process tree")
                    terminate(process)
                if now - heartbeat >= tick:
                    log("STALE" if now - last >= stale else "alive")
                    heartbeat = now
                try:
                    line = lines.get(timeout=min(tick, 0.2))
                except queue.Empty:
                    continue
                if line is None:
                    done = True
                else:
                    last = time.monotonic()
                    stream.write(line)
                    stream.flush()
                    print(line, end="", flush=True)
        return 124 if timed_out else process.wait()
    finally:
        terminate(process)
        reader.join(timeout=2)
        process.stdout.close()


def artifact(directory, name):
    if not isinstance(name, str) or not name or Path(name).is_absolute():
        raise Blocked("Artifact must be a relative path")
    path = (directory / name).resolve()
    if not path.is_relative_to(directory.resolve()) or not path.is_file() or not path.stat().st_size:
        raise Blocked("Missing, empty, or out-of-scope artifact: " + name)
    return path


class Loop:
    def __init__(self, root, args):
        self.root, self.args = root, args
        run = args.run or args.resume or args.smoke
        if not re.fullmatch(r"afk-[\w-]+", run):
            raise Blocked("Invalid run identifier")
        self.directory = root / ".scratch/afk-loop" / run
        self.approval = load(self.directory / "APPROVAL.json")
        approval = self.approval
        if approval.get("version") != 1 or approval.get("run") != run or Path(approval["root"]).resolve() != root:
            raise Blocked("Approval does not belong to this run and checkout")
        if not args.smoke and (approval.get("confirmed") is not True or approval.get("policy_verified") is not True):
            raise Blocked("Playback, seam, acceptance mapping, and worker policy need confirmation")
        if args.publish and approval.get("allow_publish") is not True:
            raise Blocked("Publication needs explicit approval")
        if args.claim and approval.get("allow_claim") is not True:
            raise Blocked("Ticket assignment needs explicit approval")
        if not args.smoke:
            policy = load(self.directory / "POLICY.json")
            events = Path(policy["events"]).resolve()
            if policy.get("passed") is not True or policy.get("package_hash") != package_hash() or policy.get("base") != approval["base"]:
                raise Blocked("Actual worker policy probe is missing or stale")
            if not events.is_relative_to(self.directory) or not events.is_file() or file_hash(events) != policy["events_hash"]:
                raise Blocked("Worker policy evidence is missing or changed")
        clean(root)
        if git(root, "branch", "--show-current") != approval["base_branch"] or git(root, "rev-parse", "HEAD") != approval["base"]:
            raise Blocked("Canonical base branch or commit changed; prepare a new approved run")
        if repository(root) != approval["repository"]:
            raise Blocked("GitHub repository changed")
        for ticket in approval["tickets"]:
            if args.smoke:
                continue
            criteria = ticket.get("criteria", [])
            if not ticket.get("seam", "").strip() or not criteria:
                raise Blocked("Each ticket needs an approved seam and acceptance-criterion mapping")
            ids = [row.get("id") for row in criteria]
            if any(not isinstance(value, str) or not value for value in ids) or len(set(ids)) != len(ids):
                raise Blocked("Acceptance criteria need distinct non-empty ids")
            if any(not row.get("text") or row.get("kind") not in {"automated", "visual", "host", "not-applicable"} for row in criteria):
                raise Blocked("Invalid acceptance-criterion mapping")
            if digest(specification(root, ticket["number"])) != ticket["snapshot_hash"]:
                raise Blocked("Ticket or parent specification changed; reconfirm the playback")
            assignees = issue(root, ticket["number"]).get("assignees", [])
            if assignees:
                me = capture("gh", ["api", "user", "--jq", ".login"], root).stdout.strip()
                if any(owner.get("login") != me for owner in assignees):
                    raise Blocked("Ticket already has another owner; maintainer disposition is required")
            prior = ticket["prior_work"]
            if any(prior.values()) or (not args.resume and any(prior_work(root, ticket["number"], approval["base"]).values())):
                raise Blocked("Prior work requires a maintainer disposition before starting a new run")
        self.status_path = self.directory / ("POLICY_STATUS.json" if args.smoke else "STATUS.json")
        path = self.status_path
        if args.smoke and (self.directory / "STATUS.json").exists():
            raise Blocked("Policy changes cannot modify an already-started approval; prepare a new run")
        if args.smoke:
            self.state = load(path) if path.exists() else {"version": 1, "run": run, "tickets": {}}
            self.state.update(approval_hash=digest(approval), package_hash=package_hash(), budgets=self.budgets())
            self.checkpoint()
        elif path.exists():
            if not args.resume:
                raise Blocked("Run already started; use --resume")
            self.state = load(path)
            if self.state["approval_hash"] != digest(approval) or self.state["package_hash"] != package_hash():
                raise Blocked("Approval or AFK package changed; recorded verdicts cannot be replayed")
            if self.state["budgets"] != self.budgets():
                raise Blocked("Resume must preserve the original repair and retry budgets")
            if self.state.get("effects") != {"publish": args.publish, "claim": args.claim}:
                raise Blocked("Resume must preserve the explicitly approved effect switches")
        else:
            if args.resume:
                raise Blocked("No checkpoint exists to resume")
            self.state = {"version": 1, "run": run, "approval_hash": digest(approval),
                          "package_hash": package_hash(), "budgets": self.budgets(),
                          "started": stamp(), "tickets": {}, "effects": {"publish": args.publish, "claim": args.claim}}
            self.checkpoint()

    def recorded_evidence(self):
        gates = [self.state.get("baseline")]
        for ticket in self.state["tickets"].values():
            gates.append(ticket.get("checks"))
            reports = list(ticket.get("reviews", {}).values())
            if ticket.get("verification"):
                reports.append(ticket["verification"])
            for report in reports:
                paths = [(report["report_path"], report["report_hash"])]
                paths.extend((row["artifact_path"], row["artifact_hash"]) for row in report.get("criteria", []) if row.get("artifact_path"))
                for name, expected in paths:
                    path = Path(name).resolve()
                    if not path.is_relative_to(self.directory) or not path.is_file() or file_hash(path) != expected:
                        raise Blocked("Recorded evidence is missing or changed; its verdict cannot be replayed")
            if ticket.get("phase") in {"verify", "done"}:
                current = ticket["head"]
                if not ticket.get("checks", {}).get("passed") or ticket["checks"]["head"] != current:
                    raise Blocked("Recorded deterministic evidence does not match the current commit")
                if any(report.get("head") != current for report in ticket.get("reviews", {}).values()):
                    raise Blocked("Recorded review evidence is stale")
        for gate in filter(None, gates):
            for row in gate["logs"]:
                path = Path(row["log"]).resolve()
                if not path.is_relative_to(self.directory) or not path.is_file() or file_hash(path) != row["hash"]:
                    raise Blocked("Recorded deterministic evidence is missing or changed")
            passed = len(gate["logs"]) == 2 and all(row["exit_code"] == 0 for row in gate["logs"])
            if gate["passed"] is not passed:
                raise Blocked("Recorded deterministic verdict contradicts its logs")

    def budgets(self):
        return {"rounds": self.args.rounds, "verify_rounds": self.args.verify_rounds, "retries": self.args.retries}

    def checkpoint(self):
        self.state["updated"] = stamp()
        save(self.status_path, self.state)

    def log(self, message):
        line = stamp() + " " + message
        print(line, flush=True)
        with (self.directory / "PROGRESS.md").open("a", encoding="utf-8", newline="\n") as stream:
            stream.write(line + "\n")

    def check(self, cwd, directory, head):
        clean(cwd)
        directory.mkdir(parents=True, exist_ok=False)
        commands = [["test", "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj", "--disable-build-servers"],
                    ["format", "Solidworks Inventree Add-In.sln", "--verify-no-changes"]]
        rows = []
        for command in commands:
            path = directory / (command[0] + ".log")
            code = stream_command([*tool("dotnet"), *command], cwd, path, self.log)
            rows.append({"command": ["dotnet", *command], "exit_code": code, "log": str(path), "hash": file_hash(path)})
            if code:
                break
        clean(cwd)
        if git(cwd, "rev-parse", "HEAD") != head:
            raise Blocked("Commit moved during deterministic verification")
        return {"head": head, "passed": len(rows) == 2 and all(row["exit_code"] == 0 for row in rows), "logs": rows}

    def worktree(self, approved, ticket):
        path = Path(ticket["worktree"]).resolve()
        expected = (self.root / ".worktrees/afk-loop" / self.state["run"] / ("issue-" + str(approved["number"]))).resolve()
        if path != expected or ticket["branch"] != "build/issue-" + str(approved["number"]):
            raise Blocked("Checkpoint points outside its owned ticket worktree")
        if not path.exists():
            existing = git(self.root, "branch", "--all", "--list", ticket["branch"], "remotes/origin/" + ticket["branch"])
            if existing:
                raise Blocked("Ticket branch already exists; preserve prior work")
            git(self.root, "worktree", "add", "-b", ticket["branch"], str(path), self.approval["base"])
            override = self.root / "Directory.Build.props.user"
            if override.exists():
                shutil.copy2(override, path / override.name)
        if git(path, "branch", "--show-current") != ticket["branch"]:
            raise Blocked("Worktree is on the wrong branch")
        head = git(path, "rev-parse", "HEAD")
        pending = ticket.get("active", {}).get("phase") in {"implement", "repair"}
        if head != ticket["head"] and not pending:
            raise Blocked("Worktree moved outside a recorded implementation or repair dispatch")
        if not pending:
            clean(path)
        if capture("git", ["merge-base", "--is-ancestor", self.approval["base"], head], path, required=False).returncode:
            raise Blocked("Worktree no longer descends from the approved base")
        return path

    def config(self, job, writable):
        worktree, directory = Path(job["worktree"]), Path(job["job_dir"])
        runtime = self.root / ".devin/afk-loop" / self.state["run"] / str(job["ticket"]) / directory.name
        runtime.mkdir(parents=True, exist_ok=True)
        config_path = runtime / "worker.json"
        guard_command = "python " + subprocess.list2cmdline([str(PACKAGE / "scripts/guard.py"), str(directory / "job.json")])
        allow = ["Write(" + directory.as_posix() + "/**)", "Exec(git status)", "Exec(git diff)",
                 "Exec(git log)", "Exec(git show)", "Exec(git rev-parse)", "Exec(git ls-files)",
                 "Exec(dotnet test)", "Exec(dotnet format)", "Exec(cp)", "skill", "todo_write", "get_output"]
        if writable:
            allow += ["Write(" + worktree.as_posix() + "/**)", "Exec(git add)", "Exec(git commit)"]
        deny = ["run_subagent", "read_subagent", "code_search", "Fetch(*)", "Exec(gh)", "Exec(git push)",
                "Exec(git -C)", "Exec(git config)", "Exec(git reset)", "Exec(git clean)", "Exec(git restore)",
                "Exec(git checkout)", "Exec(git rebase)", "Exec(git merge)", "Exec(git commit --amend)",
                "Exec(dotnet build)", "Exec(dotnet run)", "Exec(rm)", "Exec(powershell)", "Exec(pwsh)",
                "Exec(cmd)", "Exec(bash)", "Exec(python)", "Read(**/inventree_servers.json)", "Read(**/.env*)",
                "Write(**/.git/**)", "Write(**/.devin/**)", "Write(**/.agents/**)", "Write(**/.env*)"]
        config = {"subagents_enabled": False, "permissions": {"allow": allow, "deny": deny},
                  "read_config_from": {"agents_standard": True, "cursor": False, "windsurf": False,
                                       "claude": False, "copilot": False, "opencode": False, "zed": False},
                  "hooks": {event: [{"matcher": "", "hooks": [{"type": "command", "command": guard_command, "timeout": 10}]}]
                            for event in ("PreToolUse", "SessionStart")}}
        save(config_path, config)
        local = worktree / ".devin/config.local.json"
        owner = worktree / ".devin/afk-loop/owner.json"
        if local.exists():
            old = load(owner) if owner.exists() else {}
            if old.get("run") != self.state["run"] or old.get("config_hash") != file_hash(local):
                raise Blocked("Existing worktree permissions need maintainer approval; they will not be overwritten")
        save(local, {"permissions": config["permissions"], "read_config_from": config["read_config_from"]})
        save(owner, {"run": self.state["run"], "config_hash": file_hash(local)})
        return config_path

    def validate(self, job, result, ticket):
        if not isinstance(result, dict):
            raise Blocked("Worker result must be a JSON object")
        for key in ("version", "ticket", "phase", "round", "attempt", "input_head"):
            if type(result.get(key)) is not type(job[key]) or result.get(key) != job[key]:
                raise Blocked("Worker result has a mismatched " + key)
        if result.get("status") not in {"clean", "findings", "blocked"}:
            raise Blocked("Worker result has an invalid status")
        cwd = Path(job["worktree"])
        head = git(cwd, "rev-parse", "HEAD")
        clean(cwd)
        if result.get("head") != head or (not job["writable"] and head != job["input_head"]):
            raise Blocked("Worker result does not match the exercised commit")
        if job["phase"] in {"implement", "repair"} and result["status"] == "clean" and head == self.approval["base"]:
            raise Blocked("Implementation did not produce a committed change")
        report = artifact(Path(job["job_dir"]), result.get("report"))
        result["report_path"], result["report_hash"] = str(report), file_hash(report)
        if not isinstance(result.get("findings"), list) or not isinstance(result.get("criteria"), list):
            raise Blocked("Worker findings and criteria must be arrays")
        if result["status"] == "clean" and result["findings"]:
            raise Blocked("Clean verdict cannot contain open findings")
        if job["phase"] in {"implement", "repair"} and result["status"] == "findings":
            raise Blocked("Implementation and repair return clean committed work or a named blocker")
        if result["status"] == "findings" and not result["findings"]:
            raise Blocked("Findings verdict has no work order")
        if any(not row.get("id") or not row.get("anchor") or not row.get("correction") for row in result["findings"]):
            raise Blocked("Findings require ids, anchors, and settled corrections")
        if result["status"] == "blocked":
            raise Blocked(result.get("reason") or "Worker reported an unnamed blocker")
        if job["phase"] == "verify":
            expected = {row["id"]: row for row in job["criteria"]}
            actual = {row.get("id"): row for row in result["criteria"]}
            if set(expected) != set(actual) or len(actual) != len(result["criteria"]):
                raise Blocked("Verification did not account for every approved acceptance criterion")
            for identity, row in actual.items():
                kind = expected[identity]["kind"]
                allowed = {"host": {"host-pending"}, "not-applicable": {"not-applicable"},
                           "automated": {"automated", "not-proven"}, "visual": {"visual", "not-proven"}}[kind]
                if row.get("status") not in allowed:
                    raise Blocked("Verification changed an acceptance criterion's evidence requirement")
                if row["status"] in {"automated", "visual"}:
                    evidence = artifact(Path(job["ticket_dir"]), row.get("artifact"))
                    row["artifact_path"], row["artifact_hash"] = str(evidence), file_hash(evidence)
                    if kind == "visual" and evidence.suffix.lower() != ".png":
                        raise Blocked("Visual verification requires a PNG capture")
                elif not row.get("reason"):
                    raise Blocked("Unexercised criterion needs a reason and QA handoff")
                if row["status"] == "not-proven" and result["status"] != "findings":
                    raise Blocked("Unproven automated evidence cannot earn a clean verdict")
        return result

    def dispatch(self, approved, ticket, phase, round_number):
        cwd = Path(ticket["worktree"])
        active = ticket.get("active")
        salvage = bool(active) and phase in {"implement", "repair"} and (
            git(cwd, "rev-parse", "HEAD") != active["head"] or bool(git(cwd, "status", "--porcelain"))
            or Path(active["job"]).with_name("report.md").exists())
        resume = ticket.get("sessions", {}).get(phase) if phase not in {"implement", "repair"} else None
        delays = json.loads(os.environ.get("AFK_RETRY_DELAYS", "[45,120,300]"))
        if not isinstance(delays, list) or not delays or any(type(value) not in (int, float) or not math.isfinite(value) or value < 0 for value in delays):
            raise Blocked("Invalid retry delay schedule")
        retry = 0
        while retry <= self.args.retries:
            sequence = ticket.get("dispatches", 0) + 1
            ticket["dispatches"] = sequence
            directory = self.directory / str(approved["number"]) / "jobs" / f"{phase}-r{round_number}-a{sequence}"
            directory.mkdir(parents=True, exist_ok=False)
            head = git(cwd, "rev-parse", "HEAD")
            job = {"version": 1, "ticket": approved["number"], "phase": phase, "round": round_number,
                   "attempt": sequence, "input_head": head, "base": self.approval["base"], "worktree": str(cwd),
                   "job_dir": str(directory), "ticket_dir": str(directory.parent.parent),
                   "package_dir": str(PACKAGE), "contracts": str(PACKAGE / "contracts.md"),
                   "criteria": approved["criteria"], "seam": approved["seam"], "snapshot": approved["snapshot"],
                   "findings": ticket.get("findings", []), "checks": ticket.get("checks"),
                   "reviews": ticket.get("reviews", {}), "previous_verification": ticket.get("verification"),
                   "policy_outside": str(self.directory / "outside-policy-probe.txt"),
                   "salvage": salvage, "writable": phase in {"implement", "repair", "policy"} and not salvage}
            save(directory / "job.json", job)
            ticket["active"] = {"phase": phase, "job": str(directory / "job.json"), "head": head}
            self.checkpoint()
            template = "review" if phase.startswith("review-") else phase
            prompt = "JOB_FILE=" + str(directory / "job.json") + "\n\n" + (PACKAGE / "prompts" / (template + ".md")).read_text(encoding="utf-8")
            if salvage:
                prompt += "\nSALVAGE: inspect existing work and evidence only. Persist a supported verdict; do not repeat or revise implementation. Insufficient evidence is blocked.\n"
            (directory / "prompt.md").write_text(prompt, encoding="utf-8")
            config = self.config(job, job["writable"])
            model = os.environ.get("MODEL_REVIEW" if phase.startswith("review-") or phase == "adjudicate" else "MODEL_IMPL", "swe-2-max")
            if phase == "repair" and ticket["round"] >= self.args.rounds - 1:
                model = os.environ.get("MODEL_REPAIR_FINAL", "swe-2-max")
            command = [*tool("devin"), "-p", "--model", model, "--permission-mode", "auto", "--respect-workspace-trust", "false",
                       "--config", str(config), "--export", str(directory / "transcript.json"), "--prompt-file", str(directory / "prompt.md")]
            if resume:
                command += ["-r", resume]
            self.log(f"#{approved['number']} {phase} r{round_number} dispatch {sequence}")
            code = stream_command(command, cwd, directory / "stream.log", self.log)
            session = directory / "session.json"
            sid = load(session).get("session_id") if session.exists() else None
            if sid:
                ticket.setdefault("sessions", {})[phase] = sid
            result_path = directory / "result.json"
            if result_path.exists():
                try:
                    result = self.validate(job, load(result_path), ticket)
                except (ValueError, KeyError, TypeError, AttributeError, OSError) as error:
                    raise Blocked("Invalid worker result: " + str(error))
                ticket.pop("active", None)
                ticket["head"] = result["head"]
                self.checkpoint()
                return result
            if resume and "No session found matching" in (directory / "stream.log").read_text(encoding="utf-8"):
                self.log("Expired review/verification session; falling back to fresh context without spending a retry")
                ticket.setdefault("sessions", {}).pop(phase, None)
                resume = None
                self.checkpoint()
                continue
            if retry == self.args.retries:
                raise Blocked("Dispatch exhausted retries without a validated verdict; partial work is retained")
            moved = git(cwd, "rev-parse", "HEAD") != head or bool(git(cwd, "status", "--porcelain"))
            salvage = moved or (directory / "report.md").exists()
            resume = sid if code == 0 and sid else None
            if resume:
                salvage = True
            self.log(f"Verdictless dispatch exited {code}; {'salvage' if salvage else 'retry'} without deleting partial work")
            self.checkpoint()
            time.sleep(delays[min(retry, len(delays) - 1)])
            retry += 1
        raise Blocked("No dispatch verdict")

    def smoke(self):
        if os.environ.get("DEVIN_API_KEY"):
            raise Blocked("Resolve terminal authentication without DEVIN_API_KEY before the policy probe")
        capture("devin", ["auth", "status"], self.root)
        self.approval["policy_verified"] = False
        save(self.directory / "APPROVAL.json", self.approval)
        cwd = self.root / ".worktrees/afk-loop" / self.state["run"] / ("policy-" + uuid.uuid4().hex[:8])
        git(self.root, "worktree", "add", "--detach", str(cwd), self.approval["base"])
        previous = self.state["tickets"].get("0", {})
        ticket = {"worktree": str(cwd), "head": self.approval["base"], "round": 1, "dispatches": previous.get("dispatches", 0)}
        self.state["tickets"]["0"] = ticket
        approved = {"number": 0, "criteria": [], "seam": "Neutral policy probe; no ticket implementation", "snapshot": {}}
        result = self.dispatch(approved, ticket, "policy", 1)
        directory = Path(result["report_path"]).parent
        events_path = directory / "policy-events.jsonl"
        if not events_path.exists():
            raise Blocked("Actual CLI did not provide protected hook evidence; worker policy is not verified")
        events = [json.loads(line) for line in events_path.read_text(encoding="utf-8").splitlines()]
        expected = {"allowed-status": "approve", "chained-command": "block", "outside-write": "block"}
        for check, decision in expected.items():
            if not any(row.get("check") == check and row.get("decision") == decision
                       and row.get("guard_hash") == file_hash(PACKAGE / "scripts/guard.py") for row in events):
                raise Blocked("Actual worker policy did not demonstrate " + check)
        sid = ticket.get("sessions", {}).get("policy")
        if not sid or any(row.get("session_id") != sid for row in events):
            raise Blocked("Policy probe could not establish exact child-session attribution")
        if Path(self.directory / "outside-policy-probe.txt").exists() or git(cwd, "rev-parse", "HEAD") != self.approval["base"]:
            raise Blocked("Policy probe escaped its permitted effects")
        clean(cwd)
        save(self.directory / "POLICY.json", {"version": 1, "passed": True, "package_hash": package_hash(),
                                             "base": self.approval["base"], "events": str(events_path),
                                             "events_hash": file_hash(events_path), "report": result["report_path"], "checked": stamp()})
        self.approval["policy_verified"] = True
        save(self.directory / "APPROVAL.json", self.approval)
        self.state["status"] = "policy-verified"
        self.checkpoint()
        self.log("Worker policy verified in a separate worktree; ticket implementation has not started")
        return 0

    def publish(self, approved, ticket):
        cwd = Path(ticket["worktree"])
        clean(cwd)
        clean(self.root)
        if git(self.root, "rev-parse", "HEAD") != self.approval["base"] or digest(specification(self.root, approved["number"])) != approved["snapshot_hash"]:
            raise Blocked("Base or specification changed before publication")
        self.recorded_evidence()
        if git(cwd, "rev-parse", "HEAD") != ticket["head"]:
            raise Blocked("Commit moved before publication")
        body = ["## Summary", "", approved["snapshot"]["ticket"]["title"], "", f"Closes #{approved['number']}",
                "", "Automated checks and independent reviews passed. SolidWorks QA is pending.",
                "", "## Acceptance criteria", "", "| Criterion | Evidence | Result |", "| --- | --- | --- |"]
        for row in approved["criteria"]:
            observed = next(item for item in ticket["verification"]["criteria"] if item["id"] == row["id"])
            text = row["text"].replace("|", "\\|").replace("\n", " ")
            evidence = observed.get("artifact") or observed.get("reason", "")
            body.append(f"| {text} | `{evidence.replace('|', ' / ').replace(chr(10), ' ')}` | {observed['status']} |")
        parents = approved["snapshot"]["parents"]
        body.extend("\nPart of #" + str(number) for number in parents)
        body += ["", "## Build and test commands", ""]
        body += ["- `" + " ".join(row["command"]) + "`" for row in ticket["checks"]["logs"]]
        body += ["", "### Review notes", ""]
        for phase in ("review-spec", "review-standards", "adjudicate"):
            body += [Path(ticket["reviews"][phase]["report_path"]).read_text(encoding="utf-8"), ""]
        body += ["### Deferred and follow-up issues", "", "See the adjudication notes above; no unapproved deferral is implied.",
                 "", "## GUI flows and edge cases", "", Path(ticket["verification"]["report_path"]).read_text(encoding="utf-8"),
                 "", "Evidence artifacts remain local; no transcript, image, or log upload is automatic.", "",
                 "Run /qa on this branch. /qa will take the PR out of draft if QA passes and ask whether to merge.",
                 "", "Generated with [Devin](https://devin.ai)"]
        path = self.directory / str(approved["number"]) / "pr-body.md"
        path.write_text("\n".join(body), encoding="utf-8")
        ticket["publication"] = "pushing"
        self.checkpoint()
        pushed = capture("git", ["push", "-u", "origin", ticket["branch"]], cwd, required=False)
        if pushed.returncode:
            ticket["publication"] = "failed"
            raise Blocked("Publication failed: push; local work and evidence are retained")
        ticket["publication"] = "branch-pushed"
        self.checkpoint()
        existing = json.loads(capture("gh", ["pr", "list", "--state", "open", "--head", ticket["branch"],
                                            "--base", self.approval["base_branch"], "--json", "number,url"], cwd).stdout)
        if len(existing) > 1:
            ticket["publication"] = "failed"
            raise Blocked("Multiple PRs match this branch; maintainer disposition required")
        if existing:
            reference = str(existing[0]["number"])
            result = capture("gh", ["pr", "edit", reference, "--body-file", str(path)], cwd, required=False)
        else:
            result = capture("gh", ["pr", "create", "--draft", "--head", ticket["branch"], "--base", self.approval["base_branch"],
                                     "--title", approved["snapshot"]["ticket"]["title"], "--body-file", str(path)], cwd, required=False)
            reference = result.stdout.strip()
        if result.returncode:
            ticket["publication"] = "failed"
            raise Blocked("Publication failed: PR body/create; branch was pushed")
        ticket["pr"], ticket["publication"], ticket["ci"] = reference, "draft-created", "pending"
        self.checkpoint()
        checks = json.loads(capture("gh", ["pr", "checks", reference, "--json", "name,state"], cwd).stdout)
        if not checks:
            raise Blocked("Draft PR exists, but CI checks have not appeared; CI is pending")
        code = stream_command([*tool("gh"), "pr", "checks", reference, "--watch", "--fail-fast"],
                              cwd, path.with_name("ci-" + uuid.uuid4().hex[:8] + ".log"), self.log)
        ticket["ci"] = "green" if code == 0 else "pending" if code == 124 else "failed"
        self.checkpoint()
        if code:
            raise Blocked("Draft PR exists, but required CI is " + ticket["ci"])

    def ticket(self, approved):
        number = str(approved["number"])
        ticket = self.state["tickets"].setdefault(number, {"branch": "build/issue-" + number,
                    "worktree": str(self.root / ".worktrees/afk-loop" / self.state["run"] / ("issue-" + number)),
                    "head": self.approval["base"], "phase": "implement", "round": 1, "verify_round": 1,
                    "verify_repairs": 0, "status": "running", "publication": "not-requested", "claim": self.state.get("claims", {}).get(number, "not-requested")})
        self.checkpoint()
        try:
            cwd = self.worktree(approved, ticket)
            while ticket["phase"] != "done":
                phase, round_number = ticket["phase"], ticket["round"]
                ticket["status"], ticket["reason"] = "running", ""
                self.checkpoint()
                if phase in {"implement", "repair"}:
                    self.dispatch(approved, ticket, phase, round_number)
                    if phase == "repair":
                        ticket["round"] = 1 if ticket.pop("repair_source", "review") == "verify" else round_number + 1
                    ticket["reviews"] = {}
                    ticket.pop("checks", None)
                    ticket["phase"] = "checks"
                elif phase == "checks":
                    name = "gate-" + uuid.uuid4().hex[:8]
                    ticket["checks"] = self.check(cwd, self.directory / number / "checks" / name, ticket["head"])
                    ticket["phase"] = "review-spec"
                elif phase in {"review-spec", "review-standards", "adjudicate"}:
                    result = self.dispatch(approved, ticket, phase, round_number)
                    ticket.setdefault("reviews", {})[phase] = result
                    if phase != "adjudicate":
                        ticket["phase"] = "review-standards" if phase == "review-spec" else "adjudicate"
                    elif result["status"] == "clean":
                        if not ticket["checks"]["passed"]:
                            raise Blocked("A red deterministic gate cannot earn a clean adjudication")
                        ticket["findings"] = []
                        ticket["phase"] = "verify"
                    else:
                        ticket["findings"] = result["findings"]
                        if ticket.get("post_verify"):
                            if ticket["verify_repairs"] >= self.args.verify_rounds:
                                raise Blocked("Post-verification repair budget exhausted during confirming review")
                            ticket["verify_repairs"] += 1
                            ticket["repair_source"] = "verify"
                        elif round_number >= self.args.rounds:
                            raise Blocked("Review/repair budget exhausted; a repair must have a confirming review")
                        ticket["phase"] = "repair"
                elif phase == "verify":
                    result = self.dispatch(approved, ticket, phase, ticket["verify_round"])
                    ticket["verification"] = result
                    if result["status"] == "clean":
                        ticket["phase"], ticket["status"] = "done", "ready-for-qa"
                    else:
                        if ticket["verify_repairs"] >= self.args.verify_rounds:
                            raise Blocked("Verification repair budget exhausted")
                        ticket["post_verify"] = True
                        ticket["verify_repairs"] += 1
                        ticket["verify_round"] += 1
                        ticket["findings"] = result["findings"]
                        ticket["repair_source"], ticket["phase"] = "verify", "repair"
                else:
                    raise Blocked("Unknown checkpoint phase")
                self.checkpoint()
            if self.args.publish and ticket.get("ci") != "green":
                self.publish(approved, ticket)
                ticket["status"] = "ready-for-qa"
                self.checkpoint()
        except Blocked as error:
            ticket["status"], ticket["reason"] = "blocked", str(error)
            self.checkpoint()
            self.log(f"Terminal #{number}: blocked; claim {ticket['claim']}; {error}; handoff /build-hitl")
            return False
        ticket["status"], ticket["reason"] = "ready-for-qa", ""
        self.checkpoint()
        self.log(f"Terminal #{number}: ready-for-qa @ {ticket['head']}; run /qa; claim {ticket['claim']}")
        return True

    def run(self):
        if os.environ.get("DEVIN_API_KEY"):
            raise Blocked("DEVIN_API_KEY overrides terminal login; authenticate in your own terminal without that override")
        self.state["status"] = "running"
        self.checkpoint()
        capture("devin", ["auth", "status"], self.root)
        capture("gh", ["auth", "status"], self.root)
        if self.args.claim:
            for ticket in self.approval["tickets"]:
                number = str(ticket["number"])
                if self.state.setdefault("claims", {}).get(number) != "retained":
                    capture("gh", ["issue", "edit", number, "--add-assignee", "@me"], self.root)
                    self.state["claims"][number] = "retained"
                    self.checkpoint()
                    self.log(f"Claim #{number}: retained; assignment is an ownership record, not an atomic cross-machine lock")
        self.recorded_evidence()
        baseline = self.state.get("baseline")
        if not baseline:
            self.state["baseline"] = self.check(self.root, self.directory / ("baseline-" + uuid.uuid4().hex[:8]), self.approval["base"])
            self.checkpoint()
        if not self.state["baseline"]["passed"]:
            raise Blocked("Red baseline; implementation cannot start")
        outcomes = [self.ticket(ticket) for ticket in self.approval["tickets"]]
        clean(self.root)
        if git(self.root, "rev-parse", "HEAD") != self.approval["base"]:
            raise Blocked("Canonical base moved during the run")
        self.state["ended"] = stamp()
        self.state["status"] = "ready-for-qa" if all(outcomes) else "blocked"
        self.checkpoint()
        return 0 if all(outcomes) else 2


def arguments():
    parser = argparse.ArgumentParser(description="Local AFK ticket loop; successful builds still require SolidWorks QA")
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--tickets", nargs="+", type=int)
    parser.add_argument("--base")
    parser.add_argument("--run")
    parser.add_argument("--resume")
    parser.add_argument("--smoke")
    parser.add_argument("--publish", action="store_true")
    parser.add_argument("--claim", action="store_true")
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--verify-rounds", type=int, default=1)
    parser.add_argument("--retries", type=int, default=3)
    args = parser.parse_args()
    if sum(bool(value) for value in (args.prepare, args.run, args.resume, args.smoke)) != 1:
        parser.error("Choose exactly one of --prepare, --smoke, --run, or --resume")
    if (args.prepare or args.smoke) and (args.publish or args.claim):
        parser.error("Preparation and policy probes cannot approve remote effects")
    if args.prepare and (not args.tickets or any(number <= 0 for number in args.tickets) or len(set(args.tickets)) != len(args.tickets)):
        parser.error("--prepare requires distinct positive --tickets")
    if not 1 <= args.rounds <= 10 or not 0 <= args.verify_rounds <= 10 or not 0 <= args.retries <= 10:
        parser.error("Invalid review, verification, or retry budget")
    return args


def main():
    loop = None
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    try:
        args = arguments()
        root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel")).resolve()
        if args.prepare:
            return prepare(root, args)
        with RepoLock(root):
            loop = Loop(root, args)
            return loop.smoke() if args.smoke else loop.run()
    except KeyboardInterrupt:
        if loop:
            loop.state.update(status="interrupted", reason="User interrupted the run; partial work and active dispatch are retained")
            loop.checkpoint()
        print("INTERRUPTED: use --resume; partial work is retained", file=sys.stderr)
        return 130
    except (Blocked, OSError, ValueError, KeyError, TypeError, AttributeError, subprocess.TimeoutExpired) as error:
        print("BLOCKED: " + str(error), file=sys.stderr)
        if loop:
            loop.state.update(status="blocked", reason=str(error))
            loop.checkpoint()
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
