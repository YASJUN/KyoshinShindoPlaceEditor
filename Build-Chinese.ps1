param(
    [string]$OriginalReleaseDir = 'D:\KyoshinShindoPlaceEditor.0.0.6.0'
)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio MSBuild was not found.' }

$packageDir = Join-Path $PSScriptRoot 'packages\Microsoft.NETFramework.ReferenceAssemblies.net47.1.0.3'
$frameworkRoot = Join-Path $packageDir 'build'
if (!(Test-Path -LiteralPath (Join-Path $frameworkRoot '.NETFramework\v4.7\mscorlib.dll'))) {
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    $archive = Join-Path $packageDir 'reference-assemblies.zip'
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net47/1.0.3/microsoft.netframework.referenceassemblies.net47.1.0.3.nupkg' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $packageDir -Force
}

& $msbuild (Join-Path $PSScriptRoot 'KyoshinShindoPlaceEditor\KyoshinShindoPlaceEditor.csproj') `
    /t:Rebuild /p:Configuration=Release /p:BuildProjectReferences=false `
    "/p:TargetFrameworkRootPath=$frameworkRoot" `
    "/p:OriginalReleaseDir=$OriginalReleaseDir" `
    "/p:CustomBeforeMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'build\Chinese.targets')" `
    /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
