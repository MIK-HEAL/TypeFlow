param([ValidateSet('Plan','Install','Uninstall','Start','Stop','Deploy')][string]$Action = 'Plan')
$ErrorActionPreference = 'Stop'
$productRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stage = Join-Path $productRoot 'build/windows-ime/preview'
$manifest = Get-Content -LiteralPath (Join-Path $stage 'TYPEFLOW_PREVIEW.json') -Raw | ConvertFrom-Json
if ($manifest.ServiceClsid -ne '5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C' -or $manifest.RegistryKey -ne 'Software\TypeFlow\Ime') { throw 'Unexpected preview identity.' }
foreach ($file in $manifest.Files) {
    if ([IO.Path]::GetFileName($file.Name) -ne $file.Name) { throw 'Invalid manifest filename.' }
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage $file.Name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $file.Sha256) { throw "Preview binary changed: $($file.Name)" }
}
$setup = Join-Path $stage 'TypeFlowImeSetup.exe'
$server = Join-Path $stage 'TypeFlowImeServer.exe'
function Initialize-PreviewUserData {
    $userData = Join-Path $env:LOCALAPPDATA 'TypeFlow/Ime'
    $settings = Get-ItemProperty -LiteralPath 'HKCU:\Software\TypeFlow\Ime' -ErrorAction SilentlyContinue
    if ($settings -and $settings.RimeUserDir) { $userData = $settings.RimeUserDir }
    $null = New-Item -ItemType Directory -Path $userData -Force
    $custom = Join-Path $userData 'default.custom.yaml'
    # Rime may create an empty placeholder on first deployment. Preserve any user content.
    if (-not (Test-Path -LiteralPath $custom) -or (Get-Item -LiteralPath $custom).Length -eq 0) {
        Copy-Item -LiteralPath (Join-Path $stage 'data/default.custom.yaml') -Destination $custom -Force
    }
}
$plan = [ordered]@{
    Name = 'TypeFlow IME Preview'; Directory = $stage
    ServiceClsid = $manifest.ServiceClsid; ProfileGuid = $manifest.ProfileGuid
    SystemFiles = @("$env:WINDIR\System32\typeflowtip.dll", "$env:WINDIR\SysWOW64\typeflowtip.dll")
    Registry = @('HKLM\SOFTWARE\TypeFlow\Ime (32-bit view)', 'HKCR\CLSID\{5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C} (32/64-bit views)', 'Windows TSF profiles for this CLSID', 'HKCU\Software\TypeFlow\Ime')
    UserData = '%LOCALAPPDATA%\TypeFlow\Ime'
    AutoStart = $false; DefaultInputOverride = $false; LegacyImm = $false
    Install = 'TypeFlowImeSetup.exe /s'; Uninstall = 'TypeFlowImeSetup.exe /u'
    UninstallPreservesUserData = $true
}
if ($Action -eq 'Plan') { $plan | ConvertTo-Json -Depth 4; return }
if ($Action -in @('Install','Uninstall')) {
    if ($Action -eq 'Install') {
        foreach ($destination in $plan.SystemFiles) {
            if (Test-Path -LiteralPath $destination) { throw "An existing native preview must be reviewed or uninstalled first: $destination" }
        }
    }
    # The native setup owns elevation and architecture-specific regsvr32 calls.
    # No upstream install.bat, automatic startup, or default input override is used.
    $argument = if ($Action -eq 'Install') { '/s' } else { '/u' }
    $operation = Start-Process -FilePath $setup -ArgumentList $argument -WorkingDirectory $stage -WindowStyle Hidden -PassThru -Wait
    if ($operation.ExitCode -ne 0) { throw "$Action failed: exit code $($operation.ExitCode)." }
    if ($Action -eq 'Install') {
        $userKey = 'HKCU:\Software\TypeFlow\Ime'
        $null = New-Item -Path $userKey -Force
        $null = New-ItemProperty -LiteralPath $userKey -Name Hant -Value 0 -PropertyType DWord -Force
        Initialize-PreviewUserData
    }
    $receipt = [ordered]@{ Action = $Action; At = [DateTimeOffset]::UtcNow.ToString('o'); ExitCode = $operation.ExitCode; Plan = $plan; SystemInputVerified = $false }
    $receiptPath = Join-Path $productRoot 'build/evidence/native-ime-registration.json'
    $null = New-Item -ItemType Directory -Path (Split-Path $receiptPath -Parent) -Force
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Write-Output "$Action completed; real application input still requires verification."
    return
}
if ($Action -eq 'Deploy') {
    Initialize-PreviewUserData
    $operation = Start-Process -FilePath (Join-Path $stage 'TypeFlowImeDeployer.exe') -ArgumentList '/deploy' -WorkingDirectory $stage -WindowStyle Hidden -PassThru -Wait
    if ($operation.ExitCode -ne 0) { throw "Deployment failed: $($operation.ExitCode)" }
    return
}
if ($Action -eq 'Start') {
    Initialize-PreviewUserData
    Start-Process -FilePath $server -WorkingDirectory $stage -WindowStyle Hidden
} else {
    # /q addresses only the isolated TypeFlow IPC endpoint.
    $operation = Start-Process -FilePath $server -ArgumentList '/q' -WorkingDirectory $stage -WindowStyle Hidden -PassThru -Wait
    if ($operation.ExitCode -ne 0) { throw "Server stop failed: $($operation.ExitCode)" }
}
