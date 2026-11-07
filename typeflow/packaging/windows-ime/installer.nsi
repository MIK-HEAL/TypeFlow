Unicode true
!include MUI2.nsh
!include LogicLib.nsh
!include x64.nsh
!include WinVer.nsh
!include FileFunc.nsh

!define PACKAGE_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TypeFlowImePreview"
Name "TypeFlow 输入法 ${VERSION}"
OutFile "${INSTALLER_FILE}"
InstallDir "$PROGRAMFILES64\TypeFlow\Ime"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "TypeFlow 输入法 · 预览版"
VIProductVersion "${FILE_VERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "TypeFlow 输入法"
VIAddVersionKey /LANG=2052 "FileDescription" "TypeFlow 输入法安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "TypeFlow contributors; Rime / Weasel contributors"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "安装 TypeFlow 拼音输入法，自动准备词库和输入服务。$\r$\n$\r$\n支持导入 SSF、编辑皮肤布局，并应用到系统候选窗。"
!define MUI_FINISHPAGE_TITLE "TypeFlow 已安装"
!define MUI_FINISHPAGE_TEXT "首次启动会自动准备拼音词库。$\r$\n$\r$\n使用 Win + 空格切换到 TypeFlow IME Preview。Shift 切换中英文。$\r$\n$\r$\n升级后请完全退出并重开浏览器。若桌面或资源管理器仍显示旧候选窗，请注销 Windows 后重新登录。"
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "启用 TypeFlow 并打开皮肤设置"
!define MUI_FINISHPAGE_RUN_FUNCTION StartTypeFlow
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${PAYLOAD}\WEASEL-LICENSE.txt"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
Var PreviousRoot
Var InstallerMutex

Function .onInit
  ${IfNot} ${IsNativeAMD64}
    MessageBox MB_ICONSTOP "本安装包支持 Windows x64。"
    SetErrorLevel 2
    Quit
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "本安装包需要 Windows 10 或更新版本。"
    SetErrorLevel 2
    Quit
  ${EndIf}
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\TypeFlow.Ime.Installer") p .r0 ?e'
  Pop $1
  StrCpy $InstallerMutex $0
  ${If} $1 = 183
    MessageBox MB_ICONSTOP "另一个 TypeFlow 安装或卸载正在运行。"
    SetErrorLevel 2
    Quit
  ${EndIf}
  SetRegView 32
  ReadRegStr $PreviousRoot HKLM "Software\TypeFlow\Ime" "WeaselRoot"
FunctionEnd

Function StartTypeFlow
  ClearErrors
  ExecWait '"$INSTDIR\TypeFlowImeLauncher.exe" --as-user --enable --skin-settings' $0
  ${If} ${Errors}
  ${OrIf} $0 != 0
    MessageBox MB_ICONEXCLAMATION "输入法已安装。请从开始菜单启动 TypeFlow 完成初始化。"
  ${EndIf}
FunctionEnd

