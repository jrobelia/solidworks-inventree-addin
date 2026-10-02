; SwInventreeAddin Inno Setup script
;
; Build:  iscc /DAppVersion=2.1.0 Installer\Setup.iss
; Output: Installer\SwInventreeAddin-<version>-Setup.exe
;
; Replaces the zip + Install.bat + Install.ps1 flow with a standard Windows
; installer. Admin rights are still required — SolidWorks only discovers
; add-ins under HKLM\SOFTWARE\SolidWorks\Addins and COM registration lands
; in HKLM\Software\Classes — but the user sees a normal wizard, a UAC prompt,
; and a real entry in Settings > Apps instead of a batch file.
;
; COM registration still goes through RegAsm so the registry layout stays
; owned by [ComRegisterFunction]/[ComUnregisterFunction] in SwAddin.cs.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#define AppName "SwInventreeAddin"
#define BuildDir "..\SwInventreeAddin\bin\Release\net48"
#define RegAsm "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

[Setup]
; The add-in's CLSID doubles as the AppId — a stable identity that lets Inno
; recognise previous installs and upgrade them in place.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=SwInventreeAddin
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Prompt to close SolidWorks when it has the DLL loaded — the usual
; "install while SolidWorks is open" failure becomes a guided close.
CloseApplications=yes
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=.
OutputBaseFilename={#AppName}-{#AppVersion}-Setup
UninstallDisplayName={#AppName}

[Files]
Source: "{#BuildDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Resources\*"; DestDir: "{app}\Resources"; \
    Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Run]
Filename: "{#RegAsm}"; \
    Parameters: """{app}\SwInventreeAddin.dll"" /codebase /s"; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Registering the add-in with SolidWorks..."

; Runs before files are removed, so the DLL still exists when RegAsm
; unregisters it — [ComUnregisterFunction] removes the SolidWorks keys.
[UninstallRun]
Filename: "{#RegAsm}"; \
    Parameters: """{app}\SwInventreeAddin.dll"" /u /s"; \
    Flags: runhidden waituntilterminated; RunOnceId: "UnregisterAddin"

[Code]
// .NET Framework 4.8 is a hard prerequisite; fail early with a clear
// message rather than letting the RegAsm step error mid-install.
function InitializeSetup(): Boolean;
begin
  if not FileExists(ExpandConstant('{#RegAsm}')) then
  begin
    MsgBox('SwInventreeAddin requires .NET Framework 4.8.' + #13#10 +
           'Install it from Microsoft and run this installer again.',
           mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
