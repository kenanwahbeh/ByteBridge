; Inno Setup script for the ByteBridge .exe installer.
;
; Built by .github/workflows/release.yml. One script builds both
; distributions:
;
;   ; .NET bundled
;   iscc /DAppVersion=1.2.3 installer\ByteBridge.iss
;
;   ; .NET required on the machine
;   iscc /DAppVersion=1.2.3 /DPublishDir=publish-fx ^
;        /DVariant=-framework /DRequireRuntime=1 installer\ByteBridge.iss
;
; PublishDir is named relative to the repo root; the script adds the
; ..\ itself.
;
; AppId identifies the product across every release and both
; distributions, so a machine ends up with one ByteBridge rather
; than two competing installs. It must never change.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; The file-version resource takes digits only, so a pre-release suffix
; ("3.3.0-beta.1") is cut off for it and kept everywhere else.
#define DashAt Pos("-", AppVersion)
#if DashAt > 0
  #define NumericVersion Copy(AppVersion, 1, DashAt - 1)
#else
  #define NumericVersion AppVersion
#endif

#ifndef PublishDir
  #define PublishDir "publish"
#endif

#ifndef Variant
  #define Variant ""
#endif

#define AppName      "ByteBridge"
#define AppPublisher "ByteBridge"
#define AppExeName   "ByteBridge.exe"
#define ServiceExe   "ByteBridge.Service.exe"
#define ServiceName  "ByteBridge"
#define ServiceLabel "ByteBridge Gateway"
#define AppUrl       "https://github.com/kenanwahbeh/ByteBridge"

; Prerequisites Setup can fetch on the customer's machine. Both are
; official vendor URLs that always point at the current build, so no
; SHA-256 is pinned -- a pinned hash would break on every upstream
; release. HTTPS is what authenticates them, which is why these must
; stay https:// and must stay on the vendors' own domains.
;
; release.yml checks both respond before it builds, so a URL that moves
; fails the release instead of failing on a customer's machine.
#ifndef DotNetUrl
  #define DotNetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#endif

#ifndef CloudflaredUrl
  #define CloudflaredUrl "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.msi"
#endif

