param(
    [string]$WeaselSource,
    [string]$BoostRoot,
    [string]$ReportPath,
    [switch]$RequireReady
)
$ErrorActionPreference = 'Stop'
$productRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $productRoot -Parent
if (-not $WeaselSource) { $WeaselSource = Join-Path $productRoot 'build/weasel-source/weasel-0.17.4' }
$WeaselSource = [IO.Path]::GetFullPath($WeaselSource)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$instances = @()
if (Test-Path -LiteralPath $vswhere) {
    $instances = @((& $vswhere -products '*' -format json -utf8 | ConvertFrom-Json))
}
$toolsets = @(
    foreach ($instance in $instances) {
        $root = Join-Path $instance.installationPath 'VC/Tools/MSVC'
        if (Test-Path -LiteralPath $root) {
            foreach ($version in Get-ChildItem -LiteralPath $root -Directory) {
                [pscustomobject]@{
                    Installation = $instance.installationPath
                    Version = $version.Name
                    HasCompilerX64 = Test-Path -LiteralPath (Join-Path $version.FullName 'bin/Hostx64/x64/cl.exe')
                    HasCompilerX86 = Test-Path -LiteralPath (Join-Path $version.FullName 'bin/Hostx64/x86/cl.exe')
                    HasAtlHeaders = Test-Path -LiteralPath (Join-Path $version.FullName 'atlmfc/include/atlbase.h')
                    HasAtlLibraryX64 = Test-Path -LiteralPath (Join-Path $version.FullName 'atlmfc/lib/x64/atls.lib')
                    HasAtlLibraryX86 = Test-Path -LiteralPath (Join-Path $version.FullName 'atlmfc/lib/x86/atls.lib')
                }
            }
        }
    }
)
$boostCandidates = @($BoostRoot, $env:BOOST_ROOT, (Join-Path $WeaselSource 'boost')) | Where-Object { $_ }
foreach ($base in @('C:\Libraries', 'C:\local', 'D:\Libraries', 'D:\Boost', 'D:\boost')) {
    if (Test-Path -LiteralPath $base) {
        $boostCandidates += $base
        $boostCandidates += @(Get-ChildItem -LiteralPath $base -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    }
}
$boost = @(
    foreach ($candidate in $boostCandidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'boost/version.hpp')) {
            $libDirectory = Join-Path $candidate 'stage/lib'
            [pscustomobject]@{
                Root = [IO.Path]::GetFullPath($candidate)
                HasSerializationHeaders = Test-Path -LiteralPath (Join-Path $candidate 'boost/serialization/vector.hpp')
                SerializationLibraries = @(if (Test-Path -LiteralPath $libDirectory) { Get-ChildItem -LiteralPath $libDirectory -Filter '*boost_serialization*.lib' | Select-Object -ExpandProperty Name })
            }
        }
    }
)
$checks = @(
    [pscustomobject]@{ Id = 'msvc'; Ready = @($toolsets | Where-Object HasCompilerX64).Count -gt 0; Detail = 'MSVC x64 compiler; an ordinary shell need not have cl.exe on PATH.' }
    [pscustomobject]@{ Id = 'atl'; Ready = @($toolsets | Where-Object { $_.HasCompilerX64 -and $_.HasAtlHeaders -and $_.HasAtlLibraryX64 }).Count -gt 0; Detail = 'ATL is used by the selected Weasel TSF/server sources; WTL alone does not supply ATL.' }
    [pscustomobject]@{ Id = 'boost'; Ready = @($boost | Where-Object { $_.HasSerializationHeaders -and $_.SerializationLibraries.Count -gt 0 }).Count -gt 0; Detail = 'Pass -BoostRoot for a nonstandard location. Matching architecture/toolset still requires a real build.' }
    [pscustomobject]@{ Id = 'weasel-source'; Ready = Test-Path -LiteralPath (Join-Path $WeaselSource 'WeaselTSF/WeaselTSF.vcxproj'); Detail = $WeaselSource }
    [pscustomobject]@{ Id = 'wtl'; Ready = Test-Path -LiteralPath (Join-Path $WeaselSource 'include/wtl/atlapp.h'); Detail = 'WTL header availability in the selected source tree.' }
    [pscustomobject]@{ Id = 'rime-runtime-x64'; Ready = Test-Path -LiteralPath (Join-Path $productRoot 'runtime/rime.dll'); Detail = 'Presence only; TSF integration and deployment remain unverified.' }
    [pscustomobject]@{ Id = 'rime-import-library'; Ready = Test-Path -LiteralPath (Join-Path $repoRoot '.cache/typeflow/reference/dist/lib/rime.lib'); Detail = 'Official prebuilt import library is available for staging, not yet a linked Weasel build.' }
    [pscustomobject]@{ Id = 'rime-headers'; Ready = Test-Path -LiteralPath (Join-Path $repoRoot '.cache/typeflow/reference/dist/include/rime_api.h'); Detail = 'Official public API headers are available for staging.' }
)
$blockers = @($checks | Where-Object { -not $_.Ready } | Select-Object -ExpandProperty Id)
$report = [pscustomobject]@{
    CheckedAt = [DateTimeOffset]::UtcNow.ToString('o')
    Scope = 'Windows host, selected Weasel 0.17.4 source, first x64 build; read-only inspection'
    PrerequisitesPresent = $blockers.Count -eq 0
    BuildVerified = $false
    SystemInputVerified = $false
    Blockers = $blockers
    Checks = $checks
    Toolsets = $toolsets
    BoostCandidates = $boost
    Notes = @('No dependencies were downloaded or installed.', 'No input method was registered and no user input settings were changed.', 'A passing preflight does not establish build success, IME registration, x86 compatibility, or application input correctness.')
}
if ($ReportPath) {
    $output = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
    [IO.File]::WriteAllText($output, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
$report | ConvertTo-Json -Depth 8
if ($RequireReady -and $blockers.Count -gt 0) { exit 1 }
