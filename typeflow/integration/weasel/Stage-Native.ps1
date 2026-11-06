param([string]$Source)
$ErrorActionPreference = 'Stop'
$productRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Source) { $Source = Join-Path $productRoot 'build/windows-ime/source-preview/weasel-0.17.4' }
$Source = [IO.Path]::GetFullPath($Source)
$marker = Get-Content -LiteralPath (Join-Path $Source 'TYPEFLOW_NATIVE_SOURCE.json') -Raw | ConvertFrom-Json
if ($marker.ServiceClsid -ne '5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C' -or $marker.RegistryKey -ne 'Software\TypeFlow\Ime') { throw 'Source identity mismatch.' }
$setup = [IO.File]::ReadAllText((Join-Path $Source 'WeaselSetup/imesetup.cpp'))
foreach ($forbidden in @('L"\\weasel"','SetRegKeyValue(HKEY_LOCAL_MACHINE, WEASEL_WER_KEY','uninstall_ime_file(L".ime"')) {
    if ($setup.Contains($forbidden)) { throw "Unsafe upstream setup operation remains: $forbidden" }
}
if (-not $setup.Contains('if (exitCode != 0) return 1;')) { throw 'Registration exit code must be checked.' }
$uninstallStart = $setup.IndexOf('int uninstall_ime_file(')
$uninstall = $setup.Substring($uninstallStart, $setup.IndexOf('// 注册IME输入法', $uninstallStart) - $uninstallStart)
if ($uninstall.IndexOf('retval += func(imePath, false, true, false, false, silent);') -lt $uninstall.IndexOf('Wow64DisableWow64FsRedirection')) { throw '64-bit unregister must run after disabling WOW64 redirection.' }
$ui = [IO.File]::ReadAllText((Join-Path $Source 'WeaselUI/WeaselUI.cpp'))
if (-not $ui.Contains('TypeFlowEndpoint.h') -or -not $ui.Contains('Endpoint::Instance().Attach')) { throw 'Missing skin renderer integration or recovery hook.' }
$server = [IO.File]::ReadAllText((Join-Path $Source 'WeaselServer/WeaselServerApp.cpp'))
if ($server.Contains('  win_sparkle_init();')) { throw 'Preview must not activate upstream auto-update.' }
$files = [ordered]@{
    'typeflowtipx64.dll' = 0x8664
    'typeflowtip.dll' = 0x014c
    'TypeFlowImeServer.exe' = 0x8664
    'TypeFlowImeDeployer.exe' = 0x8664
    'TypeFlowImeSetup.exe' = 0x014c
    'WinSparkle.dll' = 0x8664
}
function Get-PeMachine([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not a PE binary: $Path" }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
        return $reader.ReadUInt16()
    } finally { $reader.Dispose(); $stream.Dispose() }
}
foreach ($file in $files.GetEnumerator()) {
    $path = Join-Path $Source "output/$($file.Key)"
    if ((Get-PeMachine $path) -ne $file.Value) { throw "Wrong architecture: $path" }
}
$runtime = Join-Path $productRoot 'runtime/rime.dll'
if ((Get-PeMachine $runtime) -ne 0x8664) { throw 'Rime runtime must be x64.' }
$stage = Join-Path $productRoot 'build/windows-ime/preview'
if ((Test-Path -LiteralPath $stage) -and -not (Test-Path -LiteralPath (Join-Path $stage 'TYPEFLOW_PREVIEW.json'))) { throw 'Unmarked preview directory exists; refusing to overwrite.' }
$null = New-Item -ItemType Directory -Path $stage -Force
foreach ($name in $files.Keys) { Copy-Item -LiteralPath (Join-Path $Source "output/$name") -Destination $stage -Force }
Copy-Item -LiteralPath $runtime -Destination $stage -Force
$data = Join-Path $stage 'data'
$null = New-Item -ItemType Directory -Path $data -Force
Copy-Item -Path (Join-Path $productRoot 'rime-data/*') -Destination $data -Recurse -Force
Copy-Item -LiteralPath (Join-Path $Source 'output/data/weasel.yaml') -Destination $data -Force
Copy-Item -LiteralPath (Join-Path $Source 'LICENSE.txt') -Destination (Join-Path $stage 'WEASEL-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'THIRD_PARTY_NOTICES.md') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $Source 'TYPEFLOW_NATIVE_SOURCE.json') -Destination $stage -Force
$manifest = [ordered]@{
    Product = 'TypeFlow IME Preview'; PreparedAt = [DateTimeOffset]::UtcNow.ToString('o')
    Source = $Source; ServiceClsid = $marker.ServiceClsid; ProfileGuid = $marker.ProfileGuid
    RegistryKey = $marker.RegistryKey; UserData = '%LOCALAPPDATA%\TypeFlow\Ime'
    Renderer = 'TypeFlow optional skin host with upstream native candidate fallback'
    BinaryArchitectureChecked = $true; RegistrationExecuted = $false; SystemInputVerified = $false
    Files = @(Get-ChildItem -LiteralPath $stage -File | Where-Object Extension -in @('.dll','.exe') | ForEach-Object {
        [ordered]@{ Name = $_.Name; Machine = ('0x{0:x4}' -f (Get-PeMachine $_.FullName)); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
[IO.File]::WriteAllText((Join-Path $stage 'TYPEFLOW_PREVIEW.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output "NATIVE_PREVIEW_STAGED: $stage"
