TypeFlow {{VERSION}} · 皮肤工作台体验版
Windows 10/11 x64

开始体验
1. 将整个 ZIP 解压到普通文件夹；请勿直接在压缩包内运行。
2. 双击 TypeFlow.exe。无需另行安装 .NET 或开发工具。
3. 左侧选择“纸间”或“夜航”，尝试换肤；也可导入自己的 SSF，
   或关联已有 JSON/JSONC 配置。
4. 打开“拖动布局”后移动文字/已有图层，或调整字号。
5. “保存副本”生成独立皮肤；“设为当前皮肤”保存选择。

这一版包含
- 原生皮肤与普通 ZIP SSF 导入、静态 PNG 候选预览。
- 拖动布局、字号调整、撤销/重做、另存副本。
- 导入兼容报告、外部配置关联与失败回退。
- 两套 TypeFlow 自有示例皮肤。

体验范围
- 默认候选是明确标注的固定演示数据。输入框用于预览拼音文字；
  点击候选演示选择结果。
- 这不是已注册的系统输入法，不能替换记事本或浏览器当前使用的输入法。
- 本包不附带 Rime 运行时、第三方词库或第三方 SSF 素材。
- 高级用户可以自行配置本机 Rime，详见 ADVANCED.txt。
- 当前只接受静态 PNG；加密 SSF、动画、竖排及完整搜狗兼容未实现。
- Windows 体验包未做代码签名；请通过项目 Release 下载并核对 SHA256。

数据位置
默认：%LOCALAPPDATA%\TypeFlow\Avalonia
其中保存皮肤、草稿和选择；不会读取或改动 %APPDATA%\Rime。
如需隔离体验，可从终端运行：
  TypeFlow.exe --workspace "D:\TypeFlow-MyProfile"
删除解压文件夹即可移除程序；如需清理个人皮肤，请自行备份后清理上述数据目录。

反馈
https://github.com/MIK-HEAL/TypeFlow/issues
请附版本、Windows 版本、复现步骤和错误信息。
分享截图/配置前，请检查是否包含自己的输入内容、路径或未获授权的素材。

许可与来源
TypeFlow 原创代码：BSD-3-Clause，见 LICENSE。
依赖许可和原始 notices：见 THIRD_PARTY_NOTICES.md、licenses/。
package-manifest.json 记录构建来源、运行时和随包文件哈希。
