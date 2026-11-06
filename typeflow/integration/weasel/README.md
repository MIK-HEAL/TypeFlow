# 小狼毫 0.17.4 可选外部候选窗适配

**Alpha.2 当前状态：** 已接入 Windows 安装包的皮肤设置与候选窗，原生前端可回退。下文早期手动集成记录保留作原理说明；其中“未整套构建”和“缺少 ATL”的机器状态已过时。当前构建入口为 `Build-Native.ps1`，安装包说明见 `../../docs/windows-ime-installer.md`。

当前协议使用同用户、同 Windows 会话的双向管道。宿主处理帧后返回一个字节 `1`（已接受）或 `0`（使用原生回退）；只有确认后才隐藏原生候选窗。后台每 250ms 检查连接，单次读写等待 150ms，UI 线程不等待管道。可用状态变化通过消息通知原生窗口，由其 UI 线程刷新，宿主断开后恢复原生候选窗。

皮肤与已选项由原有 ThemeLibrary 管理，导入仍通过插件转换到原生模型。关闭设置页不终止渲染；退出宿主不改动 TSF/Rime 会话。当前不实现外部鼠标选词。隐藏启动后的可见性验证使用 Win32 实际窗口状态，不只检查消息到达和 WPF 属性。

目标是复用小狼毫的 TSF、输入会话和上屏，只替换候选窗显示。这里提供的是**源码集成入口**，不是可直接安装的改版小狼毫。

```powershell
# 在仓库根目录准备固定版源码；不会覆盖已存在的前端源码目录。
pwsh -NoProfile -File typeflow/scripts/Prepare-Runtime.ps1 -IncludeWeasel

# 或向自己准备的 0.17.4 源码应用适配；保留原 WeaselUI.cpp 备份。
pwsh -NoProfile -File typeflow/integration/weasel/Apply-Bridge.ps1 -WeaselSource "path/to/weasel-0.17.4"
```

脚本只修改 `WeaselUI.cpp` 的 `Show/Hide/Refresh/UpdateInputPosition` 接入点，并复制 `TypeFlowEndpoint.h`。缺少预期接入点时拒绝写入；重复运行不重复打补丁。没有改动 TSF、Rime 会话或选词回调。

按上游说明准备 ATL/WTL、Boost、Rime 和其余构建依赖后，编译带适配的前端。在该前端可运行后启动 TypeFlow 宿主：

```powershell
dotnet typeflow/build/app/TypeFlow.Desktop.dll --weasel-host
```

宿主读取管理界面持久保存的当前皮肤。前端通过当前 Windows 会话的命名管道 `TypeFlow.Weasel.v1.<sessionId>` 发送 UTF-8 JSON，每行一条，最多 64 KiB：

```json
{"visible":true,"left":400,"top":400,"right":402,"bottom":426,"view":{"preedit":"ni hao","candidates":[{"text":"你好","comment":"","label":"1"}],"highlighted":0,"page":0,"isLastPage":true}}
```

坐标为屏幕物理像素；宿主根据目标屏幕工作区和 DPI 放置窗口，空间不够时尝试上方。窗口使用不激活样式，键盘选词继续由小狼毫处理。外部宿主暂不转发鼠标选词；管理工具中的本地预览支持鼠标选词。

C++ 端只缓存最新消息，后台线程异步写入；写入等待上限为 100 ms，不在输入线程等待。未连接或写入失败时使用原生候选窗；失联检测发生在下一次发送时，不能据此声称已验证无缝崩溃恢复。宿主缺失或格式无法承载时不会更改输入会话。此协议传输组合文本和候选，只在本机当前用户/Windows 会话内通信，不做日志持久化。

已经验证：固定版源码补丁、单独 C++ 发送端编译、同用户管道候选与坐标消息往返。尚待验证：完整前端编译、安装注册、真实跨应用定位、多屏焦点、宿主故障后的视觉回退。`endpoint_smoke.cpp` 仅为协议测试发送端，不等于 TSF 已验收。

本机的 ATL/MFC 探测没有匹配到已安装组件，因此没有安装或更改系统输入法。上游源码受 GPL-3.0 约束，发布修改版本时必须保留许可并提供相应源码和修改。
