[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Setup,
    [Parameter(Mandatory)][string]$Scratch
)
$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$Scratch = [IO.Path]::GetFullPath($Scratch)
$source = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$registry = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{1F156DD0-1ABC-4D48-BB6C-A73862FA9515}_is1'
function Test-Registered {
    foreach ($view in 'Registry64','Registry32') {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser',$view)
        try { $entry = $base.OpenSubKey($registry); if ($entry) { $entry.Dispose(); return $true } }
        finally { $base.Dispose() }
    }
    return $false
}
$shortcutDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'FlowPivot'
if ((Test-Registered) -or (Test-Path -LiteralPath $shortcutDir)) { throw 'Existing installation or shortcut group; test not started.' }
if (Test-Path -LiteralPath $Scratch) { throw 'Scratch must be a new directory.' }
New-Item -Path $Scratch -ItemType Directory | Out-Null
$dir = Join-Path $Scratch 'installed'
$app = Join-Path $dir 'FlowPivot.exe'
$uninstall = Join-Path $dir 'unins000.exe'
$report = Join-Path $Scratch 'startup.txt'
$log = Join-Path $Scratch 'setup.log'
try {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LANG=zhcn /TASKS="" /DIR="'+$dir+'" /LOG="'+$log+'"'
    $installer = Start-Process -FilePath $Setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($installer.ExitCode -ne 0) { throw "Setup exit: $($installer.ExitCode)" }
    if (!(Test-Registered) -or !(Test-Path -LiteralPath $uninstall)) { throw 'Uninstall registration missing.' }
    $link = Get-ChildItem -LiteralPath $shortcutDir -Filter '*.lnk' -File | Select-Object -First 1
    if (!$link) { throw 'Start Menu shortcut missing.' }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($link.FullName).TargetPath -ne $app) { throw 'Shortcut target mismatch.' }
    foreach ($name in 'index.html','client.js','client.css','app.css','visuals.js') {
        $actual = (Get-FileHash -LiteralPath (Join-Path $dir "web/$name")).Hash
        $expected = (Get-FileHash -LiteralPath (Join-Path $(if($name -in 'app.css','visuals.js'){$source}else{Join-Path $PSScriptRoot 'web'}) $name)).Hash
        if ($actual -ne $expected) { throw "Installed file mismatch: $name" }
    }
    'PASS: installation, Start Menu shortcut target, installed UI file integrity, uninstall registration.'
    $client = Start-Process -FilePath $app -ArgumentList ('--self-test "'+$report+'"') -WindowStyle Hidden -PassThru
    if (!$client.WaitForExit(35000)) { $client.Kill(); $client.WaitForExit(); throw 'Installed program startup test timed out.' }
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }
    if ($client.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Installed program exit: $($client.ExitCode)" }
} finally {
    if (Test-Path -LiteralPath $uninstall) {
        $removed = Start-Process -FilePath $uninstall -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($removed.ExitCode -ne 0 -or (Test-Registered) -or (Test-Path -LiteralPath $app) -or (Test-Path -LiteralPath $shortcutDir)) {
            throw "Failed to remove temporary test installation: $($removed.ExitCode)"
        }
        'PASS: uninstall removed application, Start Menu shortcut and registration.'
    }
}
