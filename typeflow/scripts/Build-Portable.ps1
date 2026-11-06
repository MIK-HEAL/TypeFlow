param([switch]$Native, [switch]$Test)
$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $product -Parent
Push-Location $repo
try {
    dotnet build typeflow/src/TypeFlow.Cli/TypeFlow.Cli.csproj -c Release -o typeflow/build/cli -m:1 --ignore-failed-sources
    if ($LASTEXITCODE) { throw 'CLI build failed.' }
    # Build every portable component, including services that the CLI does not instantiate.
    dotnet build typeflow/src/TypeFlow.Themes/TypeFlow.Themes.csproj -c Release -m:1 --ignore-failed-sources
    if ($LASTEXITCODE) { throw 'Theme service build failed.' }
    if ($Native) {
        cmake -S typeflow/native -B typeflow/build/native-portable
        if ($LASTEXITCODE) { throw 'Native configure failed.' }
        cmake --build typeflow/build/native-portable --config Release
        if ($LASTEXITCODE) { throw 'Native build failed.' }
        $name = if ($IsWindows) { 'typeflow_rime.dll' } elseif ($IsMacOS) { 'libtypeflow_rime.dylib' } else { 'libtypeflow_rime.so' }
        $candidates = @(
            (Join-Path $product "build/native-portable/Release/$name"),
            (Join-Path $product "build/native-portable/$name")
        )
        $bridge = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $bridge) { throw "Native output missing: $name" }
        Copy-Item -LiteralPath $bridge -Destination (Join-Path $product "build/cli/$name")
    }
    if ($Test) {
        dotnet build typeflow/tests/TypeFlow.Tests/TypeFlow.Tests.csproj -c Release -m:1 --ignore-failed-sources
        if ($LASTEXITCODE) { throw 'Portable test build failed.' }
        dotnet run --project typeflow/tests/TypeFlow.Tests/TypeFlow.Tests.csproj -c Release --no-build -- $repo
        if ($LASTEXITCODE) { throw 'Portable tests failed.' }
    }
    Write-Output "Built: $product/build/cli/TypeFlow.Cli.dll"
} finally { Pop-Location }
