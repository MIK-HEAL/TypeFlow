param([Parameter(Mandatory)][string]$WeaselSource)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $WeaselSource).Path
$target = Join-Path $sourceRoot 'WeaselUI/WeaselUI.cpp'
$text = [IO.File]::ReadAllText($target).Replace("`r", '')
if ($text.Contains('TypeFlowEndpoint.h')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TypeFlowEndpoint.h') -Destination (Join-Path $sourceRoot 'WeaselUI/TypeFlowEndpoint.h')
    Write-Output 'TypeFlow adapter already applied; endpoint header refreshed.'
    exit 0
}
$replacements = [ordered]@{
    '#include <WeaselUI.h>' = "#include <WeaselUI.h>`n#include `"TypeFlowEndpoint.h`""
    'void UI::Show() {' = "void UI::Show() {`n  if (typeflow::Endpoint::Instance().Update(ctx_, status_.composing)) {`n    if (pimpl_) pimpl_->Hide();`n    return;`n  }"
    'void UI::Hide() {' = "void UI::Hide() {`n  typeflow::Endpoint::Instance().Hide();"
    'void UI::Refresh() {' = "void UI::Refresh() {`n  if (typeflow::Endpoint::Instance().Update(ctx_, status_.composing)) {`n    if (pimpl_) pimpl_->Hide();`n    return;`n  }"
    'void UI::UpdateInputPosition(RECT const& rc) {' = "void UI::UpdateInputPosition(RECT const& rc) {`n  typeflow::Endpoint::Instance().Position(rc);"
}
foreach ($item in $replacements.GetEnumerator()) {
    if (($text.Split([string[]]@($item.Key), [StringSplitOptions]::None).Count - 1) -ne 1) {
        throw "Expected exactly one occurrence: $($item.Key). Adapter targets Weasel 0.17.4; no files changed."
    }
    $text = $text.Replace($item.Key, $item.Value)
}
if (Test-Path -LiteralPath "$target.typeflow-original") { throw 'Backup exists; refusing to overwrite it.' }
Copy-Item -LiteralPath $target -Destination "$target.typeflow-original"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TypeFlowEndpoint.h') -Destination (Join-Path $sourceRoot 'WeaselUI/TypeFlowEndpoint.h')
[IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))
Write-Output "Applied optional presentation adapter to $target"
