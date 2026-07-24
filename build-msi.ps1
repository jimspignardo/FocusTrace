[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$publishDirectory = Join-Path $projectRoot 'artifacts\publish\win-x64'
$installerDirectory = Join-Path $projectRoot 'artifacts\installer'
$projectFile = Join-Path $projectRoot 'FocusTrace.csproj'
$installerProject = Join-Path $projectRoot 'installer\FocusTrace.Installer.wixproj'

foreach ($generatedDirectory in @($publishDirectory, $installerDirectory)) {
    $resolvedGeneratedDirectory = [IO.Path]::GetFullPath($generatedDirectory)
    if (-not $resolvedGeneratedDirectory.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated output path escaped the FocusTrace project: $resolvedGeneratedDirectory"
    }

    Remove-Item -LiteralPath $resolvedGeneratedDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishDir="$publishDirectory\" `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    throw "FocusTrace publish failed with exit code $LASTEXITCODE."
}

$buildOutputDirectory = dotnet msbuild $projectFile `
    -nologo `
    -getProperty:TargetDir `
    -p:Configuration=Release `
    -p:RuntimeIdentifier=win-x64

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($buildOutputDirectory)) {
    throw "Could not resolve the FocusTrace Release output directory."
}

$buildOutputDirectory = $buildOutputDirectory.Trim()
foreach ($resourceFile in Get-ChildItem -LiteralPath $buildOutputDirectory -File |
    Where-Object { $_.Extension -in '.xbf', '.pri' }) {
    Copy-Item -LiteralPath $resourceFile.FullName -Destination $publishDirectory -Force
}

dotnet build $installerProject `
    --configuration Release `
    -p:ProductVersion=$Version `
    -p:PublishDir=$publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "FocusTrace MSI build failed with exit code $LASTEXITCODE."
}

$msiPath = Join-Path $installerDirectory "FocusTrace-$Version-x64.msi"
if (-not (Test-Path -LiteralPath $msiPath)) {
    throw "The installer build completed without producing $msiPath."
}

Get-Item -LiteralPath $msiPath
