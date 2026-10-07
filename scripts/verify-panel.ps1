# Issue #37: verify the control panel and the engine column by hand on Windows.
#
# Run from an ELEVATED PowerShell, after installing a build of main:
#   powershell -ExecutionPolicy Bypass -File scripts\verify-panel.ps1
#
# Part A runs by itself through the command line and the settings database.
# Part B asks you to look at the panel and answer y or n.
# Every result is written to verify-panel-report.txt, next to where you ran it.
# The rows it adds are named zz-verify-* and are removed at the end.

param(
    [string]$Cli = 'C:\Program Files\ByteBridge\ByteBridge.Service.exe',
    [string]$Db = 'C:\ProgramData\ByteBridge\bytebridge.db',
    [string]$Report = (Join-Path (Get-Location) 'verify-panel-report.txt')
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Run this from an elevated PowerShell (Run as administrator).' }
if (-not (Test-Path $Cli)) { throw "Not found: $Cli. Install the build first, or pass -Cli." }

"ByteBridge panel verification, $(Get-Date -Format s)" | Set-Content $Report -Encoding utf8
"Build: $((Get-Item $Cli).VersionInfo.ProductVersion)" | Add-Content $Report -Encoding utf8

function Result($id, $ok, $note) {
    $line = '{0} {1} - {2}' -f $id, $(if ($ok) { 'PASS' } else { 'FAIL' }), $note
    Write-Host $line -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    Add-Content $Report $line -Encoding utf8
}

function Ask($id, $question) {
    $a = Read-Host "$id  $question  (y/n)"
    $note = ''
    if ($a -ne 'y') { $note = Read-Host '    what did you see instead?' }
    Result $id ($a -eq 'y') $(if ($note) { "$question -> $note" } else { $question })
}

# Read the rows straight from SQLite (python is installed; passwords are
# encrypted, so only the plain columns are read).
$py = Join-Path $env:TEMP 'bb-rows.py'
@'
import sqlite3, sys, json
c = sqlite3.connect('file:' + sys.argv[1].replace('\\', '/') + '?mode=ro', uri=True)
rows = c.execute("select Name, Port, Username, DatabaseType from Databases where Name like 'zz-verify-%'").fetchall()
print(json.dumps(rows))
'@ | Set-Content $py -Encoding utf8

function Rows { ConvertFrom-Json (& python $py $Db) }

function Row($name) {
    foreach ($r in (Rows)) { if ($r[0] -eq $name) { return $r } }
    return $null
}

function Cleanup {
    foreach ($n in 'zz-verify-fb', 'zz-verify-pg', 'zz-verify-port', 'zz-verify-repair') {
        try { & $Cli db remove $n *> $null } catch { }
    }
}

Cleanup

# ---------------------------------------------------------------- Part A
Write-Host "`n== Part A: command line and database ==" -ForegroundColor Cyan

& $Cli db add --name zz-verify-fb --server 127.0.0.1 --path 'C:\verify\none.fdb' --user SYSDBA --password x | Out-Null
$r = Row 'zz-verify-fb'
Result 'A1' ($r -and $r[1] -eq 3050 -and $r[2] -eq 'SYSDBA' -and $r[3] -eq 'Firebird') `
    "Firebird row: port 3050, user SYSDBA, type Firebird (got: $($r -join ', '))"

& $Cli db add --name zz-verify-pg --type postgresql --server 127.0.0.1 --database shop --user postgres --password x | Out-Null
$r = Row 'zz-verify-pg'
Result 'A2' ($r -and $r[1] -eq 5432 -and $r[2] -eq 'postgres' -and $r[3] -eq 'PostgreSql') `
    "PostgreSQL row: port 5432, user postgres, type PostgreSql (got: $($r -join ', '))"

& $Cli db add --name zz-verify-port --type postgresql --server 127.0.0.1 --database shop --port 5999 --user app --password x | Out-Null
$r = Row 'zz-verify-port'
Result 'A3' ($r -and $r[1] -eq 5999) "a port you typed stays yours (got: $($r -join ', '))"

$list = (& $Cli db list) -join "`n"
Result 'A4' ($list -match 'PostgreSQL') 'db list marks the PostgreSQL connection'

$bad = $false
try {
    & $Cli db add --name zz-verify-bad --type oracle --server x --database y --user u --password p *> $null
    $bad = ($LASTEXITCODE -ne 0)
} catch { $bad = $true }
Result 'A5' ($bad -and -not (Row 'zz-verify-bad')) 'an unknown --type is refused and saves nothing'

# An unsupported engine, left by an older build: set one row to SqlServer.
& $Cli db add --name zz-verify-repair --server 127.0.0.1 --path 'C:\verify\none.fdb' --user SYSDBA --password x | Out-Null
$svc = Get-Service ByteBridge -ErrorAction SilentlyContinue
$wasRunning = $svc -and $svc.Status -eq 'Running'
if ($wasRunning) { Stop-Service ByteBridge }
@'
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
c.execute("update Databases set DatabaseType='SqlServer' where Name='zz-verify-repair'")
c.commit()
'@ | Set-Content (Join-Path $env:TEMP 'bb-flip.py') -Encoding utf8
& python (Join-Path $env:TEMP 'bb-flip.py') $Db
$r = Row 'zz-verify-repair'
Result 'A6' ($r -and $r[3] -eq 'SqlServer') 'one row now holds the unsupported engine SqlServer'

# ---------------------------------------------------------------- Part B
Write-Host "`n== Part B: look at the panel ==" -ForegroundColor Cyan
Write-Host 'Open the ByteBridge control panel now (as administrator). Rows zz-verify-* exist.'
Read-Host 'Press Enter when the panel is open' | Out-Null

Ask 'B1' 'File > New Database...: the engine box says Firebird, port 3050, user SYSDBA, page 2 asks for a PATH?'
Ask 'B2' 'Test Connection against a real Firebird database, then Finish: the card reads Online?'
Ask 'B3' 'Open that connection for editing: the engine box says Firebird, details unchanged, saving keeps it Online?'
Ask 'B4' 'New Database with PostgreSQL chosen: port 5432, user postgres, page 2 asks for a NAME?'
Ask 'B5' 'Edit zz-verify-pg and switch it to Firebird: the port changed to 3050 (it held the 5432 default)?'
Ask 'B6' 'Edit zz-verify-port and switch it to Firebird: the port stayed 5999 (you typed it)?'
Ask 'B7' 'Switch the panel to Arabic and reopen the wizard: every label is mirrored, no stray English?'
Ask 'B8' 'The card for zz-verify-repair says "Engine not supported", and its engine box opens with nothing selected?'
Ask 'B9' 'Choosing an engine on zz-verify-repair and saving repairs the row (the card no longer says unsupported)?'

# ---------------------------------------------------------------- Done
Cleanup
if ($wasRunning) { Start-Service ByteBridge }
Write-Host "`nDone. Send me the file: $Report" -ForegroundColor Cyan
