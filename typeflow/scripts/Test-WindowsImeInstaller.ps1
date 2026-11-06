param(
    [Parameter(Mandatory)][string]$Installer,
    [switch]$InstallCycle,
    [string]$ExpectedUserSid
)
$ErrorActionPreference = 'Stop'
$product = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$repo = Split-Path $product -Parent
$Installer = [IO.Path]::GetFullPath($Installer)
$output = Split-Path $Installer -Parent
$manifest = Get-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Raw | ConvertFrom-Json
$buildInfo = Get-Content -LiteralPath (Join-Path $output 'installer-build.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $buildInfo.sha256) { throw 'Installer checksum mismatch.' }
$evidence = Join-Path $product ('build/evidence/ime-installer-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $evidence -Force
$sevenZip = Join-Path $product 'build/windows-ime/source-preview/weasel-0.17.4/output/7z.exe'
$extracted = Join-Path $evidence 'extracted'
& $sevenZip x $Installer "-o$extracted" -y *> (Join-Path $evidence 'extraction.log')
if ($LASTEXITCODE) { throw 'Installer extraction failed.' }
function Assert-Files([string]$Root) {
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $Root $file.path))
        if (-not $path.StartsWith([IO.Path]::GetFullPath($Root).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Packaged file mismatch: $($file.path)" }
    }
}
Assert-Files $extracted
if (@(Get-ChildItem -LiteralPath $extracted -Recurse -File -Filter '*.ssf').Count) { throw 'User SSF fixture must not be bundled.' }
$result = [ordered]@{
    installer=$Installer; checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); extraction='passed'
    payloadHashes='passed'; systemIme=$manifest.systemIme; lifecycle='not executed'; userDataPreserved=$null
}
if ($InstallCycle) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Install-cycle testing must run in an explicitly elevated shell.' }
    if (-not $ExpectedUserSid -or $identity.User.Value -ne $ExpectedUserSid) { throw 'Refusing to test with another administrator user profile.' }
    $installedRoot = Join-Path ${env:ProgramW6432} 'TypeFlow/Ime'
    $userRoot = Join-Path $env:LOCALAPPDATA 'TypeFlow/Ime'
    $userSettings = Get-ItemProperty 'HKCU:\Software\TypeFlow\Ime' -ErrorAction SilentlyContinue
    if ($userSettings -and $userSettings.RimeUserDir) { $userRoot = $userSettings.RimeUserDir }
    $userHashes = @{}
    foreach ($name in @('default.custom.yaml','weasel.custom.yaml')) {
        $path = Join-Path $userRoot $name
        if (Test-Path -LiteralPath $path) { $userHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $registry = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\TypeFlowImePreview'
    function Install-Preview {
        $process = Start-Process -FilePath $Installer -ArgumentList '/S /NOSTART' -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode) { throw "Installer failed: $($process.ExitCode)" }
        $location = (Get-ItemProperty -LiteralPath $registry).InstallLocation
        if ($location -ne $installedRoot) { throw "Unexpected install location: $location" }
        Assert-Files $installedRoot
        if ((Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\TypeFlow\Ime').WeaselRoot -ne $installedRoot) { throw 'Native registration points to another directory.' }
    }
    Install-Preview
    # Copy the uninstaller before launching it so its real exit code can be checked.
    $uninstaller = Join-Path $evidence 'uninstall-test.exe'
    Copy-Item -LiteralPath (Join-Path $installedRoot 'Uninstall.exe') -Destination $uninstaller
    $process = Start-Process -FilePath $uninstaller -ArgumentList "/S _?=$installedRoot" -WorkingDirectory $evidence -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode) { throw "Uninstaller failed: $($process.ExitCode)" }
    if (Test-Path -LiteralPath $registry) { throw 'Uninstall entry remains.' }
    foreach ($key in @('HKLM:\SOFTWARE\Classes\CLSID\{5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C}','HKLM:\SOFTWARE\Classes\WOW6432Node\CLSID\{5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C}')) {
        if (Test-Path -LiteralPath $key) { throw 'TSF COM registration remains after uninstall.' }
    }
    foreach ($path in $userHashes.Keys) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $userHashes[$path]) { throw "User configuration changed: $path" }
    }
    Install-Preview
    foreach ($path in $userHashes.Keys) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $userHashes[$path]) { throw "Reinstall changed user configuration: $path" }
    }
    $result.lifecycle = 'install / uninstall / reinstall passed'
    $result.userDataPreserved = $true
    $result.installedDirectory = $installedRoot
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'installer-verification.json') -Encoding utf8
Write-Output "INSTALLER_VERIFIED: $evidence"