[Setup]
AppId={{4DF9ABE1-3644-4B2B-9A42-B4742A9C6DB4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#NumericVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no

; Per-machine install into Program Files; asks for elevation once.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Matches the MSI's floor and the app's own requirements.
MinVersion=6.3

; Paths here are relative to this .iss file, not to the directory
; ISCC was launched from, so the repo root is one level up.
SetupIconFile=ByteBridge.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

OutputDir=..\artifacts
OutputBaseFilename=ByteBridge-{#AppVersion}-x64{#Variant}-setup

; The bundled build is a self-contained .NET tree, so it compresses
; well but is large; solid LZMA2 keeps the download reasonable.
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; \
  Description: "{cm:CreateDesktopIcon}"; \
  GroupDescription: "{cm:AdditionalIcons}"; \
  Flags: unchecked

; Offered only when cloudflared is not already on the machine, and
; ticked by default because this app is meant to be reached through a
; tunnel. Installing the binary does not connect a tunnel by itself:
; that takes a token, which the "Connect to ByteBalance" page below (or
; the control panel's Connect to ByteBalance dialog, or the `enroll`
; command) obtains once ByteBalance has approved this machine.
Name: "cloudflared"; \
  Description: "Download and install Cloudflare Tunnel (cloudflared), about 18 MB"; \
  GroupDescription: "Cloudflare Tunnel:"; \
  Check: not HasCloudflared

[Dirs]
; Created here so the installer owns its permissions rather than
; whichever account happens to start the service first.
Name: "{commonappdata}\ByteBridge"

[Files]
Source: "..\{#PublishDir}\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; shellexec matters here and is not decoration: app.manifest marks
; ByteBridge.exe requireAdministrator, and without this flag Setup
; launches [Run] entries via CreateProcess, which cannot silently
; elevate a manifested child -- even though Setup itself is already
; running elevated. That fails with "Unable to execute file ... error
; code 740" right after an otherwise-successful install, reading as a
; failure when nothing failed. ShellExecute (what shellexec selects)
; goes through the same elevation path Explorer uses for a shortcut,
; which does handle it correctly.

[Run]
Filename: "{app}\{#AppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent shellexec

[Code]

{
  Setup can fetch two things from the vendors during install: the .NET
  Desktop Runtime, for the -framework build that does not carry it, and
  cloudflared, without which there is no tunnel to reach this app
  through.

  The two failures are treated differently on purpose. Without .NET the
  -framework build cannot run at all, so a failed download aborts the
  install rather than leaving a shortcut to an app that exits on launch.
  cloudflared is not needed for the app to run, only to reach it from
  outside, so a failed download warns and carries on.
}

var
  PrereqPage: TDownloadWizardPage;
  DotNetSetup: String;
  CloudflaredSetup: String;
  ConnectPage: TInputQueryWizardPage;
  SkipRuntime: Boolean;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

{ Walks PATH by hand rather than using a search helper, to stay inside
  the small set of functions Inno's Pascal Script is guaranteed to have. }
function OnPath(const Exe: String): Boolean;
var
  Path, Dir: String;
  P: Integer;
begin
  Result := False;
  Path := GetEnv('PATH') + ';';

  while (not Result) and (Path <> '') do
  begin
    P := Pos(';', Path);
    if P = 0 then
      P := Length(Path) + 1;

    Dir := Trim(Copy(Path, 1, P - 1));
    Delete(Path, 1, P);

    if Dir <> '' then
    begin
      if Dir[Length(Dir)] <> '\' then
        Dir := Dir + '\';

      Result := FileExists(Dir + Exe);
    end;
  end;
end;

{
  Both Program Files locations, because the cloudflared .msi has shipped
  to each over time, plus PATH, which covers winget, scoop and a manual
  drop into a folder of the admin's choosing.
}
function HasCloudflared(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf64}\cloudflared\cloudflared.exe'))
         or FileExists(ExpandConstant('{commonpf32}\cloudflared\cloudflared.exe'))
         or OnPath('cloudflared.exe');
end;

#ifdef RequireRuntime
{
  A 10.x shared-framework directory under a dotnet root, rather than a
  registry version string alone, so a leftover key from an uninstalled
  runtime cannot fool it.
}
function RootHasDesktopRuntime10(Root: String): Boolean;
var
  Rec: TFindRec;
  Base: String;
begin
  Result := False;

  Root := Trim(Root);
  if Root = '' then
    Exit;

  if Root[Length(Root)] <> '\' then
    Root := Root + '\';

  Base := Root + 'shared\Microsoft.WindowsDesktop.App';

  if FindFirst(Base + '\10.*', Rec) then
  begin
    try
      repeat
        if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := True;
      until Result or (not FindNext(Rec));
    finally
      FindClose(Rec);
    end;
  end;
end;

{
  Checks every place a runtime can legitimately be: the default folder,
  the folder the .NET installer recorded for itself (covers a custom
  install location), and a machine-wide DOTNET_ROOT. Each is confirmed
  on disk.

  The .NET installer writes InstallLocation to the 32-bit registry view,
  so that is read first, with the 64-bit view as a fallback.

  DOTNET_ROOT is read from the machine environment, not Setup's own: the
  service runs as Local System and never sees the signed-in user's
  variables, so a runtime reachable only through those would pass here
  and then fail to start.
}
function HasDesktopRuntime10(): Boolean;
var
  Location: String;
begin
  Result := RootHasDesktopRuntime10(ExpandConstant('{commonpf64}\dotnet'));

  if (not Result)
     and (RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64',
                              'InstallLocation', Location)
          or RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64',
                                 'InstallLocation', Location)) then
    Result := RootHasDesktopRuntime10(Location);

  if (not Result)
     and RegQueryStringValue(HKLM64,
           'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
           'DOTNET_ROOT', Location) then
    Result := RootHasDesktopRuntime10(Location);
end;
#endif

{ Downloads one file, returning False rather than raising, so each
  caller can decide whether its prerequisite is worth stopping for. }
function TryDownload(const Url, BaseName: String; var Path: String): Boolean;
begin
  Path := '';

  PrereqPage.Clear;
  PrereqPage.Add(Url, BaseName, '');
  PrereqPage.Show;

  try
    try
      PrereqPage.Download;
      Path := ExpandConstant('{tmp}\') + BaseName;
      Result := True;
    except
      Log('Download of ' + Url + ' failed: ' + GetExceptionMessage);
      Result := False;
    end;
  finally
    PrereqPage.Hide;
  end;
end;

procedure InitializeWizard();
begin
  PrereqPage := CreateDownloadPage(SetupMessage(msgWizardPreparing),
                                   SetupMessage(msgPreparingDesc),
                                   @OnDownloadProgress);

  { After the task choices, so the person has just decided about the
    tunnel software. Optional: an empty box connects nothing, and the
    same thing can be done later from the control panel. A silent
    install can supply the address as /ByteBalanceEmail=owner@example.com. }
  ConnectPage := CreateInputQueryPage(
    wpSelectTasks,
    'Connect to ByteBalance (optional)',
    'Give this machine a private tunnel to ByteBalance.',
    'Enter the one email address that should be allowed through to this '
    + 'machine''s tunnel. ByteBalance approves each request before anything is '
    + 'opened.'
    + #13#10#13#10
    + 'Leave it empty to connect later: ByteBridge > Connect to ByteBalance.');

  ConnectPage.Add('Owner email:', False);
  ConnectPage.Values[0] := ExpandConstant('{param:ByteBalanceEmail|}');
end;

{
  A plain check, only to catch a typo before it is sent. ByteBalance
  validates strictly and has the last word. It also refuses everything
  that would mean something to the command line the address is later
  placed on.
}
function ValidEmail(const Address: String): Boolean;
var
  At, I: Integer;
  Domain: String;
begin
  Result := False;

  At := Pos('@', Address);

  if (At < 2) or (At = Length(Address)) then
    Exit;

  Domain := Copy(Address, At + 1, Length(Address));

  if (Pos('@', Domain) > 0) or (Pos('.', Domain) = 0) then
    Exit;

  for I := 1 to Length(Address) do
  begin
    if (Ord(Address[I]) <= 32) or (Address[I] = '"') or (Address[I] = ',')
       or (Address[I] = ';') or (Address[I] = '<') or (Address[I] = '>')
       or (Address[I] = '\') then
      Exit;
  end;

  Result := True;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

#ifdef RequireRuntime
  { Asked before anything is written, so declining costs nothing. A
    silent run has nobody to ask, so it consents on their behalf --
    MsgBox blocks forever otherwise, since it is a raw message box,
    not a wizard page, and /SILENT and /VERYSILENT only suppress
    those. }
  if not HasDesktopRuntime10() then
  begin
    if WizardSilent() then
      Log('Silent install: .NET 10 Desktop Runtime missing, downloading without prompting.')
    else
      case MsgBox('The .NET 10 Desktop Runtime (x64) was not found on this computer.'
                  + #13#10#13#10
                  + 'Yes: download it from Microsoft and install it (about 60 MB).'
                  + #13#10
                  + 'No: install ByteBridge without it, if you know it is already installed.'
                  + #13#10
                  + 'Cancel: exit Setup.',
                  mbConfirmation, MB_YESNOCANCEL) of
        IDNO: SkipRuntime := True;
        IDCANCEL: Result := False;
      end;
  end;
#endif
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if CurPageID = ConnectPage.ID then
  begin
    if (Trim(ConnectPage.Values[0]) <> '') and (not ValidEmail(Trim(ConnectPage.Values[0]))) then
    begin
      MsgBox('That does not look like an email address. Enter one address, such as'
             + ' owner@example.com, or leave the box empty to connect later.',
             mbError, MB_OK);
      Result := False;
    end;

    Exit;
  end;

  if CurPageID <> wpReady then
    Exit;

#ifdef RequireRuntime
  if (not SkipRuntime) and (not HasDesktopRuntime10()) then
  begin
    if not TryDownload('{#DotNetUrl}', 'windowsdesktop-runtime.exe', DotNetSetup) then
    begin
      if WizardSilent() then
        Log('Silent install: the .NET 10 Desktop Runtime could not be downloaded; aborting.')
      else if MsgBox('The .NET 10 Desktop Runtime could not be downloaded.'
                + #13#10#13#10
                + 'Check the internet connection and try again, or install the runtime'
                + ' yourself and re-run this installer. The build that bundles .NET needs'
                + ' no download at all.'
                + #13#10#13#10
                + 'Open the .NET download page now?',
                mbError, MB_YESNO) = IDYES then
        ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0',
                  '', '', SW_SHOW, ewNoWait, ErrorCode);

      Result := False;
      Exit;
    end;
  end;
#endif

  if WizardIsTaskSelected('cloudflared') then
    if not TryDownload('{#CloudflaredUrl}', 'cloudflared.msi', CloudflaredSetup) then
    begin
      if WizardSilent() then
        Log('Silent install: cloudflared could not be downloaded; skipping it.')
      else
        MsgBox('cloudflared could not be downloaded, so it will be skipped.'
               + #13#10#13#10
               + 'ByteBridge itself will still install. You can add cloudflared later from'
               + ' https://github.com/cloudflare/cloudflared/releases.',
               mbInformation, MB_OK);
    end;
end;

{ Runs a console tool with no window and hands back its exit code. }
function RunHidden(const FileName, Params: String; var Code: Integer): Boolean;
begin
  Result := Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function Sc(const Params: String; var Code: Integer): Boolean;
begin
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), Params, Code);
end;

{
  sc query answers 0 for a service that exists whatever state it is in,
  and 1060 when there is no such service.
}
function ServiceExists(): Boolean;
var
  Code: Integer;
begin
  Result := Sc('query {#ServiceName}', Code) and (Code = 0);
end;

{
  An upgrade of a machine that already has the service has nothing to
  ask: it is either connected already or was left unconnected on
  purpose, and asking every release would be noise.
}
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ConnectPage.ID) and ServiceExists();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';

  {
    The service holds its own executable open, so an upgrade cannot
    replace the files until it stops. Failure is ignored on purpose:
    sc stop answers 1062 for a service that is already stopped, and a
    service that will not stop is reported by the file copy that
    follows, with a better message than anything available here.
  }
  if ServiceExists() then
  begin
    Sc('stop {#ServiceName}', Code);

    { Stopping is not instant, and sc returns as soon as it is asked. }
    Sleep(3000);
  end;

