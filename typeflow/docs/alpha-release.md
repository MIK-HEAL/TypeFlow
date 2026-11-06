# 首个 Alpha 便携包

版本来源：Version.props，当前为 0.1.0-alpha.1。TypeFlow 原创代码和自有示例皮肤采用 BSD-3-Clause，见 typeflow/LICENSE；仓库根 Rime 许可证保持原样。

## 用户拿到什么

Windows 10/11 x64 ZIP，完整解压后运行 TypeFlow.exe，无需安装 .NET。
默认显示固定演示候选，并提供“纸间”“夜航”两套示例皮肤。可导入自己的皮肤、关联 JSON/JSONC、拖动布局、撤销/重做和保存副本。

首包是皮肤工作台 Alpha。它不注册系统输入法，不附带 librime、第三方词库、小狼毫或 SSF 测试素材。自行连接 Rime 的入口保留在 ADVANCED.txt；真实系统输入与安装器属于后续里程碑。

本地已生成约 75 MB 的 ZIP，验证了 277 个随包文件；独立原生进程两次启动均成功，并确认使用随包 .NET 10.0.11、保存副本和恢复选择。功能回归为 34 项 Avalonia 检查（含真实 Rime）和 59 项核心/皮肤服务检查通过，编译 0 警告、0 错误。当前本地包来自未提交工作区，manifest 已据实标记；远程草稿应由提交后的 CI 生成。

默认用户数据位置为 LocalApplicationData/TypeFlow/Avalonia；--workspace 可以隔离测试数据。更新程序不覆盖已复制到用户库的示例皮肤。

## 本地构建与验证

在仓库根目录执行，使用已有 .NET SDK 10.0.303 和 PowerShell 7：

```powershell
pwsh -NoProfile -File typeflow/scripts/Build-CrossPlatform.ps1 -Test
pwsh -NoProfile -File typeflow/scripts/Package-Alpha.ps1
pwsh -NoProfile -File typeflow/scripts/Test-AlphaPackage.ps1 -Archive typeflow/build/releases/0.1.0-alpha.1/TypeFlow-0.1.0-alpha.1-win-x64.zip
```

首次自包含发布需还原 Windows .NET 10.0.11 运行时包。版本由项目属性和 NuGet 锁文件固定。Package-Alpha.ps1 拒绝覆盖已有 ZIP；重建时可指定新的 -OutputDirectory。

发布目录包含：

- TypeFlow-版本-win-x64.zip：完整便携程序、自有皮肤、README/ADVANCED、许可与依赖清单。
- 同名 .zip.sha256：压缩包校验值。
- package-manifest.json：版本、源提交、工作区是否有未提交改动、运行时版本，以及包内文件 SHA256。
- RELEASE_NOTES.md：由随版本替换的模板生成。

许可清单根据实际发布的 deps.json 和 NuGet 元数据生成。包中保留依赖原始许可证、native notices 和包来源；MIT-expression 且未附 license 文件的包使用元数据版权声明与标准 MIT 文本。未知许可类型或缺失版权信息会阻止打包。

## 验证范围

Test-AlphaPackage.ps1 验证 ZIP 的 SHA256、文件清单和每个文件内容，确认未包含第三方 SSF 或原生 Rime DLL。随后在项目独立临时目录中解压，用 TypeFlow.exe 直接启动两次：

1. 禁用全局 DOTNET_ROOT 与 dotnet PATH，检查实际运行时目录来自解压包。
2. 使用真实 Windows/Avalonia/Skia 窗口验证两套示例皮肤、候选渲染、保存副本和应用。
3. 以同一隔离用户目录重启，验证当前皮肤持久化。

报告和 PNG 写入 build/evidence/alpha-package。测试使用程序自身的 --smoke-test 入口，不需要用户操作；该入口必须显式指定 --workspace，避免改动真实个人配置。

这些检查证明便携包不依赖开发目录或预装 .NET，不能替代另一台干净 Windows 设备的完整手动体验。首包未做代码签名，完整系统输入、安装器和其他系统的用户包继续独立验收。

## GitHub 发布路径

新增工作流为 **TypeFlow Alpha package**（.github/workflows/typeflow-release.yml）：

- master 上相关变更、PR 或手动运行：执行工作台/共享组件测试、打包、解压包验证，上传可下载 Actions artifact。
- 推送 typeflow-v0.1.0-alpha.1 形式的版本 tag，或手动运行时勾选 create_draft：验证成功后创建 **draft + prerelease**，附 ZIP、SHA256、manifest 和说明。
- 自动流程不会公开发布，也不会覆盖已有 Release。发布前由用户审阅草稿内容和下载包。

草稿生成前会检查 tag/版本一致、源提交一致且工作区无未提交改动。用未提交工作区生成的本地包仅用于审阅，manifest 会标记 sourceDirty=true。

原有 Release CI 继续承担 librime 上游流程。删除 nightly 的操作已限制为官方 rime/librime 仓库；TypeFlow 使用独立 typeflow-v 前缀，避免混淆引擎 tag 和应用版本。
