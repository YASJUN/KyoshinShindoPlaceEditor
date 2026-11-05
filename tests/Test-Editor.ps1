param(
    [string]$SamplePath = 'D:\Document\github\kyoshin-monitor-observation-points\intensity-points.json',
    [string]$ReleaseDir = 'D:\KyoshinShindoPlaceEditor.0.0.6.0',
    [switch]$Network
)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
Get-ChildItem -LiteralPath $ReleaseDir -Filter '*.dll' | Copy-Item -Destination $bin
$editor = Join-Path $PSScriptRoot '..\KyoshinShindoPlaceEditor\bin\Release\KyoshinShindoPlaceEditor.exe'
Copy-Item -LiteralPath $editor -Destination $bin -Force
Copy-Item -LiteralPath (Join-Path $ReleaseDir 'KyoshinShindoPlaceEditor.exe.config') -Destination $bin -Force
$testExe = Join-Path $bin 'EditorRegressionTests.exe'
& $compiler /nologo /target:exe "/out:$testExe" `
    "/reference:$(Join-Path $bin 'KyoshinShindoPlaceEditor.exe')" `
    "/reference:$(Join-Path $bin 'KyoshinMonitorLib.dll')" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll `
    (Join-Path $PSScriptRoot 'EditorRegressionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Regression tests did not compile.' }
if ($Network) { & $testExe $SamplePath --network } else { & $testExe $SamplePath }
if ($LASTEXITCODE -ne 0) { throw 'Editor regression tests failed.' }