#ifdef RequireRuntime
  if DotNetSetup <> '' then
  begin
    if not Exec(DotNetSetup, '/install /quiet /norestart', '',
                SW_SHOW, ewWaitUntilTerminated, Code) then
    begin
      Result := 'The .NET Desktop Runtime installer could not be started.';
      Exit;
    end;

    { 3010 is success with a restart pending. }
    if Code = 3010 then
      NeedsRestart := True
    else if Code <> 0 then
    begin
      Result := Format('The .NET Desktop Runtime installer failed with code %d.', [Code]);
      Exit;
    end;

    { Trust the result of the check, not the exit code. }
    if not HasDesktopRuntime10() then
    begin
      Result := 'The .NET Desktop Runtime installer reported success, but the runtime'
                + ' is still not present. Install it manually and run Setup again.';
      Exit;
    end;
  end;
#endif

  { Never fatal: a machine without cloudflared still runs the app. }
  if CloudflaredSetup <> '' then
  begin
    if Exec(ExpandConstant('{sys}\msiexec.exe'),
            '/i "' + CloudflaredSetup + '" /qn /norestart', '',
            SW_SHOW, ewWaitUntilTerminated, Code) then
    begin
      if Code = 3010 then
        NeedsRestart := True
      else if Code <> 0 then
      begin
        if WizardSilent() then
          Log(Format('Silent install: cloudflared did not install (installer code %d).', [Code]))
        else
          MsgBox(Format('cloudflared did not install (installer code %d).', [Code])
                 + #13#10#13#10
                 + 'ByteBridge is installed and will work locally; add cloudflared later'
                 + ' to reach it through a tunnel.', mbInformation, MB_OK);
      end;
    end
    else if WizardSilent() then
      Log('Silent install: cloudflared could not be installed; ByteBridge will still work locally.')
    else
      MsgBox('cloudflared could not be installed, but ByteBridge is installed'
             + ' and will work locally.', mbInformation, MB_OK);
  end;
