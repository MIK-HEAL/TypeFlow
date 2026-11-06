# 优先完成真实系统输入：路线校正

日期：2026-11-06。

## 当前产品边界

当前已有输入引擎适配、皮肤转换、编辑和工作台，工作台中的上屏不等于 Windows 其他应用中的系统上屏。现有便携皮肤工作台不能作为可日常使用的系统输入法交付。后续优先完成 M1，暂停新增皮肤功能及新配置语言的实现。

本记录不覆盖当前其他发布、Avalonia 和打包工作的未提交修改，也不把那些工作作为系统输入闭环的验收证据。

## 成熟项目的参考结论

| 参考 | 实际做法 | TypeFlow 应借鉴的内容 |
| --- | --- | --- |
| Fcitx5 Classic UI | `theme.conf` 分节配置、图片、各类 Margin | 清晰的主题目录和字段；优先评估现成主题格式的静态子集，别让用户先手写完整内部模型 |
| Rime / Weasel | 引擎、Windows TSF 前端、YAML 外观配置 | 复用真实系统会话、焦点和上屏；用户设置采用小量覆盖项 |
| niri | KDL、默认配置、校验、指定路径及实时重载 | 借鉴可读性、默认值和配置工作流；它本身不是输入法前端 |

JSON 暂时保留为当前归一化数据模型、编辑器输出和调试格式，不再视为已经确定的最佳手写入口。INI、YAML、KDL 各有取舍；单纯换后缀无法降低模型本身的复杂度。用户入口应先提供默认皮肤和少量覆盖项，复杂图层数据继续由编辑器或导入器生成。

本次未新增其他解析器，也未声称兼容 Fcitx5 主题。

## 当前机器的实测结果

Windows 主机只读检查：

- MSVC x64/x86 编译器存在，所检工具集为 14.51.36231。
- 已准备 Weasel 0.17.4 源码及 WTL 头文件。
- 已有官方 Rime x64 DLL、导入库和公开头文件，可用于后续构建准备；尚未完成整套前端链接。
- 未在所检 MSVC 工具集中找到 ATL 头文件和库。
- 环境变量与所检常见目录中未找到可用 Boost；非标准目录可以通过 `-BoostRoot` 补充检查。

WSL 的检查最初受到沙箱 `E_ACCESSDENIED` 限制，在沙箱外以只读命令复核后：

- `Debian` 正在运行，版本为 WSL 2。
- G++ 14.2.0 可调用，目标为 `x86_64-linux-gnu`。
- CMake 3.31.6 可调用，`startxfce4` 存在。
- `x86_64-w64-mingw32-g++` 和 `fcitx5` 未在本次 Debian PATH 检查中找到。

因此 WSL 已有 Linux C++ 开发能力；不能把 Windows 侧缺少 ATL/Boost 描述为整台机器没有 C++ 编译器。Linux/Fcitx5 路线可以使用这套 G++，Windows 主机输入法仍需 Windows 前端产物。当前默认交付目标继续按 Windows 处理，不因开发环境在 WSL 自动改成 Linux。

可复查 Windows 前置条件：

```powershell
pwsh -NoProfile -File typeflow/scripts/Check-WindowsImePrerequisites.ps1 -ReportPath typeflow/build/evidence/windows-ime-prerequisites.json
```

该脚本只检查并可输出报告，不下载、安装依赖，不注册输入法。检查通过仍不代表构建成功或系统输入通过。

## 下一阶段的交付条件

1. 核实并补齐所选前端构建依赖；新增环境或依赖前明确安装范围。优先复用 Weasel TSF、IPC 和 Rime 服务，不先自写一套键盘钩子。
2. 构建原生前端产物，先使用默认候选窗验证；测试输入不能通过工作台文本框、剪贴板或模拟键盘替代系统会话。
3. 在 Windows 设置中可选择输入法，在记事本、浏览器输入框中完成拼音组合、空格/数字选词、上屏、退格、Esc 和中英文切换。
4. 验证切换应用、焦点丢失、候选定位与退出恢复；后续覆盖代表性 32 位应用、显示缩放和多屏。
5. 系统输入通过后接入 TypeFlow 候选渲染，验证宿主缺失/退出时的回退及换肤保持组合状态。
6. 再完善常用设置、词库持久化、安装卸载和精简的手写主题入口。

前置依赖的检查结果不替代第 2–5 项验收。没有达到这些条件前，继续明确称为工作台/原型。

## 一手资料

- [Fcitx5 默认主题源码](https://github.com/fcitx/fcitx5/blob/master/src/ui/classic/themes/default/theme.conf.in)
- [Fcitx5 主题模型源码](https://github.com/fcitx/fcitx5/blob/master/src/ui/classic/theme.h)
- [Rime 定制指南](https://github.com/rime/home/wiki/CustomizationGuide)
- [Weasel Windows 前端](https://github.com/rime/weasel)
- [niri 配置说明](https://niri-wm.github.io/niri/Configuration:-Introduction.html)
- [Microsoft WSL Linux GUI 应用说明](https://learn.microsoft.com/en-us/windows/wsl/tutorials/gui-apps)
