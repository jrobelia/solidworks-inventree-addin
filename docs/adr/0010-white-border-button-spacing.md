# White border over spacer panels for button spacing

Attempted invisible spacer Panel between buttons for visual separation. WinForms AutoSize layout collapsed the spacer repeatedly. Simpler fix: 1px white border + 2px vertical margin on every button via `MakeButton`. Stands out against the blue background and requires no extra controls.
