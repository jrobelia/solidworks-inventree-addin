OA InvenTree Add-In  Installation Instructions
================================================

REQUIREMENTS
  - SolidWorks 2022 or newer
  - Windows 10 or 11 (64-bit)
  - .NET Framework 4.8 (already installed on most machines)

FIRST-TIME INSTALL
  1. Extract this zip to any folder (e.g. your Desktop).
  2. Right-click "Install (Run as Administrator).bat" -> Run as administrator.
  3. Start SolidWorks. The InvenTree panel appears in the right-hand task pane.
  4. Click the Settings button (gear icon) in the panel.
  5. Enter your InvenTree server URL and API key, then click Save.
     (To get an API key: InvenTree -> click your name -> Account Settings -> API Tokens)

UPDATING TO A NEW VERSION
  Run "Install (Run as Administrator).bat" again.
  Your saved server settings are preserved automatically.

UNINSTALLING
  Right-click "Uninstall (Run as Administrator).bat" -> Run as administrator.

TROUBLESHOOTING
  - Panel does not appear: Go to SolidWorks -> Tools -> Add-ins and tick "InvenTree".
  - "API key rejected" error: generate a new token in InvenTree Account Settings
    and enter it via the Settings button in the InvenTree panel.
