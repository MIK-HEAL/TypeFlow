param([Parameter(Mandatory)][string]$Source)
$ErrorActionPreference = 'Stop'
$productRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Source = [IO.Path]::GetFullPath($Source)
$allowed = [IO.Path]::GetFullPath((Join-Path $productRoot 'build/windows-ime')).TrimEnd('\') + '\'
if (-not $Source.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build fixes must stay within the native staging directory.' }
$marker = Get-Content -LiteralPath (Join-Path $Source 'TYPEFLOW_NATIVE_SOURCE.json') -Raw | ConvertFrom-Json
if ($marker.ServiceClsid -ne '5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C') { throw 'Unexpected native source identity.' }
$modified = @()
foreach ($resource in @('WeaselTSF/WeaselTSF.rc','WeaselServer/WeaselServer.rc','WeaselSetup/WeaselSetup.rc','WeaselDeployer/WeaselDeployer.rc')) {
    $path = Join-Path $Source $resource
    $text = [IO.File]::ReadAllText($path).Replace('afxres.h', 'winres.h').Replace('VALUE "FileVersion", FILE_VERSION', 'VALUE "FileVersion", STRZ(FILE_VERSION)')
    if (-not $text.Contains('#pragma code_page(65001)')) { $text = "#pragma code_page(65001)`n" + $text }
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    $modified += $resource
}
$path = Join-Path $Source 'WeaselSetup/imesetup.cpp'
$text = [IO.File]::ReadAllText($path)
$start = $text.IndexOf('int uninstall_ime_file(')
$end = $text.IndexOf('// 注册IME输入法', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Unexpected uninstall function layout.' }
$block = $text.Substring($start, $end - $start)
$call = '    retval += func(imePath, false, true, false, false, silent);'
$callAt = $block.IndexOf($call)
$redirectAt = $block.IndexOf('Wow64DisableWow64FsRedirection')
if ($callAt -lt 0 -or $redirectAt -lt 0) { throw 'Missing architecture-specific unregister call.' }
if ($callAt -lt $redirectAt) {
    # regsvr32 must be resolved while WOW64 redirection is disabled, after the
    # 32-bit DLL is removed. Otherwise the old code runs 32-bit regsvr32 twice.
    $block = $block.Replace($call, '')
    $block = $block.Replace('    if (is_arm64_machine()) {', $call + "`n    if (is_arm64_machine()) {")
    $text = $text.Substring(0, $start) + $block + $text.Substring($end)
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}
if ($block.IndexOf($call) -lt $block.IndexOf('Wow64DisableWow64FsRedirection')) { throw 'Unregister architecture patch failed.' }
$modified += 'WeaselSetup/imesetup.cpp'
$path = Join-Path $Source 'WeaselServer/WeaselServerApp.cpp'
$text = [IO.File]::ReadAllText($path)
$text = $text.Replace('std::bind(execute, dir / L"TypeFlowImeDeployer.exe", std::wstring()));',
    '[dir] { return std::filesystem::exists(dir / L"skin" / L"TypeFlow.Desktop.exe") ? execute(dir / L"skin" / L"TypeFlow.Desktop.exe", std::wstring(L"--settings")) : execute(dir / L"TypeFlowImeDeployer.exe", std::wstring()); });')
[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
$modified += 'WeaselServer/WeaselServerApp.cpp'
[ordered]@{ patchVersion=1; files=@($modified | ForEach-Object { [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $Source $_) -Algorithm SHA256).Hash.ToLowerInvariant() } }) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Source 'TYPEFLOW_BUILD_PATCHES.json') -Encoding utf8
