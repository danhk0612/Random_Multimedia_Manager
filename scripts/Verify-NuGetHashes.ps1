param(
    [Parameter(Mandatory)][string]$AssetsPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Regression
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assets = Get-Content $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
$rows = @()
foreach ($key in ($assets.libraries.Keys | Sort-Object)) {
    $lib = $assets.libraries[$key]
    if ($lib.type -ne 'package') { continue }
    $cache = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder $lib.path
        if (Test-Path $candidate) { $cache = $candidate; break }
    }
    if (-not $cache) { throw "Package cache missing: $key" }
    $id, $version = $key.ToLowerInvariant().Split('/')
    $archive = Join-Path $cache "$id.$version.nupkg"
    $rows += [ordered]@{ package = $key; archive = $archive; contentHash = $lib.sha512; archiveSha512 = (Get-Content "$archive.sha512" -Raw).Trim() }
}
foreach ($id in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $version = '10.0.12'
    $cache = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder "$id/$version"
        if (Test-Path $candidate) { $cache = $candidate; break }
    }
    if (-not $cache) { throw "Runtime cache missing: $id" }
    $archive = Join-Path $cache "$id.$version.nupkg"
    # Restore metadata is signature-aware; never manufacture it from archive .sha512.
    $metadata = Get-Content "$cache/.nupkg.metadata" -Raw | ConvertFrom-Json
    $rows += [ordered]@{ package = "$id/$version"; archive = $archive; contentHash = $metadata.contentHash; archiveSha512 = (Get-Content "$archive.sha512" -Raw).Trim() }
}
$out = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory (Split-Path $out -Parent) -Force | Out-Null
$inputPath = "$out.input.json"
$rows | ConvertTo-Json -Depth 6 | Set-Content $inputPath -Encoding utf8NoBOM
dotnet build "$PSScriptRoot/NuGetHashVerifier.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'NuGet hash verifier build failed.' }
$verifier = "$root/artifacts/nuget-hash-bin/net10.0/NuGetHashVerifier.dll"
dotnet $verifier $inputPath $out
if ($LASTEXITCODE -ne 0) { throw 'NuGet actual archive/content hash verification failed.' }
if ($Regression) {
    dotnet $verifier --regression $inputPath "$out.regression"
    if ($LASTEXITCODE -ne 0) { throw 'NuGet hash regression failed.' }
}