end;

{
  Everything that turns the installed files into a running service.

  Done here rather than in [Run] so an upgrade can tell the difference
  between creating the service and repointing an existing one, and so a
  failure can say which step failed.
}
procedure SecureDataFolder();
var
  Code: Integer;
  Folder: String;
begin
  Folder := ExpandConstant('{commonappdata}\ByteBridge');

  {
    The settings file holds the Firebird passwords in the clear and the
    gateway API key, and that key is all that stands between the public
    internet and those databases. So inheritance is dropped and only
    Local System, which runs the service, and Administrators, who run
    the control panel, are let in.

    Identified by SID rather than name: "NT AUTHORITY\SYSTEM" and
    "BUILTIN\Administrators" are translated on localised installations
    of Windows and icacls would not find them, whereas S-1-5-18 and
    S-1-5-32-544 are the same everywhere.
  }
  RunHidden(
    ExpandConstant('{sys}\icacls.exe'),
    '"' + Folder + '" /inheritance:r'
    + ' /grant:r "*S-1-5-18:(OI)(CI)F"'
    + ' /grant:r "*S-1-5-32-544:(OI)(CI)F"',
    Code);
end;

procedure InstallService();
var
  Code: Integer;
  Binary: String;
begin
  Binary := ExpandConstant('{app}\{#ServiceExe}');

  {
    The quoting here is deliberate and worth reading twice.

    Inno's Pascal has no escape character, so '"\\""' is literally a
    quote, a backslash, a quote. Exec calls CreateProcess without a
    shell, so sc.exe receives that verbatim and parses it by the usual
    C runtime rules, under which \\" inside a quoted run means a literal
    quote. What sc ends up storing is the path with quotes around it.

    Which is what is wanted: an unquoted ImagePath containing spaces,
    and "C:\\Program Files\\ByteBridge" certainly does, is the classic
    unquoted-service-path privilege escalation. Windows would try
    C:\\Program.exe first.

     Confirm after installing with:  sc qc ByteBridge
    BINARY_PATH_NAME must show the full path wrapped in quotes.

    Note also the space after every "=", which sc requires.
  }
  if ServiceExists() then
  begin
    Sc('config {#ServiceName} binPath= "\"' + Binary + '\"" start= auto', Code);
  end
  else
  begin
    Sc('create {#ServiceName} binPath= "\"' + Binary + '\"" start= auto'
       + ' DisplayName= "{#ServiceLabel}"', Code);

    if Code <> 0 then
    begin
      MsgBox('The ByteBridge service could not be registered (code '
             + IntToStr(Code) + ').'#13#10#13#10
             + 'The application is installed, but the gateway will not run '
             + 'until the service exists.',
             mbError, MB_OK);

      Exit;
    end;
  end;

  Sc('description {#ServiceName} "Serves the ByteBridge HTTP gateway over '
     + 'the configured databases, so it runs without anyone signed in."',
     Code);

  {
    Restart on failure rather than staying down. This is the machine a
    tunnel points at; a gateway that dies quietly is a 502 nobody is
    watching for. The counter resets after a day so a genuinely broken
    service still stops flapping.
  }
  Sc('failure {#ServiceName} reset= 86400'
     + ' actions= restart/5000/restart/15000/restart/60000', Code);

  Sc('start {#ServiceName}', Code);
