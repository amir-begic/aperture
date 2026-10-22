; The Windows installer, Aperture-Setup.exe. The release workflow builds it with Inno Setup on a Windows runner:
;
;   iscc /DReleaseDir=<unpacked aperture-… folder> /DVersion=2026.10.20.58 /DOutputDir=<folder> deploy\windows\aperture.iss
;
; Installs for the person logged in, without administrator rights, into %LOCALAPPDATA%\Aperture\releases, then runs
; "Aperture.Web install": scheduled tasks for Aperture at logon, the updater and the daily backup, and the browser opens
; on Aperture. Its uninstaller (Settings → Apps) runs "Aperture.Web uninstall"; it asks whether to remove the data and
; backups too, and keeps them unless told to. Installing a newer one over it keeps all data.

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

[UninstallDelete]
; Also the releases installed by updates since. The data, logs and backups stay unless the person chose to remove them.
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

// Before the uninstaller removes the program: stop Aperture and remove its tasks, and with the person's yes, all its
// data. No by default; a silent uninstall always keeps the data.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Parameters: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    Parameters := 'uninstall --home "' + ExpandConstant('{app}') + '"';
    // Nested, so a silent uninstall never asks (Pascal Script may evaluate both sides of an "and").
    if not UninstallSilent then
      if MsgBox('Also remove all your Aperture data?' + #13#10 + #13#10 +
        'This deletes the database, all receipts, invoices and other files, the login, the backups and the logs in ' +
        ExpandConstant('{app}') + '. It can''t be undone.' + #13#10 + #13#10 +
        'Choose No to keep them, e.g. to install Aperture again later.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        Parameters := Parameters + ' --remove-data';
    Exec(ExpandConstant('{app}\app\Aperture.Web.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
