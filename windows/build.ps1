[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Iscc,
    [Parameter(Mandatory)][string]$Sdk,
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$Iscc = (Resolve-Path -LiteralPath $Iscc).Path
$Sdk = (Resolve-Path -LiteralPath $Sdk).Path
$Output = (Resolve-Path -LiteralPath $Output).Path
$Stage = [IO.Path]::GetFullPath($Stage)
if (Test-Path -LiteralPath $Stage) { throw "Stage already exists: $Stage" }
$setup = Join-Path $Output 'FlowPivot-0.1.0-preview.1-windows-x64-setup.exe'
if (Test-Path -LiteralPath $setup) { throw "Setup already exists: $setup" }
$web = Join-Path $Stage 'web'
New-Item -Path $web -ItemType Directory | Out-Null
foreach ($file in 'index.html','app.js','app.css','visuals.js') { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $web }
foreach ($file in 'Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll') {
    Copy-Item -LiteralPath (Join-Path $Sdk "lib\net462\$file") -Destination $Stage
}
Copy-Item -LiteralPath (Join-Path $Sdk 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $Stage
foreach ($file in 'LICENSE.txt','NOTICE.txt') {
    Copy-Item -LiteralPath (Join-Path $Sdk $file) -Destination (Join-Path $Stage "WebView2-$file")
}

Add-Type -AssemblyName System.Drawing
$icon = Join-Path $Stage 'FlowPivot.ico'
$bitmap = [System.Drawing.Bitmap]::new(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$mint = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#b1e4cc'), 5)
$png = [IO.MemoryStream]::new()
try {
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#17362e'))
    $mint.StartCap = $mint.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($mint, [Drawing.Point[]]@([Drawing.Point]::new(12,18),[Drawing.Point]::new(29,18),[Drawing.Point]::new(43,32),[Drawing.Point]::new(52,32)))
    $graphics.DrawLine($mint, 12,32,52,32)
    $graphics.DrawLines($mint, [Drawing.Point[]]@([Drawing.Point]::new(12,46),[Drawing.Point]::new(29,46),[Drawing.Point]::new(43,32)))
    $graphics.DrawLines($mint, [Drawing.Point[]]@([Drawing.Point]::new(46,26),[Drawing.Point]::new(52,32),[Drawing.Point]::new(46,38)))
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $header = [byte[]]@(0,0,1,0,1,0,64,64,0,0,1,0,32,0)
    [IO.File]::WriteAllBytes($icon, [byte[]]($header + [BitConverter]::GetBytes([int]$png.Length) + [BitConverter]::GetBytes([int]22) + $png.ToArray()))
} finally { $png.Dispose(); $mint.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$Stage\FlowPivot.exe" "/win32icon:$icon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    "/reference:$Stage\Microsoft.Web.WebView2.Core.dll" "/reference:$Stage\Microsoft.Web.WebView2.WinForms.dll" `
    (Join-Path $PSScriptRoot 'PreviewWindow.cs')
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed: $LASTEXITCODE" }
& $Iscc "/DPayload=$Stage" "/DOutput=$Output" (Join-Path $PSScriptRoot 'preview.iss')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw "Installer compilation failed: $LASTEXITCODE" }
Get-Item -LiteralPath $setup
