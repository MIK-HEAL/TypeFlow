# 小狼毫 0.17.4 可选外部候选窗适配

Windows 安装包通过小狼毫的 TSF 候选控制器接入皮肤渲染。构建入口为 `Build-Native.ps1`，安装包说明见 `../../docs/windows-ime-installer.md`。

当前协议使用同用户、同 Windows 会话的双向管道。宿主处理帧后返回一个字节 `1`（已显示）或 `0`（回退原生）。每段组字开始时先等皮肤窗口确认，确认失败后才显示原生窗口，避免每次组字先闪过原生窗口。后台每 250ms 检查连接，单次读写等待上限 150ms，UI 线程通过窗口消息收到结果。

皮肤与已选项由原有 ThemeLibrary 管理，导入仍通过插件转换到原生模型。关闭设置页不终止渲染；退出宿主不改动 TSF/Rime 会话。当前不实现外部鼠标选词。隐藏启动后的可见性验证使用 Win32 实际窗口状态，不只检查消息到达和 WPF 属性。

目标是复用小狼毫的 TSF、输入会话和上屏，只替换候选窗显示。这里提供的是**源码集成入口**，不是可直接安装的改版小狼毫。

```powershell
# 在仓库根目录准备固定版源码；不会覆盖已存在的前端源码目录。
pwsh -NoProfile -File typeflow/scripts/Prepare-Runtime.ps1 -IncludeWeasel

# 或向自己准备的 0.17.4 源码应用候选控制器适配。
pwsh -NoProfile -File typeflow/integration/weasel/Apply-TsfRenderer.ps1 -WeaselSource "path/to/weasel-0.17.4"
```

脚本修改 `WeaselTSF/CandidateList.cpp` 的显示和生命周期接入点，并复制 `TypeFlowEndpoint.h`。后台服务的共享候选窗不会占用皮肤通道。应用通过 TSF 要求自行绘制候选窗时，仍由该应用决定外观。

按上游说明准备 ATL/WTL、Boost、Rime 和其余构建依赖后，编译带适配的前端。在该前端可运行后启动 TypeFlow 宿主：

```powershell
dotnet typeflow/build/app/TypeFlow.Desktop.dll --weasel-host
```

宿主读取管理界面持久保存的当前皮肤。前端通过当前 Windows 会话的命名管道 `TypeFlow.Candidates.v2.<sessionId>` 发送 UTF-8 JSON，每行一条，最多 64 KiB：

```json
{"source":"tsf","sourcePid":1234,"visible":true,"left":400,"top":400,"right":402,"bottom":426,"view":{"preedit":"ni hao","candidates":[{"text":"你好","comment":"","label":"1"}],"highlighted":0,"page":0,"isLastPage":true}}
```

坐标为屏幕物理像素；宿主根据目标屏幕工作区和 DPI 放置窗口，空间不够时尝试上方。窗口使用不激活样式，键盘选词继续由小狼毫处理。外部宿主暂不转发鼠标选词；管理工具中的本地预览支持鼠标选词。

C++ 端只缓存最新消息，组字结束后释放管道。宿主仅在本机当前用户和 Windows 会话内接收组字和候选，诊断状态只保留最近来源进程号和时间。

`endpoint_smoke.cpp` 使用独立管道测试首帧确认、候选和坐标消息及隐藏。跨应用定位和视觉验收需要在真实应用里输入。

上游源码受 GPL-3.0 约束，发布修改版本时必须保留许可并提供相应源码和修改。
