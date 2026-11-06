param([switch]$Test, [switch]$RimeTest)
$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $product -Parent
if ($RimeTest -and -not $IsWindows) { throw '-RimeTest currently uses the prepared Windows development runtime.' }
Push-Location $repo
try {
    # Lock files fix both direct and transitive package versions and content hashes.
    $project = if ($Test -or $RimeTest) { 'typeflow/tests/TypeFlow.AvaloniaTests/TypeFlow.AvaloniaTests.csproj' } else { 'typeflow/src/TypeFlow.CrossPlatform/TypeFlow.CrossPlatform.csproj' }
    dotnet restore $project --locked-mode
    if ($LASTEXITCODE) { throw 'Locked Avalonia dependency restore failed.' }
    dotnet build typeflow/src/TypeFlow.CrossPlatform/TypeFlow.CrossPlatform.csproj -c Release -o typeflow/build/cross-platform -m:1 --no-restore
    if ($LASTEXITCODE) { throw 'Cross-platform workbench build failed.' }
    $name = if ($IsWindows) { 'typeflow_rime.dll' } elseif ($IsMacOS) { 'libtypeflow_rime.dylib' } else { 'libtypeflow_rime.so' }
    $candidates = @(
        (Join-Path $product "build/native-portable/Release/$name"),
        (Join-Path $product "build/native-portable/$name")
    )
    $bridge = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($bridge) { Copy-Item -LiteralPath $bridge -Destination (Join-Path $product "build/cross-platform/$name") }
    if ($RimeTest -and (-not $bridge -or -not (Test-Path -LiteralPath (Join-Path $product 'runtime/rime.dll')))) {
        throw 'Prepare the Windows runtime and run Build-Portable.ps1 -Native before using -RimeTest.'
    }
    if ($Test -or $RimeTest) {
        dotnet build typeflow/tests/TypeFlow.AvaloniaTests/TypeFlow.AvaloniaTests.csproj -c Release -o typeflow/build/avalonia-tests -m:1 --no-restore
        if ($LASTEXITCODE) { throw 'Avalonia test build failed.' }
        $testArguments = @($repo)
        if ($RimeTest) { $testArguments += '--rime' }
        dotnet typeflow/build/avalonia-tests/TypeFlow.AvaloniaTests.dll @testArguments
        if ($LASTEXITCODE) { throw 'Avalonia checks failed.' }
    }
    Write-Output "Built: $product/build/cross-platform/TypeFlow.CrossPlatform.dll"
} finally { Pop-Location }
