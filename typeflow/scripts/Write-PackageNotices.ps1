param(
    [Parameter(Mandatory)][string]$AssetsFile,
    [Parameter(Mandatory)][string]$DepsFile,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$RuntimeVersion,
    [string[]]$AdditionalPackages = @()
)
$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
$deps = Get-Content -LiteralPath $DepsFile -Raw | ConvertFrom-Json -AsHashtable
$roots = @($assets.packageFolders.Keys)
$licenseRoot = Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$packages = @($deps.libraries.GetEnumerator() | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object Key)
$packages += "Microsoft.NETCore.App.Runtime.win-x64/$RuntimeVersion"
$packages += $AdditionalPackages
$inventory = @(foreach ($key in ($packages | Sort-Object -Unique)) {
    $id, $version = $key -split '/', 2
    if ($id -notmatch '^[A-Za-z0-9_.-]+$' -or $version -notmatch '^[A-Za-z0-9.+_-]+$') { throw "Invalid package identity: $key" }
    $relative = "$($id.ToLowerInvariant())/$($version.ToLowerInvariant())"
    $folder = $null
    foreach ($root in $roots) {
        $candidate = Join-Path $root $relative
        if (Test-Path -LiteralPath (Join-Path $candidate "$($id.ToLowerInvariant()).nuspec")) { $folder = $candidate; break }
    }
    if (-not $folder) { throw "Cannot find package license source: $key" }
    $specFile = Join-Path $folder "$($id.ToLowerInvariant()).nuspec"
    [xml]$spec = Get-Content -LiteralPath $specFile -Raw
    $metadata = $spec.SelectSingleNode("//*[local-name()='metadata']")
    $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
    if (-not $licenseNode) { throw "Package has no license metadata: $key" }
    $target = Join-Path $licenseRoot "$id.$version"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -LiteralPath $specFile -Destination (Join-Path $target 'package.nuspec')
    $noticeFiles = @(Get-ChildItem -LiteralPath $folder -File | Where-Object Name -match '(?i)licen[cs]e|notice|copying')
    if ($licenseNode.GetAttribute('type') -eq 'file') {
        $file = [IO.Path]::GetFullPath((Join-Path $folder $licenseNode.InnerText))
        $prefix = [IO.Path]::GetFullPath($folder).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $file)) { throw "Invalid license path: $key" }
        $noticeFiles += Get-Item -LiteralPath $file
    }
    foreach ($notice in ($noticeFiles | Sort-Object FullName -Unique)) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $target $notice.Name)
    }
    if ($noticeFiles.Count -eq 0) {
        if ($licenseNode.GetAttribute('type') -ne 'expression' -or $licenseNode.InnerText -ne 'MIT') { throw "License text needs explicit handling: $key" }
        $copyrightNode = $metadata.SelectSingleNode("*[local-name()='copyright']")
        if (-not $copyrightNode -or [string]::IsNullOrWhiteSpace($copyrightNode.InnerText)) { throw "Missing copyright attribution: $key" }
        $text = $copyrightNode.InnerText + "`n`n" + (Get-Content (Join-Path $product 'packaging/MIT.txt') -Raw)
        Set-Content -LiteralPath (Join-Path $target 'LICENSE.txt') -Value $text -Encoding utf8
    }
    $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
    $archive = Join-Path $folder "$($id.ToLowerInvariant()).$($version.ToLowerInvariant()).nupkg"
    [ordered]@{
        id=$id; version=$version; licenseType=$licenseNode.GetAttribute('type'); license=$licenseNode.InnerText
        repository=if ($repository) { $repository.GetAttribute('url') } else { $null }
        sourceCommit=if ($repository) { $repository.GetAttribute('commit') } else { $null }
        archiveSha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        notices=@(Get-ChildItem -LiteralPath $target -File | Select-Object -ExpandProperty Name)
    }
})
$inventory | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'dependency-inventory.json') -Encoding utf8
@'
# Third-party notices for this portable workbench

TypeFlow product code is BSD-3-Clause; see LICENSE.
This ZIP contains the .NET runtime and the NuGet components listed in dependency-inventory.json.
The exact package metadata, copyright notices and full license texts are preserved under licenses/.
For MIT-expression packages without a license file, LICENSE.txt combines the package's copyright
metadata with the standard MIT license text. Native packages preserve their supplied notices.

This workbench ZIP does not contain librime, Rime plugins/data, Weasel, or the third-party SSF fixture.
Their distribution is outside this package. The surrounding source checkout's Rime license is
preserved separately as licenses/Rime-BSD-3-Clause.txt for source attribution.
'@ | Set-Content -LiteralPath (Join-Path $Destination 'THIRD_PARTY_NOTICES.md') -Encoding utf8
Copy-Item -LiteralPath (Join-Path (Split-Path $product -Parent) 'LICENSE') -Destination (Join-Path $licenseRoot 'Rime-BSD-3-Clause.txt')
Write-Output "Preserved license metadata and notices for $($inventory.Count) packages."
