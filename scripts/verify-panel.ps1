# Issue #37: verify the control panel and the engine column by hand on Windows.
#
# Needs: an elevated PowerShell, and Python 3 on the PATH (Part A reads and
# edits the settings database with its sqlite3 module; pass -Python to point
# at a python.exe that is not on the PATH).
#
# Run from an ELEVATED PowerShell, after installing a build of main:
#   powershell -ExecutionPolicy Bypass -File scripts\verify-panel.ps1
#
# Part A runs by itself through the command line and the settings database.
# Part B asks you to look at the panel and answer y or n.
# Every result is written to verify-panel-report.txt, next to where you ran it.
# The rows it adds are named zz-verify-* and are removed at the end, and the
# ByteBridge service is put back the way it was (running or stopped), even if
# a check fails or you press Ctrl+C.

param(
    [string]$Cli = 'C:\Program Files\ByteBridge\ByteBridge.Service.exe',
    [string]$Db = 'C:\ProgramData\ByteBridge\bytebridge.db',
    [string]$Python = 'python',
    [string]$Report = (Join-Path (Get-Location) 'verify-panel-report.txt')
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Run this from an elevated PowerShell (Run as administrator).' }
if (-not (Get-Command $Python -ErrorAction SilentlyContinue)) {
    throw "Python 3 is needed for Part A and was not found ('$Python'). Install it, or pass -Python <path to python.exe>. Nothing has been changed."
}
if (-not (Test-Path $Cli)) { throw "Not found: $Cli. Install the build first, or pass -Cli." }

"ByteBridge panel verification, $(Get-Date -Format s)" | Set-Content $Report -Encoding utf8
"Build: $((Get-Item $Cli).VersionInfo.ProductVersion)" | Add-Content $Report -Encoding utf8

# The service as it was when we started, so the end can put it back.
$serviceBefore = Get-Service ByteBridge -ErrorAction SilentlyContinue
$serviceWasRunning = [bool]($serviceBefore -and $serviceBefore.Status -eq 'Running')

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

# Read the rows straight from SQLite (passwords are encrypted, so only the
# plain columns are read).
$py = Join-Path $env:TEMP 'bb-rows.py'
@'
import sqlite3, sys, json
c = sqlite3.connect('file:' + sys.argv[1].replace('\\', '/') + '?mode=ro', uri=True)
rows = c.execute("select Name, Port, Username, DatabaseType from Databases where Name like 'zz-verify-%'").fetchall()
print(json.dumps(rows))
'@ | Set-Content $py -Encoding utf8

function Rows { ConvertFrom-Json (& $Python $py $Db) }

function Row($name) {
    foreach ($r in (Rows)) { if ($r[0] -eq $name) { return $r } }
    return $null
}

$testNames = 'zz-verify-fb', 'zz-verify-pg', 'zz-verify-port', 'zz-verify-repair'

# Removes any test row. A row that cannot be removed stops the run: an old row
# left behind could otherwise be read as the result of this run's own add.
function Remove-TestRows {
    foreach ($n in $testNames) {
        if (Row $n) {
            & $Cli db remove $n *> $null
            if ($LASTEXITCODE -ne 0 -or (Row $n)) {
                throw "Could not remove the test row '$n'. Remove it in the panel, then run again."
            }
        }
    }
}

# Adds a row, and stops the run if the command line says it failed.
function Add-TestRow([string[]]$Arguments) {
    & $Cli db add @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "db add failed: $($Arguments -join ' ')" }
}

try {
    Remove-TestRows

    # ------------------------------------------------------------ Part A
    Write-Host "`n== Part A: command line and database ==" -ForegroundColor Cyan

    Add-TestRow '--name', 'zz-verify-fb', '--server', '127.0.0.1', '--path', 'C:\verify\none.fdb', '--user', 'SYSDBA', '--password', 'x'
    $r = Row 'zz-verify-fb'
    Result 'A1' ($r -and $r[1] -eq 3050 -and $r[2] -eq 'SYSDBA' -and $r[3] -eq 'Firebird') `
        "Firebird row: port 3050, user SYSDBA, type Firebird (got: $($r -join ', '))"

    Add-TestRow '--name', 'zz-verify-pg', '--type', 'postgresql', '--server', '127.0.0.1', '--database', 'shop', '--user', 'postgres', '--password', 'x'
    $r = Row 'zz-verify-pg'
    Result 'A2' ($r -and $r[1] -eq 5432 -and $r[2] -eq 'postgres' -and $r[3] -eq 'PostgreSql') `
        "PostgreSQL row: port 5432, user postgres, type PostgreSql (got: $($r -join ', '))"

    Add-TestRow '--name', 'zz-verify-port', '--type', 'postgresql', '--server', '127.0.0.1', '--database', 'shop', '--port', '5999', '--user', 'app', '--password', 'x'
    $r = Row 'zz-verify-port'
    Result 'A3' ($r -and $r[1] -eq 5999) "a port you typed stays yours (got: $($r -join ', '))"

    $list = (& $Cli db list) -join "`n"
    Result 'A4' ($list -match 'PostgreSQL') 'db list marks the PostgreSQL connection'

    $refused = $false
    try {
        & $Cli db add --name zz-verify-bad --type oracle --server x --database y --user u --password p *> $null
        $refused = ($LASTEXITCODE -ne 0)
    } catch { $refused = $true }
    Result 'A5' ($refused -and -not (Row 'zz-verify-bad')) 'an unknown --type is refused and saves nothing'

    # An unsupported engine, left by an older build: set one row to SqlServer.
    Add-TestRow '--name', 'zz-verify-repair', '--server', '127.0.0.1', '--path', 'C:\verify\none.fdb', '--user', 'SYSDBA', '--password', 'x'
    if ($serviceWasRunning) { Stop-Service ByteBridge }
    $flip = Join-Path $env:TEMP 'bb-flip.py'
    @'
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
c.execute("update Databases set DatabaseType='SqlServer' where Name='zz-verify-repair'")
c.commit()
'@ | Set-Content $flip -Encoding utf8
    & $Python $flip $Db
    $r = Row 'zz-verify-repair'
    Result 'A6' ($r -and $r[3] -eq 'SqlServer') 'one row now holds the unsupported engine SqlServer'

    # ------------------------------------------------------------ Part B
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
}
finally {
    # Runs on success, on a failed check, and on Ctrl+C.
    try { Remove-TestRows }
    catch { Write-Warning "Cleanup: $($_.Exception.Message)" }

    # Back to the state it started in. The panel may have started the service
    # while the checks ran, so the state is read again here, not assumed.
    $serviceNow = Get-Service ByteBridge -ErrorAction SilentlyContinue
    if ($serviceNow) {
        if ($serviceWasRunning -and $serviceNow.Status -ne 'Running') { Start-Service ByteBridge }
        elseif (-not $serviceWasRunning -and $serviceNow.Status -eq 'Running') { Stop-Service ByteBridge }
    }

    Write-Host "`nDone. Send me the file: $Report" -ForegroundColor Cyan
}
