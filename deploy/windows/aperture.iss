; The Windows installer, Aperture-Setup.exe. The release workflow builds it with Inno Setup on a Windows runner:
;
;   iscc /DReleaseDir=<unpacked aperture-… folder> /DVersion=2026.10.20.58 /DOutputDir=<folder> deploy\windows\aperture.iss
;
; Installs for the person logged in, without administrator rights, into %LOCALAPPDATA%\Aperture\releases, then runs
; "Aperture.Web install": scheduled tasks for Aperture at logon, the updater and the daily backup, and the browser opens
; on Aperture. Its uninstaller (Settings → Apps) runs "Aperture.Web uninstall", which keeps the data and backups.
; Installing a newer one over it keeps all data too.

#ifndef ReleaseDir
  #error Pass /DReleaseDir=<the unpacked release folder>
#endif
#ifndef Version
  #define Version "0.0.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#define ReleaseName ExtractFileName(ReleaseDir)

[Setup]
AppId={{8F4D2C1E-6B7A-4E3F-9C2D-5A1B0E7F3D46}
AppName=Aperture
AppVersion={#Version}
AppVerName=Aperture
DefaultDirName={localappdata}\Aperture
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 or newer, as .NET 10 needs.
MinVersion=10.0.17763
LicenseFile=..\..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=Aperture-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Aperture
UninstallDisplayIcon={app}\releases\{#ReleaseName}\Aperture.Web.exe
; Aperture is stopped by its own install command, not by Windows' restart manager.
CloseApplications=no

[Messages]
WelcomeLabel2=This installs Aperture, the office for your photography business, for you on this PC.%n%nIt runs in the background while you're logged in and opens in your browser. Only this PC can reach it, and your data stays in your user folder. It backs up your data every day, and updates itself when you click Install update in its settings.%n%nInstalling again later keeps all your data.
FinishedLabel=Aperture is installed and opened in your browser. Choose a username and password there, then fill in your business details under Settings.%n%nNext time, open Aperture from the Start menu.

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}\releases\{#ReleaseName}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; In the Start menu: opens Aperture in the browser.
Filename: "{userprograms}\Aperture.url"; Section: "InternetShortcut"; Key: "URL"; String: "http://localhost:5090"

[UninstallRun]
Filename: "{app}\app\Aperture.Web.exe"; Parameters: "uninstall --home ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "Uninstall"

[UninstallDelete]
; Also the releases installed by updates since; the data, logs and backups stay.
Type: filesandordirs; Name: "{app}\releases"
Type: files; Name: "{userprograms}\Aperture.url"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Starting Aperture...';
    if not Exec(ExpandConstant('{app}\releases\{#ReleaseName}\Aperture.Web.exe'), ExpandConstant('install --home "{app}"'),
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      MsgBox('Aperture was installed but didn''t start. The details are in ' + ExpandConstant('{app}\logs\install.log') + '.',
        mbError, MB_OK);
  end;
end;
