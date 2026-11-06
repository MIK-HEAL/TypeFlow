$ErrorActionPreference = 'Stop'
$product = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $product -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC is required to build the validation application.' }
$build = Join-Path $product 'build/native-input-acceptance'
$null = New-Item -ItemType Directory -Path $build -Force
$source = Join-Path $product 'integration/weasel/native_input_acceptance.cpp'
$command = Join-Path $build 'build.cmd'
foreach ($path in @($build,$source,$vs)) { if ($path -match '["%\r\n&|<>^!]') { throw 'Unsafe command path.' } }
$lines = @(
    '@echo off', "call `"$vs\VC\Auxiliary\Build\vcvarsall.bat`" x64", 'if errorlevel 1 exit /b %errorlevel%',
    "cl /nologo /EHsc /std:c++17 /MT /utf-8 /DUNICODE /D_UNICODE `"$source`" /Fo`"$build\acceptance.obj`" /Fe`"$build\TypeFlowInputAcceptance.exe`" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib ole32.lib oleaut32.lib uuid.lib advapi32.lib",
    'exit /b %errorlevel%'
)
[IO.File]::WriteAllLines($command, $lines, [Text.Encoding]::Default)
& $env:ComSpec /d /c $command
if ($LASTEXITCODE) { throw 'Validation app build failed.' }
Write-Output (Join-Path $build 'TypeFlowInputAcceptance.exe')
