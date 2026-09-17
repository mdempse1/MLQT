; MLQT — Windows installer (Inno Setup 6)
;
; Phase 7b-7. One installer carrying all three tools: the Photino GUI, the `mlqt` CLI and the MCP
; server. The nupkg, the tarball and the zips it replaces are gone — `dotnet tool install` is an SDK
; command, and obliging a build agent to install the SDK to run a linter was the wrong trade.
;
; Built by the release workflow:
;   ISCC /DAppVersion=1.2.3 /DStageDir=...\publish\win-x64 /DOutputDir=...\artifacts build\installer\mlqt.iss
;
; The defaults below let it compile locally against a staging tree for testing.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef StageDir
  #define StageDir "..\..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#define AppName        "MLQT"
#define AppPublisher   "M Dempsey Ltd"
#define AppUrl         "https://github.com/mdempse1/MLQT"
#define GuiExe         "MLQT.Photino.exe"
#define CliExe         "mlqt.exe"
#define McpExe         "MLQT.McpServer.exe"

; All three tools, or no installer. The [Files] section copies the staging tree wholesale, so a
; publish that quietly stopped producing one of them would ship an installer missing a tool and
; nothing would say so until a user went looking for it. Checked here, at compile time, because that
; is the first moment the answer is knowable and the last moment it is cheap.
#if !FileExists(AddBackslash(StageDir) + GuiExe)
  #error The staging tree has no GUI. Publish MLQT.Photino into StageDir before building the installer.
#endif
#if !FileExists(AddBackslash(StageDir) + CliExe)
  #error The staging tree has no mlqt CLI. Publish MLQT.Cli into StageDir before building the installer.
#endif
#if !FileExists(AddBackslash(StageDir) + McpExe)
  #error The staging tree has no MCP server. Publish MLQT.McpServer into StageDir before building the installer.
#endif

; VersionInfoVersion must be numeric, and AppVersion is a semver that may carry "-dev" or "-rc1".
#define Dash Pos("-", AppVersion)
#define NumericVersion (Dash > 0 ? Copy(AppVersion, 1, Dash - 1) : AppVersion)

