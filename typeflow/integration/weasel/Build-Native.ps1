param(
    [string]$BoostRoot,
    [string]$VisualStudioRoot,
    [switch]$BuildBoostOnly,
    [switch]$SkipBoost,
    [int]$Jobs = 4
)
$ErrorActionPreference = 'Stop'
$productRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$repoRoot = Split-Path $productRoot -Parent
if (-not $BoostRoot) { $BoostRoot = Join-Path $repoRoot '.cache/typeflow/native-deps/boost_1_92_0' }
$BoostRoot = [IO.Path]::GetFullPath($BoostRoot)
if (-not $VisualStudioRoot) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $VisualStudioRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
if (-not $VisualStudioRoot) { throw 'MSVC Build Tools not found.' }
$vcvars = Join-Path $VisualStudioRoot 'VC/Auxiliary/Build/vcvarsall.bat'
$msbuild = Join-Path $VisualStudioRoot 'MSBuild/Current/Bin/MSBuild.exe'
foreach ($required in @($vcvars, $msbuild, (Join-Path $BoostRoot 'bootstrap.bat'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing prerequisite: $required" }
}
if ($Jobs -lt 1 -or $Jobs -gt 32) { throw 'Jobs must be between 1 and 32.' }
# These values become cmd arguments. Reject metacharacters rather than interpreting them.
foreach ($path in @($BoostRoot, $VisualStudioRoot, $productRoot)) {
    if ($path -match '["%\r\n&|<>^!]') { throw "Unsupported command metacharacter in path: $path" }
}
$buildRoot = Join-Path $productRoot 'build/windows-ime'
$null = New-Item -ItemType Directory -Path $buildRoot -Force
$boostCommand = Join-Path $buildRoot 'build-boost.cmd'
$boostLines = @(
    '@echo off', 'setlocal', "call `"$vcvars`" x64", 'if errorlevel 1 exit /b %errorlevel%',
    "cd /d `"$BoostRoot`"", 'if not exist b2.exe call bootstrap.bat', 'if errorlevel 1 exit /b %errorlevel%'
)
foreach ($bits in @(64, 32)) {
    $boostLines += ".\b2.exe -j$Jobs toolset=msvc-14.5 architecture=x86 address-model=$bits variant=release link=static runtime-link=static threading=multi --with-filesystem --with-json --with-locale --with-regex --with-serialization --with-thread define=BOOST_USE_WINAPI_VERSION=0x0603 stage"
    $boostLines += 'if errorlevel 1 exit /b %errorlevel%'
}
$boostLines += 'exit /b 0'
[IO.File]::WriteAllLines($boostCommand, $boostLines, [Text.Encoding]::Default)
if (-not $SkipBoost) {
    & $env:ComSpec /d /c $boostCommand
    if ($LASTEXITCODE -ne 0) { throw "Boost build failed ($LASTEXITCODE)." }
}
foreach ($bits in @(64, 32)) {
    $library = Join-Path $BoostRoot "stage/lib/libboost_serialization-vc145-mt-s-x$bits-1_92.lib"
    if (-not (Test-Path -LiteralPath $library)) { throw "Missing Boost library: $library" }
}
if ($BuildBoostOnly) { return }

$source = & (Join-Path $PSScriptRoot 'Prepare-NativeSource.ps1')
$source = [string]($source | Select-Object -Last 1)
$source = $source.Trim()
# These two resource scripts only need Windows resource constants, not MFC.
# winres.h is supplied by the Windows SDK already used by the project.
foreach ($resource in @('WeaselTSF/WeaselTSF.rc','WeaselServer/WeaselServer.rc','WeaselSetup/WeaselSetup.rc','WeaselDeployer/WeaselDeployer.rc')) {
    $path = Join-Path $source $resource
    $text = [IO.File]::ReadAllText($path).Replace('afxres.h', 'winres.h').Replace('VALUE "FileVersion", FILE_VERSION', 'VALUE "FileVersion", STRZ(FILE_VERSION)')
    if (-not $text.Contains('#pragma code_page(65001)')) { $text = "#pragma code_page(65001)`n" + $text }
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}
$reference = Join-Path $repoRoot '.cache/typeflow/reference'
$include = Join-Path $source 'librime/include'
$null = New-Item -ItemType Directory -Path $include -Force
Copy-Item -Path (Join-Path $reference 'dist/include/*') -Destination $include -Recurse -Force
Copy-Item -LiteralPath (Join-Path $reference 'dist/lib/rime.lib') -Destination (Join-Path $source 'lib64/rime.lib') -Force
$props = [IO.File]::ReadAllText((Join-Path $source 'weasel.props.template'))
$values = @{
    BOOST_ROOT = [Security.SecurityElement]::Escape($BoostRoot)
    PLATFORM_TOOLSET = 'v145'; VERSION_MAJOR = '0'; VERSION_MINOR = '17'; VERSION_PATCH = '4'
    PRODUCT_VERSION = '0.17.4.0'; FILE_VERSION = '0.17.4.0'
}
foreach ($item in $values.GetEnumerator()) { $props = $props.Replace('$' + $item.Key, $item.Value) }
[IO.File]::WriteAllText((Join-Path $source 'weasel.props'), $props, [Text.UTF8Encoding]::new($false))
# Use individual projects: upstream build.bat/xbuild.bat stop an installed Weasel server.
$targets = @(
    @('WeaselTSF','x64'), @('WeaselServer','x64'), @('WeaselDeployer','x64'),
    @('WeaselTSF','Win32'), @('WeaselSetup','Win32')
)
foreach ($target in $targets) {
    $project = Join-Path $source "$($target[0])/$($target[0]).vcxproj"
    & $msbuild $project /nologo /m:$Jobs /v:minimal /p:Configuration=Release "/p:Platform=$($target[1])" "/p:SolutionDir=$source\" /p:PlatformToolset=v145
    if ($LASTEXITCODE -ne 0) { throw "Native build failed: $($target -join ' / ') ($LASTEXITCODE)." }
}
Write-Output "NATIVE_BUILD_COMPLETED: $(Join-Path $source 'output')"
