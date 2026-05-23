# SaveBMP over Win32 screen capture for Viewport Capture

Original approach used Win32 `EnumChildWindows` + `BitBlt` to screen-capture the SolidWorks viewport. Fragile: heuristic child-window matching, captured overlapping windows, wrong region if panels were docked differently.

Replaced with `IModelDoc2.SaveBMP(path, 0, 0)` — the official SolidWorks API that renders the viewport to a temp BMP file at current dimensions. Clean, reliable, no P/Invoke.