Section "TypeFlow 输入法" Main
  SetShellVarContext all
  SetRegView 32
  ${If} $PreviousRoot != ""
    IfFileExists "$PreviousRoot\TypeFlowImeServer.exe" 0 +2
    ExecWait '"$PreviousRoot\TypeFlowImeServer.exe" /q' $0
  ${EndIf}
  ; Stop the optional user renderer before replacing its runtime files.
  IfFileExists "$INSTDIR\skin\TypeFlow.Desktop.exe" 0 +2
  ExecWait '"$INSTDIR\skin\TypeFlow.Desktop.exe" --stop-host' $0
  SetOverwrite on
  ClearErrors
  !include "${INSTALL_FILES}"
  IfErrors copy_failed
  ClearErrors
  ExecWait '"$INSTDIR\TypeFlowImeSetup.exe" /s' $0
  ${If} ${Errors}
  ${OrIf} $0 != 0
    ; Recover the previous registered preview if one was present.
    ${If} $PreviousRoot != ""
    ${AndIf} $PreviousRoot != $INSTDIR
      IfFileExists "$PreviousRoot\TypeFlowImeSetup.exe" 0 registration_failed
      ExecWait '"$PreviousRoot\TypeFlowImeSetup.exe" /s' $1
    ${Else}
      ExecWait '"$INSTDIR\TypeFlowImeSetup.exe" /u' $1
    ${EndIf}
    Goto registration_failed
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${PACKAGE_KEY}" "DisplayName" "TypeFlow 输入法（预览版）"
  WriteRegStr HKLM "${PACKAGE_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${PACKAGE_KEY}" "Publisher" "TypeFlow contributors"
  WriteRegStr HKLM "${PACKAGE_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${PACKAGE_KEY}" "DisplayIcon" "$INSTDIR\TypeFlowImeServer.exe"
  WriteRegStr HKLM "${PACKAGE_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKLM "${PACKAGE_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKLM "${PACKAGE_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${PACKAGE_KEY}" "NoRepair" 1
  SetRegView 64
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Run" "TypeFlowIme" '$\"$INSTDIR\TypeFlowImeLauncher.exe$\"'
  SetRegView 32
  CreateDirectory "$SMPROGRAMS\TypeFlow 输入法"
  CreateShortCut "$SMPROGRAMS\TypeFlow 输入法\启动 TypeFlow.lnk" "$INSTDIR\TypeFlowImeLauncher.exe" "--enable"
  CreateShortCut "$SMPROGRAMS\TypeFlow 输入法\输入法设定.lnk" "$INSTDIR\TypeFlowImeDeployer.exe"
  CreateShortCut "$SMPROGRAMS\TypeFlow 输入法\皮肤设置.lnk" "$INSTDIR\TypeFlowImeLauncher.exe" "--skin-settings"
  CreateShortCut "$SMPROGRAMS\TypeFlow 输入法\打开用户配置.lnk" "$INSTDIR\TypeFlowImeLauncher.exe" "--settings"
  CreateShortCut "$SMPROGRAMS\TypeFlow 输入法\重新部署.lnk" "$INSTDIR\TypeFlowImeDeployer.exe" "/deploy"
  CreateShortCut "$SMPROGRAMS\TypeFlow 输入法\卸载.lnk" "$INSTDIR\Uninstall.exe"
  IfSilent 0 done
  ${GetParameters} $0
  ${GetOptions} $0 "/NOSTART" $1
  IfErrors 0 done
  ExecWait '"$INSTDIR\TypeFlowImeLauncher.exe" --as-user' $0
  Goto done
copy_failed:
  SetErrorLevel 10
  MessageBox MB_OK|MB_ICONSTOP "文件复制未完成，输入法未重新注册。请关闭 TypeFlow 后重试。" /SD IDOK
  Abort
registration_failed:
  SetErrorLevel 11
  MessageBox MB_OK|MB_ICONSTOP "输入法注册未完成，请重新运行安装程序。安装没有报告成功。" /SD IDOK
  Abort
done:
SectionEnd

Function un.onInit
  SetRegView 32
  ReadRegStr $0 HKLM "${PACKAGE_KEY}" "InstallLocation"
  ${If} $0 != $INSTDIR
    MessageBox MB_ICONSTOP "安装目录与记录不符，已停止卸载。"
    SetErrorLevel 2
    Quit
  ${EndIf}
  IfFileExists "$INSTDIR\package-manifest.json" +4 0
    MessageBox MB_ICONSTOP "缺少安装清单，已停止卸载以保护其他文件。"
    SetErrorLevel 2
    Quit
FunctionEnd

Section "Uninstall"
  SetShellVarContext all
  SetRegView 32
  ExecWait '"$INSTDIR\TypeFlowImeServer.exe" /q' $0
  IfFileExists "$INSTDIR\skin\TypeFlow.Desktop.exe" 0 +2
  ExecWait '"$INSTDIR\skin\TypeFlow.Desktop.exe" --stop-host' $0
  ClearErrors
  ExecWait '"$INSTDIR\TypeFlowImeSetup.exe" /u' $0
  ${If} ${Errors}
  ${OrIf} $0 != 0
    SetErrorLevel 12
    MessageBox MB_OK|MB_ICONSTOP "输入法注销未完成，程序文件已保留。请切换到其他输入法后重试。" /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\Run" "TypeFlowIme"
  SetRegView 32
  DeleteRegKey HKLM "${PACKAGE_KEY}"
  Delete "$SMPROGRAMS\TypeFlow 输入法\启动 TypeFlow.lnk"
  Delete "$SMPROGRAMS\TypeFlow 输入法\输入法设定.lnk"
  Delete "$SMPROGRAMS\TypeFlow 输入法\皮肤设置.lnk"
  Delete "$SMPROGRAMS\TypeFlow 输入法\打开用户配置.lnk"
  Delete "$SMPROGRAMS\TypeFlow 输入法\重新部署.lnk"
  Delete "$SMPROGRAMS\TypeFlow 输入法\卸载.lnk"
  RMDir "$SMPROGRAMS\TypeFlow 输入法"
  SetOutPath "$TEMP"
  ; Generated exact file list; never recursively delete an install or user directory.
  !include "${UNINSTALL_FILES}"
  Delete /REBOOTOK "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; User configuration and dictionaries are intentionally retained.
SectionEnd
