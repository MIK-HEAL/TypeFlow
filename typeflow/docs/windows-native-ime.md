# Windows 原生输入法预览

这条路线复用 Weasel 0.17.4 的 TSF、IPC、Rime 服务和原生候选窗，先完成跨应用输入。它与皮肤工作台独立；本轮未接入 TypeFlow 的高级图层渲染。

## 构建

依赖为 Visual Studio Build Tools 18 的 MSVC v145、ATL x86/x64、Windows SDK，以及项目缓存内的 Boost 1.92.0。构建脚本不安装依赖，不修改全局环境变量，不注册输入法，也不调用会停止现有小狼毫服务的上游构建脚本。

```powershell
pwsh -NoProfile -File typeflow/integration/weasel/Build-Native.ps1
# Boost 已编译后可以跳过重新检查/构建 Boost：
pwsh -NoProfile -File typeflow/integration/weasel/Build-Native.ps1 -SkipBoost
pwsh -NoProfile -File typeflow/integration/weasel/Stage-Native.ps1
```

产物在 `typeflow/build/windows-ime/preview/`。`TYPEFLOW_PREVIEW.json` 记录二进制 SHA256、PE 位数和注册标识。准备脚本校验固定 Weasel 源码包，源码放在 `typeflow/build/windows-ime/source-preview/weasel-0.17.4/`；已有标记与当前补丁脚本不符时拒绝静默复用。

本机已完成 x64/x86 TSF、x64 服务和部署器、x86 安装器构建。已检查二进制位数、TSF 注册导出和服务/Rime 的 DLL 依赖。构建成功不代表系统输入已经通过。

## 注册与卸载

首先查看具体操作范围，默认只输出计划：

```powershell
pwsh -NoProfile -File typeflow/integration/weasel/Manage-NativePreview.ps1
```

明确执行安装、启动、停止和卸载：

```powershell
pwsh -NoProfile -File typeflow/integration/weasel/Manage-NativePreview.ps1 -Action Install
pwsh -NoProfile -File typeflow/integration/weasel/Manage-NativePreview.ps1 -Action Start
pwsh -NoProfile -File typeflow/integration/weasel/Manage-NativePreview.ps1 -Action Stop
# 先切换到其他输入法，再卸载：
pwsh -NoProfile -File typeflow/integration/weasel/Manage-NativePreview.ps1 -Action Uninstall
```

安装需要 Windows 管理员授权，写入 System32/SysWOW64 中各自位数的 `typeflowtip.dll`、专用 COM/TSF 配置及 `Software\TypeFlow\Ime`。CLSID 为 `{5BB3E3A4-D9DC-4885-9EA4-C99BD97A9C5C}`，Profile GUID 为 `{BE0C4C87-20C8-4E3B-B7DD-78053F194B73}`。用户数据默认在 `%LOCALAPPDATA%\TypeFlow\Ime`。没有添加开机启动、默认输入法覆盖或旧 IMM 安装，也没有启用上游自动更新和系统崩溃转储设置。卸载保留用户词库。

安装后预览目录必须保持原位；TSF 会根据专用注册表值启动其中的服务。不要先移动/删除目录再卸载。预览不与已有小狼毫共享 IPC、系统 DLL 文件名或用户目录。

## 配置与验收

原生候选窗使用成熟的 Rime/Weasel YAML 配置方式。部署后可以在用户目录维护 `default.custom.yaml`、`weasel.custom.yaml`；修改后运行预览目录中的 `TypeFlowImeDeployer.exe /deploy`。这不等于已把 TypeFlow 的 SSF 导入或高级皮肤图层接入原生候选窗。

系统验收需在记事本和浏览器中逐项验证：拼音组合、候选显示、空格/数字选词、中文上屏、退格、Esc、中英文切换、跨应用焦点和候选位置；后续覆盖 32 位应用、DPI、多屏与服务重启。工作台文本框、IPC 探针、构建成功都不能替代这些验收。

## 上游修改与许可

准备脚本保留上游 API/命名空间，修改可共存标识、产物名和用户目录；关闭上游自动更新和安装器 WER 写入，修正注册描述字符长度及注册子进程错误传播。构建脚本对资源文件显式指定 UTF-8，使用 SDK `winres.h` 并将资源版本号转成字符串。

Weasel 部分沿用 GPL-3.0。预览目录保留其许可，生成源码及补丁脚本保留在仓库中。发布修改版前应随发布提供完整对应源码、补丁和构建说明；不能只随二进制附一个上游链接。Rime、Boost、WTL、WinSparkle 和词库保留各自许可。

参考：[Weasel 源码](https://github.com/rime/weasel/tree/0.17.4)、[TSF RegisterProfile](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-registerprofile)、[Windows 资源编译器编码](https://devblogs.microsoft.com/oldnewthing/20190607-00/?p=102569)。
