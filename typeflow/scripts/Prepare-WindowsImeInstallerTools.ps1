$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $product -Parent
$cache = Join-Path $repo '.cache/typeflow/native-deps'
$null = New-Item -ItemType Directory -Path $cache -Force
$archive = Join-Path $cache 'nsis-3.12.zip'
$expected = '56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f'
# Pinned portable tool; checksum is also published in the MacPorts NSIS Portfile.
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    $downloaded = $false
    foreach ($url in @('https://downloads.sourceforge.net/project/nsis/NSIS%203/3.12/nsis-3.12.zip','https://distfiles.macports.org/nsis/nsis-3.12.zip')) {
        try {
            Invoke-WebRequest -Uri $url -OutFile "$archive.download" -TimeoutSec 90
            if ((Get-FileHash -LiteralPath "$archive.download" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Checksum mismatch or server returned an HTML download page.' }
            Move-Item -LiteralPath "$archive.download" -Destination $archive -Force
            $downloaded = $true
            break
        } catch { Write-Warning "Portable tool download failed: $url" }
    }
    if (-not $downloaded) { throw 'Could not obtain the pinned NSIS archive.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $cache 'nsis-3.12/makensis.exe'))) { Expand-Archive -LiteralPath $archive -DestinationPath $cache }
Write-Output (Join-Path $cache 'nsis-3.12/makensis.exe')
