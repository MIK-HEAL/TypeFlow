param([string]$Destination)
$ErrorActionPreference = 'Stop'
$productRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$repoRoot = Split-Path $productRoot -Parent
if (-not $Destination) { $Destination = Join-Path $productRoot 'build/windows-ime/source-preview' }
$destinationRoot = [IO.Path]::GetFullPath($Destination)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $productRoot 'build/windows-ime')).TrimEnd('\') + '\'
if (-not $destinationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Native source staging must remain inside typeflow/build/windows-ime.' }
$archive = Join-Path $repoRoot '.cache/typeflow/reference/weasel-0.17.4.zip'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'becda44927a96e9662fc1aca4d5df344c300efc0aded9b48e0f6c685072e067b') { throw 'Weasel archive checksum mismatch.' }
$source = Join-Path $destinationRoot 'weasel-0.17.4'
$marker = Join-Path $source 'TYPEFLOW_NATIVE_SOURCE.json'
$scriptHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if (Test-Path -LiteralPath $marker) {
    $existing = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($existing.PreparationScriptSha256 -ne $scriptHash) { throw 'Staged source uses another patch revision. Choose a fresh -Destination under typeflow/build/windows-ime.' }
    Write-Output $source
    exit 0
}
if (Test-Path -LiteralPath $source) { throw 'Unmarked source directory exists; refusing to overwrite local edits.' }
Expand-Archive -LiteralPath $archive -DestinationPath $destinationRoot

# Stable identities separate the prototype from any installed upstream Weasel.
$identities = [ordered]@{
    c_clsidTextService = '5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C'
    c_guidProfile = 'BE0C4C87-20C8-4E3B-B7DD-78053F194B73'
    c_guidLangBarItemButton = '60292F16-1467-476F-B171-6C23E4CFF010'
    c_guidDisplayAttributeInput = '57E69246-E409-4099-A4EC-9C03E9B933D7'
}
function Guid-Declaration([string]$Name, [string]$Value) {
    $parts = $Value.ToLowerInvariant().Split('-')
    $tail = $parts[3] + $parts[4]
    $bytes = for ($i = 0; $i -lt 16; $i += 2) { '0x' + $tail.Substring($i, 2) }
    return "static const GUID $Name = { 0x$($parts[0]), 0x$($parts[1]), 0x$($parts[2]), { $($bytes -join ', ') } };"
}
$replacements = [ordered]@{
    'Software\\WOW6432Node\\Rime\\Weasel' = 'Software\\WOW6432Node\\TypeFlow\\Ime'
    'Software\\Rime\\Weasel' = 'Software\\TypeFlow\\Ime'
    'Software\\Rime' = 'Software\\TypeFlow'
    '%APPDATA%\\Rime' = '%LOCALAPPDATA%\\TypeFlow\\Ime'
    'WeaselIPCWindow_1.0' = 'TypeFlowIPCWindow_1.0'
    'WeaselNamedPipe' = 'TypeFlowNamedPipe'
    'WeaselInputService' = 'TypeFlowInputService'
    'WeaselDeployerExclusiveMutex' = 'TypeFlowDeployerExclusiveMutex'
    'WeaselDeployerMutex' = 'TypeFlowDeployerMutex'
    'WeaselServer.exe' = 'TypeFlowImeServer.exe'
    'WeaselDeployer.exe' = 'TypeFlowImeDeployer.exe'
    'weaselx64.dll' = 'typeflowtipx64.dll'
    'weasel.dll' = 'typeflowtip.dll'
    'weasel.ime' = 'typeflowtip.ime'
    'rime.weasel' = 'rime.typeflow-ime'
    '小狼毫' = 'TypeFlow IME Preview'
}
$modified = [Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object { $_.Extension -in @('.cpp','.h','.rc','.rgs') -and $_.FullName -notlike '*\include\wtl\*' }) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $before = $text
    foreach ($item in $replacements.GetEnumerator()) {
        $text = [regex]::Replace($text, [regex]::Escape($item.Key), [Text.RegularExpressions.MatchEvaluator]{ param($match) [string]$item.Value }, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    if ($file.Name -eq 'Globals.cpp' -or $file.Name -eq 'imesetup.cpp') {
        foreach ($pair in $identities.GetEnumerator()) {
            $pattern = 'static const GUID ' + [regex]::Escape($pair.Key) + '\s*=\s*\{[\s\S]*?\};'
            if ([regex]::IsMatch($text, $pattern)) { $text = [regex]::Replace($text, $pattern, (Guid-Declaration $pair.Key $pair.Value)) }
        }
    }
    if ($file.Name -eq 'imesetup.cpp') {
        $pattern = '#define PSZTITLE_HANS[\s\S]*?#define ILOT_UNINSTALL'
        $titles = "#define PSZTITLE_HANS L`"0804:{$($identities.c_clsidTextService)}{$($identities.c_guidProfile)}`"`n#define PSZTITLE_HANT L`"0404:{$($identities.c_clsidTextService)}{$($identities.c_guidProfile)}`"`n#define ILOT_UNINSTALL"
        if (-not [regex]::IsMatch($text, $pattern)) { throw 'Unexpected setup identity definitions.' }
        $text = [regex]::Replace($text, $pattern, $titles)
        # The upstream installer constructs filenames from this stem; keep it separate from upstream files.
        $text = $text.Replace('L"weasel"', 'L"typeflowtip"').Replace('L"\\weasel"', 'L"\\typeflowtip"').Replace('L"weaselARM64X"', 'L"typeflowtipARM64X"')
        # A preview install does not opt the machine into crash dump collection.
        $text = [regex]::Replace($text, '  // https://learn\.microsoft\.com/zh-cn/windows/win32/wer/collecting-user-mode-dumps[\s\S]*?(?=  if \(retval\))', '')
        $text = [regex]::Replace($text, '  // delete WER register,[\s\S]*?(?=  if \(retval\))', '')
        # This preview ships TSF only; do not remove a legacy IMM registration.
        $text = $text.Replace('  uninstall_ime_file(L".ime", silent, &register_ime);', '  // No legacy IMM registration is installed by this preview.')
        # Upstream ignores regsvr32 failure; propagate it before reporting success.
        $text = [regex]::Replace($text, '    WaitForSingleObject\(shExInfo.hProcess, INFINITE\);\r?\n    CloseHandle\(shExInfo.hProcess\);', "    WaitForSingleObject(shExInfo.hProcess, INFINITE);`n    DWORD exitCode = 1;`n    GetExitCodeProcess(shExInfo.hProcess, &exitCode);`n    CloseHandle(shExInfo.hProcess);`n    if (exitCode != 0) return 1;")
    }
    if ($file.Name -eq 'Register.cpp') { $text = $text.Replace('text_service_desc.size() * sizeof(wchar_t)', 'static_cast<ULONG>(text_service_desc.size())') }
    if ($file.Name -eq 'WeaselServerApp.cpp') { $text = $text.Replace('  win_sparkle_init();', '  // Preview builds are updated explicitly by TypeFlow; upstream auto-update is disabled.') }
    if ($file.Name -eq 'WeaselConstants.h') { $text = $text.Replace('"Weasel"', '"TypeFlowIme"') }
    if ($text -ne $before) {
        [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
        $modified.Add([IO.Path]::GetRelativePath($source, $file.FullName))
    }
}
# Preserve the upstream API/build identifiers while changing only resulting filenames.
foreach ($file in Get-ChildItem -LiteralPath $source -Filter '*.vcxproj' -File -Recurse) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $text = $text.Replace('weasel$(Platform)$(TargetExt)', 'typeflowtip$(Platform)$(TargetExt)').Replace('weasel$(TargetExt)', 'typeflowtip$(TargetExt)')
    $text = $text.Replace('<TargetName>weasel</TargetName>', '<TargetName>typeflowtip</TargetName>').Replace('<TargetName>weasel$(Platform)</TargetName>', '<TargetName>typeflowtip$(Platform)</TargetName>')
    if ($file.Directory.Name -eq 'WeaselServer') { $text = $text.Replace('$(ProjectName).exe', 'TypeFlowImeServer.exe') }
    if ($file.Directory.Name -eq 'WeaselDeployer') { $text = $text.Replace('$(ProjectName)$(TargetExt)', 'TypeFlowImeDeployer$(TargetExt)') }
    if ($file.Directory.Name -eq 'WeaselSetup') { $text = $text.Replace('$(ProjectName)$(TargetExt)', 'TypeFlowImeSetup$(TargetExt)') }
    [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
}
$manifest = [ordered]@{
    PatchVersion = 3; PreparationScriptSha256 = $scriptHash
    Upstream = 'rime/weasel'; Version = '0.17.4'; Commit = '9cc96e20dc71b80876b12f689bb5863c76c2a7ed'
    Mode = 'TypeFlow native IME preview, upstream candidate renderer, isolated registration and user data'
    ServiceClsid = $identities.c_clsidTextService; ProfileGuid = $identities.c_guidProfile
    RegistryKey = 'Software\TypeFlow\Ime'; PipeName = 'TypeFlowNamedPipe'
    ModifiedSources = $modified; RegistrationExecuted = $false
}
[IO.File]::WriteAllText($marker, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output $source
