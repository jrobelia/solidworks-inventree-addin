OA InvenTree Add-In  Installation Instructions
================================================

REQUIREMENTS
  - SolidWorks 2022 or newer
  - Windows 10 or 11 (64-bit)
  - .NET Framework 4.8 (already installed on most machines)

FIRST-TIME INSTALL
  1. Extract this zip to any folder (e.g. your Desktop).
  2. Right-click "Install (Run as Administrator).bat" -> Run as administrator.
  3. Notepad will open with "inventree_servers.json". Fill in:
       - "url"     : your InvenTree server address, e.g. https://inventree.mycompany.com
       - "api_key" : your personal API token from InvenTree
                     (InvenTree -> click your name -> Account Settings -> API Tokens)
  4. Save and close Notepad.
  5. Start SolidWorks. The InvenTree panel appears in the right-hand task pane.

UPDATING TO A NEW VERSION
  Run "Install (Run as Administrator).bat" again.
  Your inventree_servers.json (including your API key) is preserved automatically.

UNINSTALLING
  Right-click "Uninstall (Run as Administrator).bat" -> Run as administrator.

TROUBLESHOOTING
  - Panel does not appear: Go to SolidWorks -> Tools -> Add-ins and tick "InvenTree".
  - "API key rejected" error: generate a new token in InvenTree Account Settings
    and paste it into C:\Program Files\OA InvenTree Addin\inventree_servers.json.
