param([string]$OutputDirectory = 'artifacts/t18', [switch]$BootstrapLock)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'Windows x64 / PowerShell 7 required.' }
Set-Location (Split-Path $PSScriptRoot -Parent)
$sha = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Git SHA unavailable.' }
if ((dotnet --version).Trim() -ne '10.0.401') { throw 'SDK 10.0.401 required.' }
$project = 'src/RandomMultimediaManager.App/RandomMultimediaManager.App.csproj'
$publishProperties = @('-r', 'win-x64', '--self-contained', 'true', '-p:RuntimeFrameworkVersion=10.0.12', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false', '-p:ContinuousIntegrationBuild=true', "-p:SourceRevisionId=$sha")
$restoreProperties = @('-r', 'win-x64', '-p:SelfContained=true', '-p:RuntimeFrameworkVersion=10.0.12', '--use-lock-file')
if (-not $BootstrapLock) { $restoreProperties += '--locked-mode' }
dotnet restore $project @restoreProperties
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
# A fresh destination prevents stale dependency files entering the ZIP.
if (Test-Path $out) { throw "Destination already exists: $out" }
$package = Join-Path $out 'RandomMultimediaManager-win-x64'
New-Item -ItemType Directory -Path $package -Force | Out-Null
dotnet publish $project -c Release --no-restore @publishProperties -o $package
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($required in @('RandomMultimediaManager.App.exe', 'RandomMultimediaManager.App.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll', 'libSkiaSharp.dll', 'e_sqlite3.dll', 'libvlc/win-x64/libvlc.dll', 'libvlc/win-x64/libvlccore.dll')) {
    if (-not (Test-Path (Join-Path $package $required))) { throw "Missing required payload: $required" }
}
if (@(Get-ChildItem "$package/libvlc/win-x64/plugins" -Recurse -Filter '*.dll').Count -eq 0) { throw 'VLC plugins missing.' }
Copy-Item LICENSE "$package/LICENSE"
Copy-Item packaging/USER_GUIDE.md "$package/USER_GUIDE.md"
Copy-Item packaging/THIRD_PARTY_NOTICES.md "$package/THIRD_PARTY_NOTICES.md"
Copy-Item packaging/licenses "$package/licenses" -Recurse
$assets = Get-Content 'src/RandomMultimediaManager.App/obj/project.assets.json' -Raw | ConvertFrom-Json -AsHashtable
$licenses = Join-Path $package 'dependency-notices'
New-Item -ItemType Directory $licenses | Out-Null
$dependencies = foreach ($key in ($assets.libraries.Keys | Sort-Object)) {
    $lib = $assets.libraries[$key]
    if ($lib.type -ne 'package') { continue }
    $cache = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder $lib.path
        if (Test-Path $candidate) { $cache = $candidate; break }
    }
    if ($null -eq $cache) { throw "Package cache missing: $key" }
    $dest = Join-Path $licenses $lib.path
    New-Item -ItemType Directory $dest -Force | Out-Null
    $noticeFiles = @(Get-ChildItem $cache -Recurse -File | Where-Object { $_.Name -match '(?i)license|copying|notice|copyright|\.nuspec$|\.nupkg\.sha512$' })
    foreach ($file in $noticeFiles) {
        $relative = [IO.Path]::GetRelativePath($cache, $file.FullName)
        $target = Join-Path $dest $relative
        New-Item -ItemType Directory (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item $file.FullName $target
    }
    [ordered]@{ package = $key; sha512 = $lib.sha512; noticeFiles = @($noticeFiles | ForEach-Object { [IO.Path]::GetRelativePath($cache, $_.FullName).Replace('\','/') }) }
}
# Runtime packs are download dependencies, not always listed in assets.libraries.
foreach ($id in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $cache = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder "$id/10.0.12"
        if (Test-Path $candidate) { $cache = $candidate; break }
    }
    if ($null -eq $cache) { throw "Runtime package cache missing: $id" }
    $dest = Join-Path $licenses "$id/10.0.12"
    New-Item -ItemType Directory $dest -Force | Out-Null
    $noticeFiles = @(Get-ChildItem $cache -Recurse -File | Where-Object { $_.Name -match '(?i)license|copying|notice|copyright|\.nuspec$|\.nupkg\.sha512$' })
    if (-not @($noticeFiles | Where-Object { $_.Name -match '(?i)license' }).Count) { throw "Runtime license missing: $id" }
    foreach ($file in $noticeFiles) {
        $target = Join-Path $dest ([IO.Path]::GetRelativePath($cache, $file.FullName))
        New-Item -ItemType Directory (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item $file.FullName $target -Force
    }
    $dependencies += [ordered]@{ package = "$id/10.0.12"; sha512 = (Get-Content "$cache/$id.10.0.12.nupkg.sha512" -Raw).Trim(); noticeFiles = @($noticeFiles | ForEach-Object { [IO.Path]::GetRelativePath($cache, $_.FullName).Replace('\','/') }) }
}
$dependencies | ConvertTo-Json -Depth 10 | Set-Content "$package/dependencies.json" -Encoding utf8NoBOM
Copy-Item 'src/RandomMultimediaManager.App/packages.lock.json' "$package/packages.lock.json"
[ordered]@{ commit = $sha; sdk = '10.0.401'; runtime = '10.0.12'; rid = 'win-x64'; selfContained = $true; singleFile = $false; publicReleaseApproved = $false } | ConvertTo-Json | Set-Content "$package/build-info.json" -Encoding utf8NoBOM
$manifest = @(Get-ChildItem $package -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest | ConvertTo-Json -Depth 5 | Set-Content "$package/files.json" -Encoding utf8NoBOM
# Fixed entry ordering and timestamp; reproducibility is checked by CI, not assumed.
Add-Type -AssemblyName System.IO.Compression
$zipPath = Join-Path $out "RandomMultimediaManager-win-x64-$($sha.Substring(0,12)).zip"
$stream = [IO.File]::Create($zipPath)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem $package -Recurse -File | Sort-Object FullName)) {
        $name = 'RandomMultimediaManager-win-x64/' + [IO.Path]::GetRelativePath($package, $file.FullName).Replace('\','/')
        $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2020,1,1,0,0,0,[TimeSpan]::Zero)
        $source = $file.OpenRead(); $target = $entry.Open()
        try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
    }
} finally { $zip.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content "$zipPath.sha256" -Encoding utf8NoBOM
Write-Output "ZIP $zipPath SHA256 $hash FILES $($manifest.Count + 1)"
