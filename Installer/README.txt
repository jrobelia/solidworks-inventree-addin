OA InvenTree Add-In  Installation Instructions
================================================

REQUIREMENTS
  - SolidWorks 2022 or newer
  - Windows 10 or 11 (64-bit)
  - .NET Framework 4.8 (already installed on most machines)

FIRST-TIME INSTALL
  1. Extract this zip to any folder (e.g. your Desktop).
  2. Right-click "Install (Run as Administrator).bat" -> Run as administrator.
  3. You can delete the zip and extracted folder — the add-in is now self-contained
     in "C:\Program Files\OA InvenTree Addin\" and does not need the download.
  4. Start SolidWorks. The InvenTree panel appears in the right-hand task pane.
  5. Click the Settings button (gear icon) in the panel.
  6. Enter your InvenTree server URL and API key, then click Save.
     (To get an API key: InvenTree -> click your name -> Account Settings -> API Tokens)

UPDATING TO A NEW VERSION
  Download the new zip, extract it, run "Install (Run as Administrator).bat" again.
  Your saved server settings are preserved automatically.

UNINSTALLING
  Option A: Windows Settings -> Apps -> "OA InvenTree Add-In" -> Uninstall.
  Option B: Browse to "C:\Program Files\OA InvenTree Addin\" and double-click
            "Uninstall (Run as Administrator).bat".

TROUBLESHOOTING
  - Panel does not appear: Go to SolidWorks -> Tools -> Add-ins and tick "InvenTree".
  - "API key rejected" error: generate a new token in InvenTree Account Settings
    and enter it via the Settings button in the InvenTree panel.
