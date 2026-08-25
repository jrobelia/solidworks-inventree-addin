# Parent and center add-in pop-ups on their owner window

Add-in windows and message boxes were opening with inconsistent owners and startup locations. Some top-level windows set `Owner = SolidWorksWindowHandle.Get()` in code-behind but declared `WindowStartupLocation="CenterScreen"` in XAML, so they actually centered on the primary monitor. `ImageCropWindow` centered itself on the primary screen manually. WPF `MessageBox.Show` calls in the Task Pane had no owner and could fall behind SolidWorks. Only `PropertyMappingEditorWindow`, opened from Settings, was already correctly owned by and centered on its parent.

Considered options:

1. **Leave `CenterScreen` and no message-box owner.** Simple, but leaves dialogs off-center or hidden behind SolidWorks.
2. **Parent every top-level window to SolidWorks and use `CenterOwner`; parent child dialogs to their opener and use `CenterOwner`; give message boxes a SolidWorks `IWin32Window` owner.** Chosen because it is consistent, relies on the existing `SolidWorksWindowHandle.Get()` `IntPtr`, and introduces no new COM calls.

Decision: all top-level add-in windows (`BomCompareWindow`, `CreatePartWindow`, `SettingsWindow`, `PushRevisionConfirmDialog`, `ImageCropWindow`) use `WindowInteropHelper(this).Owner = SolidWorksWindowHandle.Get()` and `WindowStartupLocation="CenterOwner"`. `ImageCropWindow` removes its manual primary-screen centering but may still restore saved width and height. `PropertyMappingEditorWindow` continues to use `Owner = SettingsWindow` and `CenterOwner`. Unparented WPF `MessageBox` calls are replaced with WinForms `MessageBox.Show` using a lightweight `IWin32Window` wrapper around `SolidWorksWindowHandle.Get()`.
