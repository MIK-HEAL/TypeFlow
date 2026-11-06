param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The first Alpha user package targets Windows x64.' }
$product = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$repo = Split-Path $product -Parent
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $product 'Version.props') -Raw
$version = [string]$versionProps.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$') { throw 'Expected an Alpha semantic version.' }
$name = "TypeFlow-$version-win-x64"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $product "build/releases/$version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$zip = Join-Path $output "$name.zip"
if (Test-Path -LiteralPath $zip) { throw "Package already exists; choose another -OutputDirectory: $zip" }
$stage = Join-Path $product ("build/package-" + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage $name
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Push-Location $repo
try {
    dotnet publish typeflow/src/TypeFlow.CrossPlatform/TypeFlow.CrossPlatform.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -o $payload -m:1
    if ($LASTEXITCODE) { throw 'Self-contained publish failed.' }
    foreach ($required in @('TypeFlow.CrossPlatform.exe', 'hostfxr.dll', 'coreclr.dll', 'libSkiaSharp.dll', 'themes/paper/theme.json', 'themes/night/theme.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing published asset: $required" }
    }
    Move-Item -LiteralPath (Join-Path $payload 'TypeFlow.CrossPlatform.exe') -Destination (Join-Path $payload 'TypeFlow.exe')
    Copy-Item -LiteralPath (Join-Path $product 'LICENSE') -Destination (Join-Path $payload 'LICENSE')
    foreach ($file in @('README.txt', 'ADVANCED.txt')) {
        (Get-Content -LiteralPath (Join-Path $product "packaging/$file") -Raw).Replace('{{VERSION}}', $version) |
            Set-Content -LiteralPath (Join-Path $payload $file) -Encoding utf8
    }
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'TypeFlow.CrossPlatform.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    if (-not $runtimeVersion) { throw 'Published output is not self-contained.' }
    & (Join-Path $PSScriptRoot 'Write-PackageNotices.ps1') -AssetsFile (Join-Path $product 'src/TypeFlow.CrossPlatform/obj/project.assets.json') -DepsFile (Join-Path $payload 'TypeFlow.CrossPlatform.deps.json') -Destination $payload -RuntimeVersion $runtimeVersion
    $commit = (git rev-parse HEAD).Trim()
    $dirty = -not [string]::IsNullOrWhiteSpace((git status --porcelain | Out-String))
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path=[IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/')
            size=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    [ordered]@{
        formatVersion=1; product='TypeFlow'; version=$version; runtime='win-x64'; dotnetRuntime=$runtimeVersion
        selfContained=$true; rimeBundled=$false; systemIme=$false; sourceCommit=$commit; sourceDirty=$dirty
        builtAtUtc=[DateTime]::UtcNow.ToString('o'); files=$files
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Encoding utf8
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    (Get-Content -LiteralPath (Join-Path $product 'packaging/RELEASE_NOTES.md') -Raw).Replace('{{VERSION}}', $version) |
        Set-Content -LiteralPath (Join-Path $output 'RELEASE_NOTES.md') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $payload 'package-manifest.json') -Destination (Join-Path $output 'package-manifest.json')
    Write-Output "Created: $zip"
    Write-Output "SHA256: $hash"
} finally {
    Pop-Location
    $prefix = [IO.Path]::GetFullPath((Join-Path $product 'build')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($stage)
    if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -notmatch '^package-[0-9a-f]{32}$') { throw 'Unexpected package staging path.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
