param([Parameter(Mandatory)][string]$Archive, [string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows package smoke tests require Windows.' }
$product = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$build = [IO.Path]::GetFullPath((Join-Path $product 'build'))
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$temp = Join-Path $build ("package-check-" + [Guid]::NewGuid().ToString('N'))
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $build 'evidence/alpha-package' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $temp, $evidence -Force | Out-Null
try {
    $expected = (Get-Content -LiteralPath ($archivePath + '.sha256') -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0].Trim()
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expected) { throw 'Archive SHA256 mismatch.' }
    $extract = Join-Path $temp 'unpacked'
    New-Item -ItemType Directory -Path $extract | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $prefix = [IO.Path]::GetFullPath($extract) + [IO.Path]::DirectorySeparatorChar
        foreach ($entry in $zip.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $extract $entry.FullName))
            if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escapes extraction directory.' }
        }
    } finally { $zip.Dispose() }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extract
    $directories = @(Get-ChildItem -LiteralPath $extract -Directory)
    if ($directories.Count -ne 1 -or @(Get-ChildItem -LiteralPath $extract -File).Count -ne 0) { throw 'Unexpected archive layout.' }
    $app = $directories[0].FullName
    $manifest = Get-Content -LiteralPath (Join-Path $app 'package-manifest.json') -Raw | ConvertFrom-Json
    if (-not $manifest.selfContained -or $manifest.runtime -ne 'win-x64' -or $manifest.rimeBundled -or $manifest.systemIme) { throw 'Unexpected package scope.' }
    $fileCount = @(Get-ChildItem -LiteralPath $app -Recurse -File).Count
    if ($fileCount -ne $manifest.files.Count + 1) { throw 'Package contains unrecorded or missing files.' }
    foreach ($file in $manifest.files) {
        $target = [IO.Path]::GetFullPath((Join-Path $app $file.path))
        if (-not $target.StartsWith($app + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes package.' }
        if (-not (Test-Path -LiteralPath $target) -or (Get-Item -LiteralPath $target).Length -ne $file.size -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) { throw "Packaged file mismatch: $($file.path)" }
    }
    foreach ($forbidden in @('rime.dll', 'typeflow_rime.dll')) {
        if (Test-Path -LiteralPath (Join-Path $app $forbidden)) { throw "Unexpected third-party runtime: $forbidden" }
    }
    if (@(Get-ChildItem -LiteralPath $app -Recurse -File -Filter '*.ssf').Count -ne 0) { throw 'Third-party SSF fixture must not be distributed.' }
    $runs = @()
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $report = Join-Path $temp "smoke-$attempt.json"
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'TypeFlow.exe'))
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.WorkingDirectory = $temp
        foreach ($argument in @('--smoke-test', $report, '--workspace', (Join-Path $temp 'profile'))) { $start.ArgumentList.Add($argument) }
        $start.Environment['DOTNET_ROOT'] = Join-Path $temp 'no-global-dotnet'
        $start.Environment['DOTNET_ROOT_X64'] = Join-Path $temp 'no-global-dotnet'
        $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $start.Environment['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        try {
            if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw 'Packaged workbench timed out.' }
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $evidence "smoke-$attempt.stdout.txt") -Encoding utf8
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $evidence "smoke-$attempt.stderr.txt") -Encoding utf8
            if (Test-Path -LiteralPath $report) { Copy-Item -LiteralPath $report -Destination (Join-Path $evidence "smoke-$attempt.json") }
            if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw "Packaged workbench failed with exit code $($process.ExitCode)." }
        } finally { $process.Dispose() }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        Copy-Item -LiteralPath $report -Destination (Join-Path $evidence "smoke-$attempt.json")
        if ($result.preview -and (Test-Path -LiteralPath $result.preview)) { Copy-Item -LiteralPath $result.preview -Destination (Join-Path $evidence "smoke-$attempt.png") }
        if (-not $result.success -or $result.sampleCount -ne 2 -or $result.savedTheme -ne '便携包验证副本') { throw 'Packaged workbench feature check failed.' }
        if ($result.version -ne $manifest.version) { throw 'Packaged app version differs from manifest.' }
        if ([IO.Path]::GetFullPath($result.runtimeDirectory).TrimEnd('\','/') -ne $app.TrimEnd('\','/')) { throw 'Workbench used an external .NET runtime.' }
        if ($attempt -eq 2 -and $result.restoredTheme -ne '便携包验证副本') { throw 'Active theme did not persist across restart.' }
        $runs += $result
    }
    [ordered]@{
        success=$true; version=$manifest.version; archiveSha256=$expected; fileCount=$fileCount
        selfContained=$true; globalDotnetPathsDisabled=$true; runs=$runs
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'package-result.json') -Encoding utf8
    Write-Output "PASS: verified archive, $fileCount files, bundled runtime, native rendering/save and restart persistence."
} finally {
    $prefix = $build.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($temp)
    if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -notmatch '^package-check-[0-9a-f]{32}$') { throw 'Unexpected package test cleanup target.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
