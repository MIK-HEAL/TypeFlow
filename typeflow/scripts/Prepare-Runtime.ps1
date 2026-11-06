param([switch]$IncludeWeasel, [switch]$Offline)
$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $product -Parent
$cache = Join-Path $repo '.cache/typeflow/reference'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $product 'sources.json') -Raw -Encoding utf8 | ConvertFrom-Json
foreach ($package in $manifest.packages) {
    if ($package.optional -and -not $IncludeWeasel) { continue }
    $archive = Join-Path $cache $package.file
    if (-not (Test-Path -LiteralPath $archive)) {
        if ($Offline) { throw "Missing cached package: $($package.file)" }
        Write-Output "Downloading $($package.id) into the project cache."
        Invoke-WebRequest -Uri $package.url -OutFile "$archive.download" -TimeoutSec 180
        $hash = (Get-FileHash -LiteralPath "$archive.download" -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $package.sha256) { throw "Checksum mismatch: $($package.file). Download was not applied." }
        Move-Item -LiteralPath "$archive.download" -Destination $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) { throw "Cached archive checksum mismatch: $($package.file)" }
}
$extract = Join-Path $product 'build/runtime-source'
$runtime = Join-Path $product 'runtime'
$data = Join-Path $product 'rime-data'
New-Item -ItemType Directory -Force -Path $extract,$runtime,$data,(Join-Path $data 'licenses') | Out-Null
foreach ($id in @('librime', 'rime-deps')) {
    $package = $manifest.packages | Where-Object id -eq $id
    $target = Join-Path $extract $id
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    tar -xf (Join-Path $cache $package.file) -C $target
    if ($LASTEXITCODE) { throw "Extraction failed: $id" }
}
Copy-Item -LiteralPath (Join-Path $extract 'librime/dist/lib/rime.dll') -Destination $runtime
Get-ChildItem -LiteralPath (Join-Path $extract 'rime-deps/bin') -Filter '*.dll' | Copy-Item -Destination $runtime
Copy-Item -LiteralPath (Join-Path $extract 'rime-deps/share/opencc') -Destination $data -Recurse -Force
foreach ($id in @('luna-pinyin','prelude','essay')) {
    $package = $manifest.packages | Where-Object id -eq $id
    $target = Join-Path $extract $id
    Expand-Archive -LiteralPath (Join-Path $cache $package.file) -DestinationPath $target -Force
    foreach ($item in Get-ChildItem -LiteralPath $target -File -Recurse) {
        if ($item.Extension -in @('.yaml','.txt')) { Copy-Item -LiteralPath $item.FullName -Destination $data }
        if ($item.Name -in @('LICENSE','AUTHORS','README.md')) {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $data "licenses/$id-$($item.Name)")
        }
    }
}
[IO.File]::WriteAllText((Join-Path $data 'default.custom.yaml'), "patch:`n  schema_list:`n    - schema: luna_pinyin_simp`n", [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $product 'sources.json') -Destination (Join-Path $runtime 'sources.json')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $runtime 'librime-LICENSE')
Copy-Item -LiteralPath (Join-Path $product 'THIRD_PARTY_NOTICES.md') -Destination $runtime
if ($IncludeWeasel) {
    $source = Join-Path $product 'build/weasel-source'
    # Do not overwrite a source tree that may already have local edits.
    if (-not (Test-Path -LiteralPath (Join-Path $source 'weasel-0.17.4'))) {
        Expand-Archive -LiteralPath (Join-Path $cache 'weasel-0.17.4.zip') -DestinationPath $source
    }
    & (Join-Path $product 'integration/weasel/Apply-Bridge.ps1') -WeaselSource (Join-Path $source 'weasel-0.17.4')
}
Write-Output 'Prepared project-local runtime and simplified pinyin data. No system IME registration or existing Rime user data was changed.'
