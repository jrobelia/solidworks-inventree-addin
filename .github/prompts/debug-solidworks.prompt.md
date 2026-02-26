# Debug — SolidWorks Add-In Specific Techniques
<!-- Used by: Debug agent (Phase 1, when bug only occurs inside SolidWorks) -->
<!-- When to use: Any bug that cannot be reproduced by running dotnet test.   -->
<!--              Read this before touching any production code.               -->

---

## Why SolidWorks needs a different approach

SolidWorks is an opaque host process — it loads the add-in DLL and calls into
it via COM. This means:
- You cannot attach a Visual Studio debugger to a running SolidWorks instance.
- Unhandled exceptions are swallowed silently; SolidWorks shows nothing.
- Most bugs cannot be reproduced outside SolidWorks because they depend on the
  COM environment, the STA thread, or the live document model.

File logging is the only reliable way to see what is actually happening.

---

## Step 1 — Add a temporary log helper

Write this static class in the add-in project (or reuse it if it already exists).
Place it anywhere — it will be deleted before committing.

```csharp
internal static class DebugLog
{
    // Write to the project root so the log can be read directly by the AI agent.
    // Never write to Desktop or AppData — those require manual retrieval.
    private static readonly string LogPath =
        @"C:\PythonProjects\Solidworks Inventree Add-In\_scratch\inventree_debug.log";

    internal static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        System.IO.File.AppendAllText(LogPath, line + Environment.NewLine);
    }
}
```

Then add calls at the boundaries you want to observe:

```csharp
DebugLog.Write("FetchPartAsync — entered, ipn: " + ipn);
DebugLog.Write("FetchPartAsync — HTTP response received");
DebugLog.Write("FetchPartAsync — part: " + (part?.Name ?? "null"));
```

---

## Step 2 — Build and reload the add-in

```powershell
dotnet build
```

Then in SolidWorks: Add-In Manager → uncheck the add-in → OK → recheck → OK.
This forces SolidWorks to reload the new DLL.

---

## Step 3 — Trigger the bug

Perform exactly the action that causes the problem.

---

## Step 4 — Read the log

Open `_scratch/inventree_debug.log`. The last line that was written
before the failure is where to look. Work inward from there.

---

## Step 5 — Remove all logging before fixing

Once the root cause is identified, delete every `DebugLog.Write(...)` call
and the `DebugLog` class itself. Then write the fix.

**Never commit logging code.**

---

## Common SolidWorks-specific causes

| Symptom | Likely cause |
|---|---|
| SolidWorks freezes after an async button click | `await` without `ConfigureAwait(false)` in service/HTTP code — continuation deadlocks on the STA thread |
| Add-in silently fails to load | Exception in `ConnectToSW()` swallowed by COM — wrap the whole method in try/catch and show a MessageBox |
| Panel shows stale data after switching documents | `ActiveDocChangeNotify` or `OnIdleNotify` not wired up correctly |
| Exception only with certain part files | Part has a missing or null custom property — add null/empty checks at the boundary |
