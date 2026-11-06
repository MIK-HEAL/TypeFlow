param([string]$NsisPath, [string]$VisualStudioRoot, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows IME packaging requires Windows.' }
$product = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$repo = Split-Path $product -Parent
[xml]$versions = Get-Content -LiteralPath (Join-Path $product 'Version.props') -Raw
$version = [string]$versions.Project.PropertyGroup.Version
$fileVersion = [string]$versions.Project.PropertyGroup.FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$') { throw 'Expected an Alpha version.' }
if (-not $NsisPath) { $NsisPath = Join-Path $repo '.cache/typeflow/native-deps/nsis-3.12/makensis.exe' }
if (-not (Test-Path -LiteralPath $NsisPath)) { throw 'NSIS not found. Pass -NsisPath or prepare the pinned project-local NSIS tool.' }
if (-not $VisualStudioRoot) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $VisualStudioRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $product "build/releases/ime-$version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$installer = Join-Path $output "TypeFlow-IME-$version-win-x64-setup.exe"
if (Test-Path -LiteralPath $installer) { throw "Installer already exists; select a fresh -OutputDirectory: $installer" }
$build = Join-Path $product ("build/ime-package-" + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $build 'payload'
$null = New-Item -ItemType Directory -Path $payload,$output -Force
$preview = Join-Path $product 'build/windows-ime/preview'
$manifest = Get-Content -LiteralPath (Join-Path $preview 'TYPEFLOW_PREVIEW.json') -Raw | ConvertFrom-Json
if ($manifest.ServiceClsid -ne '5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C') { throw 'Unexpected native preview identity.' }
foreach ($file in $manifest.Files) {
    if ([IO.Path]::GetFileName($file.Name) -ne $file.Name) { throw 'Invalid preview filename.' }
    $path = Join-Path $preview $file.Name
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Sha256) { throw "Preview binary changed: $($file.Name)" }
    Copy-Item -LiteralPath $path -Destination $payload
}
Copy-Item -LiteralPath (Join-Path $preview 'WEASEL-LICENSE.txt') -Destination $payload
Copy-Item -LiteralPath (Join-Path $product 'THIRD_PARTY_NOTICES.md') -Destination $payload
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $payload 'RIME-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $product 'LICENSE') -Destination (Join-Path $payload 'TYPEFLOW-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $preview 'TYPEFLOW_NATIVE_SOURCE.json') -Destination $payload
Copy-Item -LiteralPath (Join-Path $preview 'data') -Destination $payload -Recurse
$nativePackaging = Join-Path $product 'packaging/windows-ime'
Copy-Item -LiteralPath (Join-Path $nativePackaging 'README.txt') -Destination $payload
$skin = Join-Path $payload 'skin'
dotnet publish (Join-Path $product 'src/TypeFlow.Desktop/TypeFlow.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.11 -p:DebugType=None -p:DebugSymbols=false -o $skin -m:1
if ($LASTEXITCODE) { throw 'Self-contained skin settings publish failed.' }
foreach ($required in @('TypeFlow.Desktop.exe','wpfgfx_cor3.dll','coreclr.dll','themes/paper/theme.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $skin $required))) { throw "Missing skin host asset: $required" }
}
& (Join-Path $PSScriptRoot 'Write-PackageNotices.ps1') -AssetsFile (Join-Path $product 'src/TypeFlow.Desktop/obj/project.assets.json') -DepsFile (Join-Path $skin 'TypeFlow.Desktop.deps.json') -Destination $skin -RuntimeVersion '10.0.11' -AdditionalPackages @('Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.11')
$vcvars = Join-Path $VisualStudioRoot 'VC/Auxiliary/Build/vcvarsall.bat'
foreach ($path in @($build, $vcvars, $nativePackaging)) { if ($path -match '["%\r\n&|<>^!]') { throw "Unsupported command path: $path" } }
$command = Join-Path $build 'compile-launcher.cmd'
$lines = @(
    '@echo off', "call `"$vcvars`" x64", 'if errorlevel 1 exit /b %errorlevel%',
    "cl /nologo /EHsc /std:c++17 /MT /O2 /utf-8 /DUNICODE /D_UNICODE /DTYPEFLOW_VERSION=\`"$version\`" `"$nativePackaging\launcher.cpp`" /Fo`"$build\launcher.obj`" /Fe`"$payload\TypeFlowImeLauncher.exe`" /link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED /MANIFESTUAC:level='asInvoker' ole32.lib oleaut32.lib uuid.lib shell32.lib advapi32.lib user32.lib",
    'exit /b %errorlevel%'
)
[IO.File]::WriteAllLines($command, $lines, [Text.Encoding]::Default)
& $env:ComSpec /d /c $command
if ($LASTEXITCODE) { throw 'Native launcher compilation failed.' }
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName)
$inventory = @($files | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$package = [ordered]@{
    product='TypeFlow IME'; version=$version; architecture='win-x64'; systemIme=$true; selfContained=$true
    sourceCommit=(git -C $repo rev-parse HEAD).Trim(); sourceDirty=(-not [string]::IsNullOrWhiteSpace((git -C $repo status --porcelain | Out-String)))
    serviceClsid=$manifest.ServiceClsid; profileGuid=$manifest.ProfileGuid; renderer='TypeFlow WPF skin host with native Weasel fallback'
    distribution='local installer preview; public release inventory review pending'
    files=$inventory
}
$manifestPath = Join-Path $payload 'package-manifest.json'
[IO.File]::WriteAllText($manifestPath, ($package | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
# Generate explicit installation/deletion commands from the same file inventory.
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName)
$installLines = [Collections.Generic.List[string]]::new()
$uninstallLines = [Collections.Generic.List[string]]::new()
$directories = [Collections.Generic.HashSet[string]]::new()
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName)
    if ($relative -match '["$\r\n]') { throw "Unsafe installer filename: $relative" }
    $folder = [IO.Path]::GetDirectoryName($relative)
    $installLines.Add('SetOutPath "$INSTDIR' + $(if ($folder) { '\' + $folder }) + '"')
    $installLines.Add('File "' + $file.FullName + '"')
    $uninstallLines.Add('Delete /REBOOTOK "$INSTDIR\' + $relative + '"')
    while ($folder) { $null = $directories.Add($folder); $folder = [IO.Path]::GetDirectoryName($folder) }
}
foreach ($folder in @($directories | Sort-Object Length -Descending)) { $uninstallLines.Add('RMDir "$INSTDIR\' + $folder + '"') }
$installList = Join-Path $build 'install-files.nsh'
$uninstallList = Join-Path $build 'uninstall-files.nsh'
[IO.File]::WriteAllLines($installList, $installLines, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines($uninstallList, $uninstallLines, [Text.UTF8Encoding]::new($false))
& $NsisPath /V3 /INPUTCHARSET UTF8 "/DVERSION=$version" "/DFILE_VERSION=$fileVersion" "/DPAYLOAD=$payload" "/DINSTALLER_FILE=$installer" "/DINSTALL_FILES=$installList" "/DUNINSTALL_FILES=$uninstallList" (Join-Path $nativePackaging 'installer.nsi')
if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$installer.sha256", "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.Encoding]::ASCII)
Copy-Item -LiteralPath $manifestPath -Destination $output
Copy-Item -LiteralPath (Join-Path $nativePackaging 'README.txt') -Destination $output
# Include the exact original Weasel sources plus all transformations and installer sources.
$sourceArchive = Join-Path $output "TypeFlow-IME-$version-source.zip"
$sourceBundle = Join-Path $build 'source'
$null = New-Item -ItemType Directory -Path (Join-Path $sourceBundle '.cache/typeflow/reference'),(Join-Path $sourceBundle 'typeflow/integration'),(Join-Path $sourceBundle 'typeflow/packaging'),(Join-Path $sourceBundle 'typeflow/scripts') -Force
Copy-Item -LiteralPath (Join-Path $repo '.cache/typeflow/reference/weasel-0.17.4.zip') -Destination (Join-Path $sourceBundle '.cache/typeflow/reference')
Copy-Item -LiteralPath (Join-Path $product 'integration/weasel') -Destination (Join-Path $sourceBundle 'typeflow/integration') -Recurse
Copy-Item -LiteralPath $nativePackaging -Destination (Join-Path $sourceBundle 'typeflow/packaging') -Recurse
foreach ($script in @('Package-WindowsIme.ps1','Test-WindowsImeInstaller.ps1','Prepare-WindowsImeInstallerTools.ps1','Prepare-Runtime.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $sourceBundle 'typeflow/scripts')
}
Copy-Item -LiteralPath (Join-Path $product 'sources.json'),(Join-Path $product 'Version.props'),(Join-Path $product 'LICENSE'),(Join-Path $product 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $sourceBundle 'typeflow')
foreach ($folder in @('src','native','tests','themes','schema')) {
    $tree = Join-Path $product $folder
    if (-not (Test-Path -LiteralPath $tree)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $tree -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
        $target = Join-Path (Join-Path $sourceBundle 'typeflow') ([IO.Path]::GetRelativePath($product, $file.FullName))
        $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
Copy-Item -LiteralPath (Join-Path $product 'Directory.Build.props'),(Join-Path $product 'Avalonia.props') -Destination (Join-Path $sourceBundle 'typeflow')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Write-PackageNotices.ps1') -Destination (Join-Path $sourceBundle 'typeflow/scripts')
[IO.Compression.ZipFile]::CreateFromDirectory($sourceBundle, $sourceArchive)
[ordered]@{ installer=$installer; sha256=$hash; payload=$payload; source=$sourceArchive; systemInstalled=$false; lifecycleVerified=$false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'installer-build.json') -Encoding utf8
Write-Output "INSTALLER_CREATED: $installer"
