# Parent and center add-in pop-ups on their owner window

Add-in windows and message boxes were opening with inconsistent owners and startup locations. Some top-level windows set `Owner = SolidWorksWindowHandle.Get()` in code-behind but declared `WindowStartupLocation="CenterScreen"` in XAML, so they actually centered on the primary monitor. `ImageCropWindow` centered itself on the primary screen manually. WPF `MessageBox.Show` calls in the Task Pane had no owner and could fall behind SolidWorks. Only `PropertyMappingEditorWindow`, opened from Settings, was already correctly owned by and centered on its parent.

The first pass (PR #110) replaced unparented WPF `MessageBox` calls with WinForms `MessageBox.Show` using a lightweight `IWin32Window` wrapper around `SolidWorksWindowHandle.Get()`. That fixed modality and z-order but not centering: `System.Windows.Forms.MessageBox.Show(IWin32Window, ...)` still opens centered on the screen, not the owner window. A generic, owner-centered WPF `MessageDialog` is therefore required for all message-box-style prompts.

Considered options:

1. **Leave `CenterScreen` and no message-box owner.** Simple, but leaves dialogs off-center or hidden behind SolidWorks. Rejected.
2. **Parent every top-level window to SolidWorks and use `CenterOwner`; parent child dialogs to their opener and use `CenterOwner`; give message boxes a SolidWorks `IWin32Window` owner.** Chosen as the first step in #106/#110, but later found insufficient for message boxes because WinForms `MessageBox` does not center on the owner. Rejected for message-box prompts.
3. **Use a generic WPF `MessageDialog` + `WindowCentering` for all message-box-style prompts, and `WindowCentering` for all top-level WPF windows.** Chosen because it gives true owner centering for both windows and message-box-style prompts, keeps the 14 `MessageBox` call sites in a single reusable dialog, and introduces no new COM calls.

Decision: all top-level add-in windows (`BomCompareWindow`, `CreatePartWindow`, `SettingsWindow`, `PushRevisionConfirmDialog`, `ImageCropWindow`) and child dialogs (`PropertyMappingEditorWindow`) use `WindowCentering.Attach(this, ownerHandle)` in their constructors. All user-facing `MessageBox`-style prompts use the generic WPF `MessageDialog`, centered with `WindowCentering.Attach` and styled with `DesignTokens.xaml` / Segoe MDL2 Assets. The existing `BomTableMissingDialog` remains the specialized BOM-table-missing prompt. `System.Windows.Forms.MessageBox.Show` and `WindowHandleOwner` are removed.

Consequences:

- All add-in pop-ups are consistently owned, parented, and centered on SolidWorks or the spawning window.
- `MessageBox`-style prompts are no longer subject to the WinForms message-box centering limitation.
- 14 `MessageBox.Show` call sites collapse into one `MessageDialog`, plus the existing `BomTableMissingDialog`.
- `WindowCentering` becomes the single source of truth for centering on a non-WPF, maximized, multi-monitor / high-DPI owner.
