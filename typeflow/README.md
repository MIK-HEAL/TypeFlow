# TypeFlow 开发原型

已实现真实 Rime 输入适配、插件式皮肤导入、原生格式、图片候选窗渲染和皮肤管理。当前交付是**桌面输入实验室**，不是已注册的 Windows 系统输入法。系统前端复用入口已提供，完整的小狼毫构建、安装和跨应用验收仍待完成。

也可完全通过公开的 **JSON/JSONC 配置文件**自定义皮肤。支持关联外部配置、保存后自动重载、打开源配置和错误回退；见 [配置文件使用方法](docs/configuration.md)。YAML/CSS 解析尚未实现。

## 直接运行

本次已生成 `build/app/TypeFlow.Desktop.exe`，可以直接打开。此原型使用当前已安装的 .NET 10 Desktop Runtime，数据保存在项目内的 `workspace/`，不读取或修改 `%AppData%/Rime`。

1. 点击“导入 SSF”，选择 `../test/ssf/尼古喵喵！.ssf`，也可以将文件拖入窗口。
2. 候选窗会预览转换结果，下面显示兼容性说明。
3. 在“输入测试区”输入 `nihao`，用空格、数字或鼠标选择候选；支持退格、Esc、方向键和 PageUp/PageDown。
4. “应用到候选窗”持久保存选择；“恢复默认皮肤”可随时恢复。换肤不重建 Rime 会话。
5. 可以从 `themes/paper`、`themes/night` 导入第二种原生格式，编辑它们的 `theme.json` 自定义样式。
6. 选择皮肤后点击“高级布局自定义…”，打开可视化布局编辑器。支持装饰图层、独立文字坐标、锚点、透明度、图层顺序和九宫格／平铺。拖动或修改参数后“另存副本”或“保存并应用”；原皮肤不覆盖。详见 [高级布局说明](docs/advanced-layout.md)。
7. 点击“关联配置文件…”可以直接选择自己的 `.json/.jsonc`；示例为 `themes/config-example/theme.jsonc`。保存修改后约 1 秒自动刷新，无需重新导入。点击“编辑配置文件”打开源文件，右键该按钮可打开所在目录。

首轮启动会在 `workspace/rime-user/` 部署词库，可能比后续启动慢。系统内其他应用目前仍使用原来的输入法。

## 构建与验证

在仓库根目录运行（PowerShell 7）：

```powershell
# 只准备项目本地文件，不安装环境或注册输入法；首次需要联网。
pwsh -NoProfile -File typeflow/scripts/Prepare-Runtime.ps1

# 有完整缓存时可离线准备，校验所有归档哈希。
pwsh -NoProfile -File typeflow/scripts/Prepare-Runtime.ps1 -Offline

# 使用现有 MSVC / Windows SDK、CMake、.NET 10，无新增 NuGet 依赖。
pwsh -NoProfile -File typeflow/scripts/Build.ps1 -Test
```

`Build.ps1` 当前使用本机的 `Visual Studio 18 2026` CMake 生成器。其他机器可在脚本中选择已安装的 VS 生成器，或使用 `-SkipNative` 只构建皮肤预览（真实输入仍需桥接 DLL）。运行输入集成测试前必须准备 Rime 运行时。测试会生成独立的临时用户库，结束时清理，不使用真实个人词库。

CLI（通过 `dotnet` 调用以查看终端输出，目标目录须不存在）：

```powershell
dotnet typeflow/build/app/TypeFlow.Desktop.dll --import "test/ssf/尼古喵喵！.ssf" "typeflow/build/my-theme"
dotnet typeflow/build/app/TypeFlow.Desktop.dll --render "typeflow/build/my-theme/theme.json" "typeflow/build/my-theme.png" 1.5
dotnet typeflow/build/app/TypeFlow.Desktop.dll --import "typeflow/themes/night" "typeflow/build/my-night"
```

## 工程边界

| 位置 | 职责 |
| --- | --- |
| `src/TypeFlow.Core` | 原生皮肤、校验、导入器接口和注册、候选模型、引擎接口 |
| `src/TypeFlow.Importers.Ssf` | 普通 ZIP SSF 的识别、编码解析、独立映射和兼容性报告 |
| `src/TypeFlow.Engine.Rime` | 实现 `IInputEngine`，对接稳定的 C 桥接接口 |
| `native` | 复用仓库 `src/rime_api.h`，动态加载官方 Rime，不依赖皮肤代码 |
| `src/TypeFlow.Desktop` | WPF 渲染器、皮肤库、输入实验室、可选小狼毫候选窗宿主 |
| `integration/weasel` | 为固定版小狼毫提供可回退的外部渲染接入 |
| `tests` | 样本、异常导入、真实引擎、换肤、渲染和通信检查 |

Rime 核心源码和原有构建没有改动。现有 C++ 引擎通过 C API 隔离；产品扩展放在独立工程，不需要为了皮肤系统重写词库和输入算法。

## SSF 已支持与降级

- 首版识别文件内容而非扩展名；支持普通 ZIP 容器，UTF-16 LE/BE、UTF-8、GB18030 配置，大小写不敏感的资源匹配。
- 映射 `Scheme_H1` 的 PNG 背景、字体像素字号、BGR 颜色、拼音与候选独立边距、有效的九宫格边界。布局值在 96 DPI 下映射为 DIP。
- 原始配置保存到转换目录 `source/Skin.ini`，原始键值和编码同时记录在 `extensions`；渲染器不读取这些 SSF 字段。
- 样本的背景尺寸为 431×160，纵向参数为 `2,0,1699`，超出图像范围。因此保留有效横向分区，纵向采用整体缩放，并报告降级。
- 该样本的非默认填充模式、GIF、搜狗状态栏、分窗口、竖排、发光/Aero 均未完全映射，结果标记为 `partial`。当前截图是 TypeFlow 的静态渲染，不代表与搜狗逐像素一致。
- 加密 SSF、非 PNG 候选背景、动画和第三方动态可执行插件尚未实现。

原生皮肤现已支持手工添加静态装饰层和独立文字位置，但 SSF 导入器仍保留上述兼容范围。增加编辑能力不会自动恢复导入时未映射的效果，保存自定义副本时也保留原兼容报告。

导入使用新建的随机暂存目录，校验后发布，不覆盖现有目标。限制压缩包大小、条目数、单资源和总展开大小；拒绝路径越界、符号链接、Windows 保留路径及大小写重名。

## 系统输入接入状态

见 [小狼毫接入说明](integration/weasel/README.md)。目前已验证 C++ 发送端可编译并与候选协议匹配；**尚未完成完整小狼毫前端的编译安装和跨应用输入验收**。本机探测未找到 ATL/MFC，所需前端依赖还没有安装。后续先补齐所选前端构建要求，再验证记事本、浏览器及焦点切换。

依赖版本和哈希见 [sources.json](sources.json)。运行时、词库、样本转换图和上游前端源码均放在忽略的生成目录内。授权与分发边界见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
