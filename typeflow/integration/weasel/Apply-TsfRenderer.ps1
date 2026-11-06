param([Parameter(Mandatory)][string]$WeaselSource)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($WeaselSource)
$uiFile = Join-Path $root 'WeaselUI/WeaselUI.cpp'
$ui = [IO.File]::ReadAllText($uiFile).Replace("`r", '')
if ($ui.Contains('TypeFlowEndpoint.h')) {
    $backup = "$uiFile.typeflow-original"
    if (-not (Test-Path -LiteralPath $backup)) { throw 'Legacy bridge backup is missing; refusing to rewrite unknown native UI code.' }
    $ui = [IO.File]::ReadAllText($backup).Replace("`r", '')
}
# The shared native renderer must remain usable by the server and UI-less apps.
# Only the authoritative TSF candidate controller selects the skin renderer.
if (-not $ui.Contains('HWND UI::NativeWindow() const')) {
    $ui += "`nHWND UI::NativeWindow() const { return pimpl_ ? pimpl_->panel.m_hWnd : nullptr; }`n"
}
[IO.File]::WriteAllText($uiFile, $ui, [Text.UTF8Encoding]::new($false))
$headerFile = Join-Path $root 'include/WeaselUI.h'
$header = [IO.File]::ReadAllText($headerFile)
if (-not $header.Contains('HWND NativeWindow() const;')) { $header = $header.Replace('  bool Create(HWND parent);', "  bool Create(HWND parent);`n  HWND NativeWindow() const;") }
[IO.File]::WriteAllText($headerFile, $header, [Text.UTF8Encoding]::new($false))

$candidateFile = Join-Path $root 'WeaselTSF/CandidateList.cpp'
$code = [IO.File]::ReadAllText($candidateFile).Replace("`r", '')
if (-not $code.Contains('// TypeFlow authoritative TSF renderer v2')) {
    function Replace-Once([string]$Text, [string]$From, [string]$To) {
        if (($Text.Split([string[]]@($From), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Unexpected TSF source shape: $From" }
        return $Text.Replace($From, $To)
    }
    $code = Replace-Once $code '#include "CandidateList.h"' "#include `"CandidateList.h`"`n#include `"TypeFlowEndpoint.h`"`n// TypeFlow authoritative TSF renderer v2"
    $code = Replace-Once $code ': _ui(make_unique<UI>()), _tsf(pTextService), _pbShow(TRUE)' ': _ui(make_unique<UI>()), _renderer(make_unique<typeflow::Endpoint>("tsf")), _tsf(pTextService), _pbShow(TRUE)'
    $code = Replace-Once $code 'CCandidateList::~CCandidateList() {}' 'CCandidateList::~CCandidateList() { _renderer->Detach(); }'
    $show = @'
STDMETHODIMP CCandidateList::Show(BOOL showCandidateWindow) {
  _showRequested = showCandidateWindow && _pbShow && _ui->status().composing;
  if (_showRequested) {
    if (_renderer->Update(_ui->ctx(), true)) _ui->Hide();
    else _ui->Show();
  } else {
    _renderer->Hide();
    _ui->Hide();
  }
  return S_OK;
}

'@
    $pattern = 'STDMETHODIMP CCandidateList::Show\(BOOL showCandidateWindow\) \{[\s\S]*?(?=STDMETHODIMP CCandidateList::IsShown)'
    if (-not [regex]::IsMatch($code, $pattern)) { throw 'Missing TSF Show method.' }
    $code = [regex]::Replace($code, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $show })
    $code = Replace-Once $code '*pIsShow = _ui->IsShown();' '*pIsShow = _ui->IsShown() || (_showRequested && _renderer->Ready());'
    $code = Replace-Once $code '  _ui->UpdateInputPosition(rc);' "  _ui->UpdateInputPosition(rc);`n  _renderer->Position(rc);"
    $code = Replace-Once $code '  _ui->Destroy();' "  _showRequested = false; _renderer->Hide();`n  _renderer->Detach();`n  _ui->Destroy();"
    $code = Replace-Once $code '  _ui->Destroy(true);' "  _showRequested = false; _renderer->Hide();`n  _renderer->Detach();`n  _ui->Destroy(true);"
    $attach = @'
  _ui->Create(p);
  _renderer->Attach(_ui->NativeWindow(), [this] {
    // This callback runs on the candidate's UI thread. A suspended context
    // must never resurrect itself when another app acquires the renderer.
    if (!_showRequested) return;
    _ui->Refresh();
    if (_renderer->Ready()) _ui->Hide(); else _ui->Show();
  });
'@
    $code = Replace-Once $code '  _ui->Create(p);' $attach
    [IO.File]::WriteAllText($candidateFile, $code, [Text.UTF8Encoding]::new($false))
}
$code = $code.Replace("  _renderer->Detach();`n  _ui->Destroy", "  _showRequested = false; _renderer->Hide();`n  _renderer->Detach();`n  _ui->Destroy")
# Avoid accumulating the explicit lifetime reset on repeated preparation.
$code = $code.Replace("  _showRequested = false; _renderer->Hide();`n  _showRequested = false; _renderer->Hide();", '  _showRequested = false; _renderer->Hide();')
[IO.File]::WriteAllText($candidateFile, $code, [Text.UTF8Encoding]::new($false))
$candidateHeader = Join-Path $root 'WeaselTSF/CandidateList.h'
$text = [IO.File]::ReadAllText($candidateHeader)
if (-not $text.Contains('std::unique_ptr<typeflow::Endpoint> _renderer;')) {
    $text = $text.Replace('class WeaselTSF;', "class WeaselTSF;`nnamespace typeflow { class Endpoint; }")
    $text = $text.Replace('  std::unique_ptr<weasel::UI> _ui;', "  std::unique_ptr<weasel::UI> _ui;`n  std::unique_ptr<typeflow::Endpoint> _renderer;`n  bool _showRequested = false;")
    [IO.File]::WriteAllText($candidateHeader, $text, [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TypeFlowEndpoint.h') -Destination (Join-Path $root 'WeaselTSF/TypeFlowEndpoint.h') -Force
Write-Output 'Patched the native TSF candidate controller; server UI does not own the skin channel.'
