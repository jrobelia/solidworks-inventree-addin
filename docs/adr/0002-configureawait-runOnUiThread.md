# ConfigureAwait(false) + RunOnUiThread for STA thread safety

SolidWorks runs on an STA thread. Task.Run forced continuations onto thread pool threads, which deadlocked NUnit's STA runner and risked COM violations. Direct await with ConfigureAwait(false) lets the HTTP call run on the thread pool naturally, then RunOnUiThread marshals back via Invoke.
