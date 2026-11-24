[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Iscc,
  [Parameter(Mandatory)][string]$WebViewSdk,
  [Parameter(Mandatory)][string]$YamlSdk,
  [Parameter(Mandatory)][string]$YamlLicense,
  [Parameter(Mandatory)][string]$CoreArchiveDir,
  [Parameter(Mandatory)][string]$Stage,
  [Parameter(Mandatory)][string]$Output
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Iscc=(Resolve-Path -LiteralPath $Iscc).Path
$WebViewSdk=(Resolve-Path -LiteralPath $WebViewSdk).Path
$YamlSdk=(Resolve-Path -LiteralPath $YamlSdk).Path
$YamlLicense=(Resolve-Path -LiteralPath $YamlLicense).Path
$Output=(Resolve-Path -LiteralPath $Output).Path
$CoreArchiveDir=(Resolve-Path -LiteralPath $CoreArchiveDir).Path
$versionMatch=[regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppUpdate.cs') -Raw),'const string Version="(\d{1,4}\.\d{1,4}\.\d{1,4})(?:-(alpha|beta|rc)\.(\d{1,4}))?";')
if(!$versionMatch.Success){throw 'AppUpdate.cs must declare a supported version'}
$version=$versionMatch.Groups[1].Value
$buildNumber=0
if($versionMatch.Groups[2].Success){$version+='-'+$versionMatch.Groups[2].Value+'.'+$versionMatch.Groups[3].Value;$buildNumber=[int]$versionMatch.Groups[3].Value}
$fileVersion=$versionMatch.Groups[1].Value+".$buildNumber"
$prefix="FlowPivot-$version-windows-x64"
$setup=Join-Path $Output ($prefix+'-setup.exe')
$zip=Join-Path $Output ($prefix+'-portable.zip')
foreach($p in @($Stage,($Stage+'.core-stage.exe'),$setup,$zip)){if(Test-Path -LiteralPath $p){throw "Refusing to overwrite $p"}}
$Stage=(New-Item -ItemType Directory -Path $Stage).FullName
$web=(New-Item -ItemType Directory -Path (Join-Path $Stage 'web')).FullName
foreach($name in @('index.html','client.js','client.css','strings.js')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot "web\$name") -Destination $web}
foreach($name in @('app.css','visuals.js','brand.svg')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\..\$name") -Destination $web}
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath (Join-Path $WebViewSdk "lib\net462\$name") -Destination $Stage}
Copy-Item -LiteralPath (Join-Path $WebViewSdk 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $Stage
foreach($name in @('LICENSE.txt','NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $WebViewSdk $name) -Destination (Join-Path $Stage ('WebView2-'+$name))}
Copy-Item -LiteralPath (Join-Path $YamlSdk 'lib\net47\YamlDotNet.dll') -Destination $Stage
Copy-Item -LiteralPath $YamlLicense -Destination (Join-Path $Stage 'YamlDotNet-LICENSE.txt')
foreach($name in @('README.md','THIRD-PARTY-CORES.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Stage}
$files=(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object Name -NotIn @('LoopbackTest.cs','BundledCoreStager.cs')).FullName
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$appIcon=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'assets\FlowPivot.ico')).Path
Copy-Item -LiteralPath $appIcon -Destination (Join-Path $Stage "FlowPivot-$version.ico")
$stager=$Stage+'.core-stage.exe'
$references=@('/reference:System.Web.Extensions.dll','/reference:System.Web.dll','/reference:System.Security.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',"/reference:$Stage\Microsoft.Web.WebView2.Core.dll","/reference:$Stage\Microsoft.Web.WebView2.WinForms.dll","/reference:$Stage\YamlDotNet.dll")
try {
    & $csc /nologo /codepage:65001 /target:exe /platform:x64 /optimize+ /warnaserror+ /main:BundledCoreStager "/out:$stager" /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'CoreAsset.cs') (Join-Path $PSScriptRoot 'BundledCoreStager.cs')
    if($LASTEXITCODE -ne 0){throw 'Core staging utility compilation failed'}
    & $stager $CoreArchiveDir (Join-Path $Stage 'cores') (Join-Path $PSScriptRoot 'core-licenses')
    if($LASTEXITCODE -ne 0){throw 'Core staging failed'}
} finally {
    if(Test-Path -LiteralPath $stager){Remove-Item -LiteralPath $stager}
}
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$Stage\FlowPivot.exe" "/win32icon:$appIcon" @references $files
if($LASTEXITCODE -ne 0){throw 'Client compilation failed'}
& $Iscc "/DPayload=$Stage" "/DOutput=$Output" "/DVersion=$version" "/DFileVersion=$fileVersion" "/DAppIcon=$appIcon" (Join-Path $PSScriptRoot 'client.iss')
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setup)){throw 'Installer build failed'}
Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $zip
Get-FileHash -LiteralPath @($setup,$zip)
