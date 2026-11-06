# 原生皮肤 v1 与插件契约

格式为 `theme.json` 加包内图片资源目录；两个可直接编辑的例子在 `themes/`。字段采用 camelCase，版本必须为 1，单位为 `dip`，颜色为 `#AARRGGBB`。未知顶层字段拒绝加载，格式扩展写入 `extensions`。未知必要能力拒绝加载，避免静默降级。

读取器也支持 JSONC 注释及末尾逗号；可用 `$schema` 指向公开的 `theme.schema.json` 供编辑器提示。关联外部配置时文件名可自定，资源相对于该配置文件目录。公开 Schema 及 [配置文件使用方法](configuration.md) 与 GUI 共用同一模型。

`layout` 支持 `direction: horizontal`、统一 `padding`、`candidateGap`、`rowGap`、`minWidth`、`minHeight`；可用 `preeditMargin`、`candidateMargin` 分别定位两行。所有 Insets 对象都使用明确的 `left/top/right/bottom` 字段，不依赖数组位置。第二行位于第一行文字下方，再加各自上下边距。`text` 提供字体、DIP 字号、普通颜色和拼音颜色；`selection` 提供选中文字和背景颜色。

`background.mode` 为 `solid`、`stretch`、`tile` 或 `nineSlice`。图片通过 `background.image` 使用包内相对路径；`slice` 是源图像像素的固定边界，不是归一化坐标。九宫格四角保持尺寸，中间分区可通过 `horizontalFill/verticalFill` 分别拉伸或平铺。图片内置 DPI 不改变协议布局：渲染前统一为 96 DPI 逻辑尺寸，最后按显示器 DPI 放大。

`layout.preeditPosition/candidatePosition` 提供独立文字坐标；`layers` 提供装饰图片、锚点、尺寸、透明度与文字前后绘制。它们是数据协议扩展，不要求 SSF 或输入引擎参与。详见 [高级布局](advanced-layout.md)。

`id/name/author/source/license` 保存标识和来源。配置最多 1 MiB，背景文件最多 16 MiB，背景尺寸最多 4096×4096 且不超过 800 万像素。相对路径禁止 `..`、盘符、流名称、符号链接和 Windows 保留名称。字体缺失时由 WPF 和系统回退字体处理。

## 导入器

实现 `TypeFlow.Core.IThemeImporter` 并在组合入口 `ThemeLibrary` 注册。公开的 `PluginInfo` 包括稳定 ID、接口版本（当前为 1）、插件版本和能力列表。`CanImport` 检查数据内容；`ImportAsync` 接受来源、目标和取消令牌，返回原生模型、主题目录和兼容报告。

新增导入器只需引用 `TypeFlow.Core` 并注册，不需要修改引擎、C++ 桥接或渲染器。现有 SSF 和原生目录导入器已经通过同一接口使用。首版采用显式注册，不扫描和加载皮肤包里的代码，也不承诺跨编译器 C++ 插件 ABI。

报告的 `compatibility` 为 `complete` 或 `partial`；无法导入时返回异常，不发布目标目录。报告保存来源文件名、SHA-256、插件标识和带稳定代码的诊断，单独存于 `import-report.json`。渲染器不依赖此报告，也不依赖原始 SSF。

## 输入引擎与渲染器

`IInputEngine` 提供 `ProcessKey`、`SelectCandidate`、`Clear` 和只读 `View`。返回的 `KeyResult` 包括是否处理按键、上屏文字和 `CandidateView`。按键使用 Rime/X11 keysym：退格 `0xff08`、Esc `0xff1b`、方向与翻页为对应 keysym；可见 ASCII 使用字符值。

`CandidateView` 只包含拼音、候选、选中索引与页状态，不包含皮肤字段。WPF `ICandidateRenderer` 只接受原生模型、资源目录和候选视图。换肤不调用 `Clear`、不关闭引擎、不触发重新部署。原生 Rime 运行时每进程仅允许一个实例；重复创建会明确拒绝，并保持已有实例有效。

当前没有插件市场、自动发现、可执行脚本或热加载 ABI。后续需要第三方独立发布时，应先稳定独立进程协议，再扩展发现机制。
