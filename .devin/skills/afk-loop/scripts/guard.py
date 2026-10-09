import json
from pathlib import Path
import re
import shlex
import sys

from driver import file_hash, load, save


def within(path, root):
    return path.resolve().is_relative_to(root.resolve())


def restricted(path):
    parts = {part.lower() for part in path.parts}
    return bool(parts.intersection({".git", ".devin", ".agents"})) or path.name.lower().startswith(".env") or path.name.lower() == "inventree_servers.json"


def decision(job, event):
    directory, worktree = Path(job["job_dir"]), Path(job["worktree"])
    if event.get("hook_event_name") == "SessionStart":
        sid = event.get("session_id")
        if not isinstance(sid, str) or not re.fullmatch(r"[\w-]+", sid):
            raise ValueError("SessionStart did not supply a valid session id")
        save(directory / "session.json", {"session_id": sid})
        return {"decision": "approve"}
    name = event.get("tool_name", "").rsplit(".", 1)[-1].lower()
    inputs = event.get("tool_input", {})
    if name in {"edit", "write", "notebook_edit"}:
        raw = inputs.get("file_path") or inputs.get("notebook_path")
        if not raw:
            raise ValueError("Write tool did not supply its path")
        path = Path(raw)
        path = (worktree / path).resolve() if not path.is_absolute() else path.resolve()
        metadata = {"job.json", "prompt.md", "session.json", "stream.log", "transcript.json", "policy-events.jsonl"}
        if within(path, directory) and path.name not in metadata:
            return {"decision": "approve"}
        if job["writable"] and within(path, worktree) and not restricted(path):
            return {"decision": "approve"}
        raise ValueError("Worker writes are limited to approved source and this job's evidence")
    if name == "exec":
        command = inputs.get("command", "")
        if not command or re.search(r"[\r\n;&|`<>$]", command):
            raise ValueError("Use one plain approved command; shell chaining and expansion are blocked")
        cwd = Path(inputs.get("workdir") or worktree).resolve()
        if cwd != worktree.resolve():
            raise ValueError("Commands must run at the assigned worktree root")
        args = shlex.split(command)
        if len(args) < 2:
            raise ValueError("Command is not on the worker allowlist")
        executable, operation = args[:2]
        if executable == "cp" and len(args) == 3:
            source, target = Path(args[1]).resolve(), Path(args[2]).resolve()
            if source.is_file() and source.suffix.lower() == ".png" and within(source, worktree) and within(target, directory) and target.suffix.lower() == ".png":
                return {"decision": "approve"}
            raise ValueError("Capture copying is limited to a worktree PNG and this job's PNG evidence")
        if executable == "git":
            if operation in {"status", "diff", "log", "show", "rev-parse", "ls-files"}:
                if any(part.startswith("--out") or "inventree_servers.json" in part or ".env" in part for part in args[2:]):
                    raise ValueError("Git read commands cannot write output or expose configuration")
                return {"decision": "approve"}
            if job["writable"] and operation == "add":
                if len(args) < 3:
                    raise ValueError("Stage named owned paths")
                for raw in args[2:]:
                    path = (worktree / raw).resolve()
                    if raw.startswith("-") or raw == "." or not within(path, worktree) or restricted(path):
                        raise ValueError("Blanket staging and protected paths are blocked")
                return {"decision": "approve"}
            if job["writable"] and operation == "commit":
                parts = args[2:]
                if not parts or len(parts) % 2 or any(parts[index] != "-m" for index in range(0, len(parts), 2)):
                    raise ValueError("Commit owned staged changes with inline -m messages; history rewriting is blocked")
                return {"decision": "approve"}
        if executable == "dotnet":
            if any(part.startswith(("-p:", "/p:", "--property", "--output")) or part == "-o" for part in args[2:]):
                raise ValueError("Build-output overrides are blocked")
            if operation == "test" and len(args) >= 3:
                target = args[2].replace("\\", "/")
                if target == "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" and "--disable-build-servers" in args:
                    return {"decision": "approve"}
            if operation == "format" and len(args) >= 3:
                targets = {"Solidworks Inventree Add-In.sln", "SwInventreeAddin/SwInventreeAddin.csproj", "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj"}
                if args[2].replace("\\", "/") in targets and (job["writable"] or "--verify-no-changes" in args):
                    return {"decision": "approve"}
        raise ValueError("Only scoped git operations and isolated dotnet test/format commands are approved")
    if name == "skill":
        if inputs.get("skill") in {"tdd", "codebase-design", "domain-voice"}:
            return {"decision": "approve"}
        raise ValueError("Use the supplied phase prompt, not another orchestration skill")
    if name in {"read", "grep", "find_file_by_name", "get_output", "todo_write"}:
        return {"decision": "approve"}
    raise ValueError("External actions, nested agents, and desktop control are unavailable to workers")


def main():
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="strict")
    job = event = None
    try:
        job, event = load(sys.argv[1]), json.load(sys.stdin)
        response = decision(job, event)
    except (ValueError, OSError, KeyError, TypeError, IndexError, AttributeError) as error:
        response = {"decision": "block", "reason": str(error)}
    if isinstance(job, dict) and isinstance(event, dict):
        try:
            inputs = event.get("tool_input", {})
            inputs = inputs if isinstance(inputs, dict) else {}
            command, path = inputs.get("command"), inputs.get("file_path")
            check = "allowed-status" if command == "git status --short" else "chained-command" if command == "git status && git push --dry-run" else "outside-write" if path and path == job.get("policy_outside") else None
            row = {"tool": event.get("tool_name"), "decision": response["decision"], "check": check,
                   "session_id": event.get("session_id"), "guard_hash": file_hash(__file__)}
            with (Path(job["job_dir"]) / "policy-events.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(row) + "\n")
        except (OSError, KeyError, TypeError, ValueError) as error:
            response = {"decision": "block", "reason": "Policy evidence could not be recorded: " + str(error)}
    print(json.dumps(response))
    return 2 if response["decision"] == "block" else 0


if __name__ == "__main__":
    raise SystemExit(main())
