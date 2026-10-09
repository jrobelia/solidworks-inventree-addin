# Verify evidence without controlling SolidWorks

Read JOB_FILE and contracts.md. Work read-only at input_head. Read the supplied specification, every approved criterion, implementation and repair recipes under ticket_dir, current reviews, and the driver's pinned test and format logs. You may write evidence and reports inside job_dir only.

For an automated criterion, inspect its discriminating test and captured result. Run a targeted test when the current logs do not establish the claim. Capture a concise text artifact with the actual observed result and its command. A screenshot is not proof of document events, Apply, Push, registration, or host behavior.

For a visual criterion, run an existing explicitly selected SurfaceCapture test using the isolated dotnet test command. Read the resulting PNG against the design language and required state. Copy the capture with one scoped `cp "<worktree PNG>" "<job_dir PNG>"` command or report a capability blocker; do not modify source to create a capture here. A missing capture can become a settled implementation finding. Keep live test windows off-screen through HiddenTestWindow; avoid the legacy harness that builds the host DLL.

For a host criterion, record host-pending with a focused GUI QA step: prerequisites, the engineer's action, and the expected result. Do not run, close, register, rebuild, or control SolidWorks. Host-pending is a successful automated handoff, not a code defect.

Use not-applicable only where the approved criterion mapping already says that. An automated or visual claim lacking proof is not-proven and produces findings with a settled correction, or blocked if the environment is unavailable. Do not recategorize it as host-only to obtain a pass.

Account for every criterion exactly once in report.md and result.json. Each automated or visual row names a real non-empty artifact relative to ticket_dir. Each host-pending or not-applicable row gives its reason; host rows include the human QA procedure. Keep evidence local and avoid secrets or real production data.

Write result.json last. Clean means all approved automated evidence is present and all host checks are named for /qa. It never means qa-verified or ready to merge.
