; installer\Duble.iss — the Setup: a wizard that says where Duble will land and lets you change it, asks
; which shortcuts to make, and offers to start the program at the end. Polish or English, after Windows.
;
; Compiled by the release workflow:
;   ISCC.exe /DAppVersion=2.3.0 /DPublishDir=publish installer\Duble.iss
; PublishDir is relative to this file. The application updates itself by downloading the newest Setup from
; the release assets, checking it against Duble-Setup.exe.sha256, and running it with /VERYSILENT — see
; InnoUpdateInstaller in Duble.App\Updates.cs, which also names this AppId to tell an installed copy.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif

[Setup]
AppId={{7C42B95D-31A4-4E7B-9AEA-6E2D64F82D11}
AppName=Duble
AppVersion={#AppVersion}
AppVerName=Duble {#AppVersion}
AppPublisher=Bobadu
AppPublisherURL=https://qorion.net/duble
AppSupportURL=https://github.com/Bobadu/duble/issues
AppUpdatesURL=https://github.com/Bobadu/duble/releases
AppCopyright=Copyright (c) 2026 Bobadu
DefaultDirName={autopf}\Duble
DefaultGroupName=Duble
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
; installs for the one user without administrator rights; the dialog lets you choose "everyone" if you may
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayName=Duble
UninstallDisplayIcon={app}\Duble.exe
SetupIconFile=..\Duble.App\assets\duble.ico
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
OutputBaseFilename=Duble-Setup
OutputDir=out
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
ShowLanguageDialog=auto
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Duble Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Duble"; Filename: "{app}\Duble.exe"
Name: "{autodesktop}\Duble"; Filename: "{app}\Duble.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Duble.exe"; Description: "{cm:LaunchProgram,Duble}"; Flags: nowait postinstall skipifsilent
