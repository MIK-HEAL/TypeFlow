# Windows 输入法安装预览版

Alpha.2 已把皮肤管理和可选候选渲染器加入安装包。开始菜单“皮肤设置”和输入法设置菜单都可进入；选择皮肤后“立即重载配置”也会应用当前选中项，并切换到 TypeFlow。支持用户提供的静态 SSF、装饰图层、文字位置和拉伸编辑；候选选择当前使用键盘。皮肤库保存在 `%LOCALAPPDATA%\TypeFlow\Skins`，卸载后保留。

皮肤后台以普通用户身份启动，安装包自带 WPF/.NET 10.0.11 运行时，无需用户另装运行时。已验证隐藏启动后的首个皮肤候选窗具备 Win32 实际可见状态且未激活窗口，避免仅凭 WPF `IsVisible` 或消息传输成功误判显示成功。

同时修复：后台启动传入 `SW_HIDE` 时首次 `ShowWindow` 被启动参数覆盖，导致消息/渲染成功但窗口仍隐藏。候选窗最终定位使用 `SWP_SHOWWINDOW | SWP_NOACTIVATE`；设置页另行显式显示。验证记录见构建证据 `skin-visible-r5.json` 和 `skin-final-status.json`。

已有真正的 `setup.exe` 安装包，包含原生输入法、Rime 运行库、简体拼音数据和用户初始化程序。终端用户无需安装 .NET、Visual Studio、Boost 或下载词库，也无需执行 PowerShell 注册脚本。

双击安装包，允许管理员授权，按中文向导完成安装。完成页的“立即启用 TypeFlow”会以桌面普通用户身份准备词库、启动服务并启用当前输入配置。之后可以使用 Win + 空格选择 `TypeFlow IME Preview`，Shift 切换中英文。

安装路径固定为 `%ProgramFiles%\TypeFlow\Ime`；开始菜单提供启动、配置目录、重新部署和卸载入口。软件出现在 Windows 已安装应用列表。每次用户登录会运行轻量初始化程序；已经部署且版本未变化时跳过重新部署。卸载不递归删除用户目录，保留用户配置和词库。

## 当前验证

2026-11-06 在本机完成：

- 解开安装包，逐项核对全部有效载荷文件哈希；未包含用户提供的 SSF 文件。
- 真正执行静默安装、卸载、重新安装，检查两个位数的 TSF COM 注销、安装路径和注册路径。
- 卸载及重新安装前后，现有 `default.custom.yaml`、`weasel.custom.yaml` 哈希相同。
- 将专用用户目录临时指向项目中的空白目录，运行已安装的初始化程序，离线生成 `luna_pinyin_simp` 部署文件；测试后恢复原目录设置。
- 从桌面 Shell 启动已安装的用户服务。

此前原生输入法已在记事本验证组合、空格/数字选词、上屏、退格、Esc 和 Shift 切换。当前候选窗仍使用 Weasel 原生渲染。安装包并不意味着高级皮肤已经接入，也不替代浏览器、32 位应用、DPI、多屏、不同 Windows 版本以及干净虚拟机上的后续验证。本轮未逐页自动验证图形安装向导。

当前包未做代码签名，是本地安装预览；没有推送或发布到公开下载渠道。公开发布前仍需补齐原生运行库及插件的完整来源、依赖许可/源码清单和兼容性验收。

## 构建与验证

先按 [原生构建说明](windows-native-ime.md) 构建并暂存原生组件，再运行：

```powershell
# 下载并校验项目内便携 NSIS，不安装到系统或修改 PATH。
pwsh -NoProfile -File typeflow/scripts/Prepare-WindowsImeInstallerTools.ps1
pwsh -NoProfile -File typeflow/scripts/Package-WindowsIme.ps1
pwsh -NoProfile -File typeflow/scripts/Test-WindowsImeInstaller.ps1 -Installer "绝对路径\setup.exe"
```

已有同名产物时需指定新的 `-OutputDirectory`，避免静默覆盖测试记录。输出包括安装包、SHA256、有效载荷清单、固定 Weasel 源码及全部修改/安装器脚本。构建工作目录保留以便复查。`Test-WindowsImeInstaller.ps1 -InstallCycle -ExpectedUserSid <当前用户SID>` 需要显式管理员权限，会暂时卸载 TypeFlow 并最终重新安装，不能作为无副作用的普通检查运行。

便携 NSIS 固定为 3.12，ZIP SHA256 为 `56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f`，与 MacPorts 包清单核对。本次 SourceForge 返回下载网页，最终使用 HTTPS MacPorts 镜像并校验相同哈希。NSIS 不进入用户有效载荷。

## 安装器实现

`packaging/windows-ime/installer.nsi` 复用 NSIS Modern UI 和已有原生注册工具。`launcher.cpp` 将每个用户的初始化与机器级安装分开，避免将输入服务作为管理员启动，也避免在使用其他管理员账户授权时把词库创建到管理员目录。只有不存在或为空的默认覆盖配置会被填充；已有内容保留。

文件安装和卸载指令由同一份清单生成。卸载前检查安装目录与专用记录相符，检查注销程序退出码；注销失败保留程序文件。没有使用 `RMDir /r $INSTDIR`，不会递归删除用户目录。

本轮安装周期测试暴露并修正了原生注册工具的一个上游问题：第二次注销必须在关闭 WOW64 文件重定向后调用，以正确运行 64 位 `regsvr32`。修复保存在 `integration/weasel/Apply-NativeBuildFixes.ps1`，可重复应用；`Stage-Native.ps1` 检查该顺序后才允许打包。

参考：[NSIS 文档](https://nsis.sourceforge.io/Docs/)、[从管理员程序以桌面用户身份启动应用](https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643)、[NSIS 固定包校验来源](https://github.com/macports/macports-ports/blob/master/devel/nsis/Portfile)。
