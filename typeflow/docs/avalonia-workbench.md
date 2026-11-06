# Avalonia 跨平台工作台小样

日期：2026-11-06。固定依赖：Avalonia 12.1.5，目标框架 .NET 10。

这是独立的皮肤/布局工作台，复用共享服务和 Rime 适配。系统输入法前端仍需各平台分别接入。

## 构建与运行

在仓库根目录执行（使用已有 .NET 10 和 PowerShell 7）：

```powershell
pwsh -NoProfile -File typeflow/scripts/Build-CrossPlatform.ps1 -Test
dotnet typeflow/build/cross-platform/TypeFlow.CrossPlatform.dll --workspace typeflow/workspace/avalonia --theme typeflow/themes/night
```

首次构建需要下载 NuGet 包；脚本以 --locked-mode 还原，三个项目的 packages.lock.json 固定传递依赖及内容哈希。没有新增系统级 SDK 或 Linux 开发环境。构建产物位于 build/cross-platform。

Windows 也可直接启动 build/cross-platform/TypeFlow.CrossPlatform.exe。默认库目录是当前用户 LocalApplicationData/TypeFlow/Avalonia；--workspace 可显式改为项目内目录。工作台自己的皮肤库与原 WPF workspace 分开，Rime 用户目录只有在传入 --rime 时才创建。

--theme 接受原生皮肤目录或 JSON/JSONC 配置：目录导入到自己的库，文件建立配置关联。每次指定该参数会新建库条目。SSF 可通过窗口内“导入 SSF…”选择。

### 可操作的界面

- 导入 SSF、原生皮肤目录，或关联外部 JSON/JSONC。
- 显示同一 Theme / CandidateView，渲染静态背景、九宫格、平铺、图层和独立文字坐标。
- 打开“拖动布局”后移动文字或已有图层；方向键微调，Shift 加速。
- 调整字号、撤销/重做、放弃修改、保存独立副本、设为当前皮肤。
- 显示并保留原导入兼容报告，用户保存副本不会把 partial 改成完全兼容。
- 配置有效且没有未保存修改时轮询重载；无效配置保留有效预览。存在未保存修改时阻止直接切换/应用，并在关闭前询问。

默认候选明确标注为固定演示数据。输入框此时只改变拼音预览文字，点击候选演示选择结果。

### 连接真实 Rime

Windows 已准备原有运行时且构建过桥接时：

```powershell
pwsh -NoProfile -File typeflow/scripts/Build-Portable.ps1 -Native
pwsh -NoProfile -File typeflow/scripts/Build-CrossPlatform.ps1 -Test -RimeTest
dotnet typeflow/build/cross-platform/TypeFlow.CrossPlatform.dll --workspace typeflow/workspace/avalonia --rime typeflow/runtime/rime.dll typeflow/rime-data typeflow/workspace/avalonia-rime-user luna_pinyin_simp
```

构建脚本会把已有 native-portable 产物复制到工作台输出目录。Linux/macOS 需自行提供对应系统/进程架构的桥接库与 librime（及它们的依赖）、共享词库和独立用户目录，再使用相同的 --rime 参数。当前 -RimeTest 专门使用本项目已准备的 Windows 测试运行时。

连接后拼音、候选和上屏结果来自 IInputEngine。局部换肤、编辑和保存副本不会重建输入会话。此窗口中的真实输入不等于在其他应用里注册了输入法。

## 组件依赖

| 工程 | 责任 |
| --- | --- |
| TypeFlow.Rendering.Avalonia | ICandidateRenderer、图片解码/96 DPI 归一化、图片校验和静态 PNG 转码；只依赖 Core 和 Avalonia |
| TypeFlow.CrossPlatform | 窗口、文件选择器、输入事件和实现装配；选择共享 Themes/导入器/Rime 适配 |
| TypeFlow.AvaloniaTests | 无窗口的真实 Skia 像素、指针/键盘事件与工作台组合检查 |

Core、Themes、SSF 和 Rime 托管适配没有新增 Avalonia 依赖。WPF 窗口与渲染器继续独立构建；未来可以替换宿主或渲染器，同时复用导入、编辑和引擎组件。

## 已验证与边界

- a857fa33 的 [Commit CI](https://github.com/MIK-HEAL/TypeFlow/actions/runs/42155798325)、[Release CI](https://github.com/MIK-HEAL/TypeFlow/actions/runs/42155798202)、[三平台共享组件 CI](https://github.com/MIK-HEAL/TypeFlow/actions/runs/42155798204) 全部通过；该结果是新增 Avalonia 小样之前的基线。
- 本轮 Windows 编译为 0 警告、0 错误。30 项 Avalonia 检查通过，包含静态 PNG、恶意尺寸/动画拒绝、DPI 归一化、1/1.5/2 倍率输出、锚点、透明度、九宫格/平铺、失败后保留有效图片、拖动/点击、共享保存、未保存修改保护。
- 其中 3 项使用本地真实 Rime：在工作台中输入 nihao 得到“你好”，换肤保留组合状态，空格上屏“你好”。不带 -RimeTest 时运行 27 项，不要求 Rime 运行时。
- Windows 原生窗口已启动，并观察到系统文件选择器和 SSF 导入预览；完整桌面交互验收仍待完成。后续窗口会话消失，未观察到相关应用崩溃日志，没有将完整焦点/多屏流程标为通过。
- 本轮新 UI 已加入三平台 CI 的 Build-CrossPlatform.ps1 -Test 步骤，尚未推送远程验证；Linux/macOS 的原生窗口、文件选择器和真实 Rime 仍需实机验证。
- 当前跨平台图片后端只接受静态 PNG，拒绝 GIF/JPEG/WebP 和 APNG；装饰图层可在 JSON 中配置。完整 WPF 属性编辑面板、新增图片按钮等尚未全部迁移。
- 各系统字体回退与文字度量可能不同，不承诺 WPF/Avalonia 或不同系统逐像素一致；测试按几何、资源、交互与关键像素分别验证。

生成证据位于 build/evidence/avalonia，包括工作台图、候选图和 result.json。SSF 素材的原授权边界继续保留，生成图片没有加入远程 CI 的发布产物。

## 资料

- [Avalonia 12.1.5 官方 NuGet 包](https://www.nuget.org/packages/Avalonia/12.1.5)
- [自绘控件](https://github.com/avaloniaui/avalonia-docs/blob/main/docs/custom-controls/custom-drawn-controls.md)
- [Headless 与真实 Skia 渲染](https://github.com/avaloniaui/avalonia-docs/blob/main/docs/testing/setting-up-the-headless-platform.md)
