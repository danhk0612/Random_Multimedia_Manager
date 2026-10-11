param([string]$ArtifactDirectory = 'artifacts/t18')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$out = [IO.Path]::GetFullPath($ArtifactDirectory)
$zip = @(Get-ChildItem $out -Filter '*.zip')
if ($zip.Count -ne 1) { throw 'Exactly one review ZIP required.' }
$extract = Join-Path $out 'extracted 한글 space'
Expand-Archive $zip[0].FullName $extract
$package = Join-Path $extract 'RandomMultimediaManager-win-x64'
$manifest = Get-Content "$package/files.json" -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $file = Join-Path $package $entry.path
    if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "ZIP hash mismatch: $($entry.path)" }
}
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Observe-Native($process, $root) {
    try {
        foreach ($module in $process.Modules) {
            if ($module.ModuleName -match '^(coreclr|hostfxr|hostpolicy|libSkiaSharp|e_sqlite3|libvlc|libvlccore)\.dll$') {
                if (-not $module.FileName.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "External dependency loaded: $($module.FileName)" }
                if ($seen.Add($module.FileName)) { Write-Output "PAYLOAD MODULE $($module.FileName)" }
            }
        }
    } catch [System.ComponentModel.Win32Exception] { } # Process can exit during enumeration.
}
# No dotnet host or globally installed VLC is used by the launched processes.
$savedPath = $env:PATH; $savedRoot = $env:DOTNET_ROOT
$dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RandomMultimediaManager'
if (Test-Path $dataRoot) { throw 'Use a fresh disposable Windows account: existing app data must not be touched.' }
$env:DOTNET_ROOT = Join-Path $out 'no-global-runtime'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:PATH = "$env:SystemRoot/System32;$env:SystemRoot"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
try {
    $db = Join-Path $dataRoot 'library.db'
    1..2 | ForEach-Object {
        $process = Start-Process "$package/RandomMultimediaManager.App.exe" -WorkingDirectory $package -PassThru
        try {
            $deadline = (Get-Date).AddSeconds(45)
            do {
                Start-Sleep -Milliseconds 200; $process.Refresh()
                if ($process.HasExited) { throw 'Extracted app exited before its main window.' }
            } while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
            if ($process.MainWindowTitle -ne 'Random Multimedia Manager') { throw 'Extracted WPF window missing.' }
            Observe-Native $process $package
            $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, '종료')
            $quit = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
            if ($null -eq $quit) { throw 'Graceful exit button missing.' }
            $quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'Extracted app normal exit failed.' }
            if (-not (Test-Path $db)) { throw 'Fresh-profile LocalAppData DB missing.' }
            Write-Output "PASS extracted shell start/normal exit/reopen $_"
        } finally { if (-not $process.HasExited) { $process.Kill() }; $process.Dispose() }
    }
} finally { $env:PATH = $savedPath; $env:DOTNET_ROOT = $savedRoot }
# Build harnesses separately, then copy ONLY their entry assemblies/config/fixtures
# onto another extracted ZIP. All product/native/runtime files remain ZIP bytes.
$probe = Join-Path $out 'package-probes'
Expand-Archive $zip[0].FullName $probe
$probeRoot = Join-Path $probe 'RandomMultimediaManager-win-x64'
foreach ($suite in @('Core','Data','Comic','Scanner','Video','Subtitle','Viewing')) {
    $name = "RandomMultimediaManager.$suite.Tests"
    $build = Join-Path $out "harness-$suite"
    dotnet publish "tests/$name/$name.csproj" -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.12 -p:PublishSingleFile=false -o $build
    if ($LASTEXITCODE -ne 0) { throw "Harness publish failed: $suite" }
    foreach ($suffix in @('exe','dll','deps.json','runtimeconfig.json')) { Copy-Item "$build/$name.$suffix" $probeRoot }
    if (Test-Path "$build/Fixtures") { Copy-Item "$build/Fixtures" $probeRoot -Recurse -Force }
    if (Test-Path "$build/silent.mp4") { Copy-Item "$build/silent.mp4" $probeRoot -Force }
}
foreach ($entry in $manifest) {
    if ((Get-FileHash (Join-Path $probeRoot $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Harness modified product payload: $($entry.path)" }
}
$savedPath = $env:PATH
$env:PATH = "$env:SystemRoot/System32;$env:SystemRoot"
$env:DOTNET_ROOT = Join-Path $out 'no-global-runtime'; $env:DOTNET_MULTILEVEL_LOOKUP = '0'
try {
    foreach ($suite in @('Core','Data','Comic','Scanner','Video','Subtitle','Viewing')) {
        $scenarios = if ($suite -eq 'Scanner') { @('basic','state','cancel','access','reparse','case','unc','t18a5','lifecycle','t18a6') } else { @('') }
        foreach ($scenario in $scenarios) {
            $label = "$suite-$scenario".TrimEnd('-')
            $args = @{ FilePath = "$probeRoot/RandomMultimediaManager.$suite.Tests.exe"; WorkingDirectory = $probeRoot; PassThru = $true; RedirectStandardOutput = "$out/$label.stdout.log"; RedirectStandardError = "$out/$label.stderr.log" }
            if ($scenario) { $args.ArgumentList = $scenario }
            $process = Start-Process @args
            try {
                $deadline = (Get-Date).AddMinutes(8)
                while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
                    Observe-Native $process $probeRoot
                    Start-Sleep -Milliseconds 100; $process.Refresh()
                }
                if (-not $process.HasExited) { throw "Harness timeout: $label (test containment only, no product timeout policy)" }
                Get-Content "$out/$label.stdout.log"
                Get-Content "$out/$label.stderr.log"
                if ($process.ExitCode -ne 0) { throw "Package harness failed: $label / $($process.ExitCode)" }
                Write-Output "PASS ZIP dependencies / $label"
            } finally { if (-not $process.HasExited) { $process.Kill() }; $process.Dispose() }
        }
    }
    foreach ($native in @('coreclr.dll','e_sqlite3.dll','libSkiaSharp.dll','libvlc.dll','libvlccore.dll')) {
        if (-not @($seen | Where-Object { [IO.Path]::GetFileName($_) -eq $native }).Count) { throw "Native module not observed: $native" }
    }
} finally { $env:PATH = $savedPath; $env:DOTNET_ROOT = $savedRoot }
$seen | Sort-Object | Set-Content "$out/loaded-native-modules.txt" -Encoding utf8NoBOM
Write-Output 'PASS ZIP file hashes, packaged native/runtime dependency paths, complete existing regression suites'