[Setup]
; Never change this. It is what makes an upgrade replace the previous install instead of sitting
; beside it, and what the uninstaller is registered under.
AppId={{8B0E5A6C-2F41-4E2B-9B77-3C6D5A1E9F04}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=MLQT-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\Branding\mlqt.ico
UninstallDisplayIcon={app}\{#GuiExe}
UninstallDisplayName={#AppName} {#AppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; PATH is written on install, so tell Windows to broadcast the change.
ChangesEnvironment=yes

; "Just for me" is the default and needs no elevation. "For all users" is offered in the same dialog.
; Note that a machine without .NET 10 will still see one UAC prompt whichever is chosen: the runtime
; installer is machine-wide and elevates itself. That is accepted rather than worked around — the
; alternative is publishing self-contained and carrying 80 MB of runtime in every download.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath";  Description: "Add the mlqt command-line tool to PATH"; GroupDescription: "Command line"

[Files]
; The whole staging tree, which is the three applications published into one folder. They are
; framework-dependent and share every assembly below MLQT.Shared, so publishing them together costs
; one copy rather than three — and `svn\`, `wwwroot\` and the bundled fonts come along with it.
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; The window and taskbar icon, which Photino loads by path at runtime.
Source: "..\..\Branding\mlqt.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Created deliberately, with the icon named explicitly. B132 was a Start Menu shortcut that *Windows*
; invented, pointing at a build from before the icon existed — and because the taskbar takes a running
; window's icon from its matching shortcut, every copy of MLQT then showed the generic placeholder.
; An installer that creates this properly is what stops that happening again.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#GuiExe}"; IconFilename: "{app}\mlqt.ico"; Comment: "Modelica Library Quality Tool"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#GuiExe}"; IconFilename: "{app}\mlqt.ico"; Tasks: desktopicon

[Registry]
; PATH, in whichever hive this install owns. Two entries rather than one because the key differs:
; a per-user install must not write to HKLM, and a per-machine install written to HKCU would put the
; tool on one account's PATH only.
;
; Inno appends but does not un-append: an uninstall leaves the fragment behind, pointing at a folder
; that has just been deleted. Measured, by installing and uninstalling and looking. RemoveFromPath in
; [Code] is what reverses it.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Tasks: addtopath; Check: not IsAdminInstallMode and NeedsPathEntry(ExpandConstant('{app}'))
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Tasks: addtopath; Check: IsAdminInstallMode and NeedsPathEntry(ExpandConstant('{app}'))

[Run]
; Prerequisites first, then optionally the application. Both installers elevate themselves when they
; need to, and both are skipped when the thing is already there — see the Check functions.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
    StatusMsg: "Installing the WebView2 runtime..."; Flags: waituntilterminated; Check: NeedsWebView2
Filename: "{tmp}\dotnet-runtime-win-x64.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Installing the .NET 10 runtime..."; Flags: waituntilterminated; Check: NeedsDotNetRuntime
Filename: "{app}\{#GuiExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The application writes nothing into {app}, but a webview leaves caches beside the executable.
Type: filesandordirs; Name: "{app}\wwwroot"

[Code]
const
  WebView2ClientKey = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2UserKey   = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  DotNetRuntimeUrl  = 'https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe';
  WebView2Url       = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

var
  DownloadPage: TDownloadWizardPage;

{ Where the .NET host lives. The sharedhost key is written by every .NET installer and carries the
  root path; falling back to the default location covers an xcopy layout. }
function DotNetRoot: String;
begin
  if not RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Path', Result) then
    Result := ExpandConstant('{commonpf64}\dotnet\');
end;

{ Whether a 10.x shared framework is installed.

  Deliberately a directory check rather than a registry one. The obvious key —
  InstalledVersions\x64\sharedfx\Microsoft.NETCore.App — **does not exist** on a machine with .NET 10
  installed; that was measured, not assumed, and an installer trusting it would download and run the
  runtime installer on every machine, raising a UAC prompt for nothing. The versioned directories
  under shared\Microsoft.NETCore.App are what the host itself resolves against. }
function DotNetRuntimeInstalled: Boolean;
var
  Search: TFindRec;
  Dir: String;
begin
  Result := False;
  Dir := AddBackslash(DotNetRoot) + 'shared\Microsoft.NETCore.App';

  if FindFirst(AddBackslash(Dir) + '10.*', Search) then
  try
    repeat
      if (Search.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(Search);
  finally
    FindClose(Search);
  end;
end;

{ The Evergreen runtime, per-machine or per-user. A `pv` of 0.0.0.0 means the key survived an
  uninstall and the runtime is not actually there. }
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result := False;

  if RegQueryStringValue(HKLM, WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := True
  else if RegQueryStringValue(HKCU, WebView2UserKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
    Result := True;
end;

function NeedsDotNetRuntime: Boolean;
begin
  Result := not DotNetRuntimeInstalled;
end;

function NeedsWebView2: Boolean;
begin
  Result := not WebView2Installed;
end;

{ True when the directory is not already on the PATH this install would write to. Compared with
  separators around both sides so that "C:\MLQT" does not match "C:\MLQTOld". }
function NeedsPathEntry(Dir: String): Boolean;
var
  Existing: String;
begin
  if IsAdminInstallMode then
  begin
    if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Existing) then
      Existing := '';
  end
  else
  begin
    if not RegQueryStringValue(HKCU, 'Environment', 'Path', Existing) then
      Existing := '';
  end;

  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Existing) + ';') = 0;
end;

{ Takes the directory back out of PATH, matching however it was written - with or without a trailing
  separator, in any case. Only the exact entry is removed; a path that merely starts with it is left
  alone, which is the same care NeedsPathEntry takes on the way in. }
procedure RemoveFromPath(Dir: String);
var
  RootKey: Integer;
  SubKey, Existing, Rebuilt, Part: String;
  Position: Integer;
begin
  if IsAdminInstallMode then
  begin
    RootKey := HKLM;
    SubKey := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  end
  else
  begin
    RootKey := HKCU;
    SubKey := 'Environment';
  end;

  if not RegQueryStringValue(RootKey, SubKey, 'Path', Existing) then
    Exit;

  Rebuilt := '';
  Existing := Existing + ';';

  repeat
    Position := Pos(';', Existing);
    Part := Copy(Existing, 1, Position - 1);
    Existing := Copy(Existing, Position + 1, Length(Existing));

    if (Part <> '') and (CompareText(RemoveBackslash(Part), RemoveBackslash(Dir)) <> 0) then
    begin
      if Rebuilt <> '' then
        Rebuilt := Rebuilt + ';';
      Rebuilt := Rebuilt + Part;
    end;
  until Existing = '';

  RegWriteExpandStringValue(RootKey, SubKey, 'Path', Rebuilt);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID <> wpReady then
    Exit;

  DownloadPage.Clear;

  if NeedsWebView2 then
    DownloadPage.Add(WebView2Url, 'MicrosoftEdgeWebview2Setup.exe', '');
  if NeedsDotNetRuntime then
    DownloadPage.Add(DotNetRuntimeUrl, 'dotnet-runtime-win-x64.exe', '');

  { Nothing to fetch is the common case, and showing an empty progress page for it would be worse
    than showing nothing. }
  if DownloadPage.AbortedByUser or (not NeedsWebView2 and not NeedsDotNetRuntime) then
    Exit;

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      { A failed download is not a failed install of the part we control, but continuing would leave
        an application that cannot start. Say which prerequisite and let the user retry. }
      SuppressibleMsgBox(
        'A prerequisite could not be downloaded:' + #13#10#13#10 + GetExceptionMessage + #13#10#13#10 +
        'MLQT needs the .NET 10 runtime and the WebView2 runtime. Install them manually and run this ' +
        'installer again, or retry with a working network connection.',
        mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;
