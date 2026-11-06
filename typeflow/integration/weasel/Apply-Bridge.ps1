param([Parameter(Mandatory)][string]$WeaselSource)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $WeaselSource).Path
$target = Join-Path $sourceRoot 'WeaselUI/WeaselUI.cpp'
$text = [IO.File]::ReadAllText($target).Replace("`r", '')
function Attach-Recovery([string]$Code) {
    $start = $Code.IndexOf('bool UI::Create(HWND parent)')
    $end = $Code.IndexOf('void UI::Destroy(bool full)', $start)
    if ($start -lt 0 -or $end -lt 0) { throw 'Missing native window lifecycle methods.' }
    $block = $Code.Substring($start, $end - $start)
    if (-not $block.Contains('Endpoint::Instance().Attach')) {
        $block = $block.Replace('return true;', 'typeflow::Endpoint::Instance().Attach(pimpl_->panel.m_hWnd, [this] { Refresh(); if (status_.composing) Show(); else Hide(); });' + "`n  return true;")
        $Code = $Code.Substring(0, $start) + $block + $Code.Substring($end)
    }
    if (-not $Code.Contains('Endpoint::Instance().Detach')) {
        $Code = $Code.Replace('void UI::Destroy(bool full) {', "void UI::Destroy(bool full) {`n  typeflow::Endpoint::Instance().Detach();")
    }
    return $Code
}
if ($text.Contains('TypeFlowEndpoint.h')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TypeFlowEndpoint.h') -Destination (Join-Path $sourceRoot 'WeaselUI/TypeFlowEndpoint.h')
    if (-not $text.Contains('Endpoint::Instance().Attach')) {
        $text = Attach-Recovery $text
        [IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))
    }
    Write-Output 'TypeFlow adapter already applied; endpoint and host recovery refreshed.'
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
$text = Attach-Recovery $text
if (Test-Path -LiteralPath "$target.typeflow-original") { throw 'Backup exists; refusing to overwrite it.' }
Copy-Item -LiteralPath $target -Destination "$target.typeflow-original"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TypeFlowEndpoint.h') -Destination (Join-Path $sourceRoot 'WeaselUI/TypeFlowEndpoint.h')
[IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))
Write-Output "Applied optional presentation adapter to $target"
