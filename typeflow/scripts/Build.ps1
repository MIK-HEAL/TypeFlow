param([switch]$SkipNative, [switch]$Test)
$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $product -Parent
Push-Location $repo
try {
    if (-not $SkipNative) {
        cmake -S typeflow/native -B typeflow/build/native -G 'Visual Studio 18 2026' -A x64
        if ($LASTEXITCODE) { throw 'Native configure failed.' }
        cmake --build typeflow/build/native --config Release
        if ($LASTEXITCODE) { throw 'Native build failed.' }
    }
    dotnet build typeflow/src/TypeFlow.Desktop/TypeFlow.Desktop.csproj -c Release -o typeflow/build/app -m:1 --ignore-failed-sources
    if ($LASTEXITCODE) { throw 'Desktop build failed.' }
    $bridge = Join-Path $product 'build/native/Release/typeflow_rime.dll'
    if (Test-Path -LiteralPath $bridge) { Copy-Item -LiteralPath $bridge -Destination (Join-Path $product 'build/app/typeflow_rime.dll') }
    if ($Test) {
        dotnet build typeflow/tests/TypeFlow.Tests/TypeFlow.Tests.csproj -c Release -m:1 --ignore-failed-sources
        if ($LASTEXITCODE) { throw 'Test build failed.' }
        dotnet run --project typeflow/tests/TypeFlow.Tests/TypeFlow.Tests.csproj -c Release --no-build -- $repo
        if ($LASTEXITCODE) { throw 'Tests failed.' }
        dotnet build typeflow/tests/TypeFlow.IntegrationTests/TypeFlow.IntegrationTests.csproj -c Release -o typeflow/build/integration-tests -m:1 --ignore-failed-sources
        if ($LASTEXITCODE) { throw 'Integration test build failed.' }
        Copy-Item -LiteralPath $bridge -Destination (Join-Path $product 'build/integration-tests/typeflow_rime.dll')
        dotnet typeflow/build/integration-tests/TypeFlow.IntegrationTests.dll $repo
        if ($LASTEXITCODE) { throw 'Integration tests failed.' }
    }
    Write-Output "Built: $product/build/app/TypeFlow.Desktop.exe"
} finally { Pop-Location }