end;

{
  Asks ByteBalance for a tunnel, without waiting for the approval: a
  person has to click an email first, and Setup should not sit there.
  Exit code 2 is "request sent, waiting", which is the success case here.
  The connector itself is installed later by `claim`, or by the Connect
  dialog in the control panel, once the request is approved.

  Never fatal. A machine that could not reach ByteBalance is a working
  gateway that is not connected yet, and says how to fix it.
}
procedure ConnectToByteBalance();
var
  Code: Integer;
  Address: String;
begin
  Address := Trim(ConnectPage.Values[0]);

  if Address = '' then
    Exit;

  if not ValidEmail(Address) then
  begin
    Log('ByteBalance email "' + Address + '" is not a valid address; not connecting.');
    Exit;
  end;

  if RunHidden(ExpandConstant('{app}\{#ServiceExe}'),
               'enroll --email "' + Address + '" --no-wait', Code)
     and ((Code = 0) or (Code = 2)) then
  begin
    Log('ByteBalance enrolment requested for ' + Address + '.');
    Exit;
  end;

  if WizardSilent() then
    Log(Format('Silent install: could not ask ByteBalance for a tunnel (code %d).', [Code]))
  else
    MsgBox('ByteBridge is installed, but ByteBalance could not be asked for a tunnel'
           + ' (code ' + IntToStr(Code) + ').'
           + #13#10#13#10
           + 'Open ByteBridge and choose Connect to ByteBalance to try again.',
           mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SecureDataFolder();
    InstallService();
    ConnectToByteBalance();
  end;
end;

{
  Taking the service away again.

  Done in code rather than [UninstallRun] because sc returns the moment
  it has asked, so a delete issued straight after a stop can arrive
  while the service is still shutting down and fail. The pause is the
  difference between a clean removal and a service left behind marked
  for deletion until the next reboot.
}
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep <> usUninstall then
  begin
    Exit;
  end;

  if not ServiceExists() then
  begin
    Exit;
  end;

  Sc('stop {#ServiceName}', Code);

  Sleep(3000);

  Sc('delete {#ServiceName}', Code);

  {
    The settings folder is deliberately left behind. It holds the
    configured databases and the API key, and someone reinstalling
    should not have to type them all again. It stays readable only by
    Administrators and Local System, as the install left it.
  }
end;
