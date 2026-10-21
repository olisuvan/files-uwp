# Files：代码审查与 WinUI 3 升级评估

日期：2026-10-21。审查仓库：olisuvan/files-uwp。
基准提交：a3f5449dbdc065bb2c2cfd4ecb160ccbb76699b4，2019-02-13。

## 结论与实现范围

原项目是采用旧式 csproj 的 UWP 文件管理器，引用 UWP Toolkit 5.0 与
Microsoft.UI.Xaml 2.1 的预发布版本。它不是通过更新一个 NuGet 包就能迁移到
WinUI 3 的项目。UWP 的窗口、应用生命周期、存储权限和控件库都需要调整。

本次采用并行迁移：保留原 Files.sln，新增 Files.Modern.slnx、
Files.WinUI 桌面应用与可独立测试的 Files.Core。新版实现文件夹与磁盘浏览、
前进/后退/上一级、路径输入、当前目录名称筛选、隐藏项开关、系统/浅色/深色主题、
新建文件夹、重命名、系统剪贴板复制/粘贴及交由 Windows 处理的回收站操作。
原 UWP 删除逻辑同时做了小范围修复。

这是一版需要在 Windows 上完成验收的迁移实现。原版照片墙、经典视图、拖放、
文件缩略图、碰撞覆盖界面尚未迁移；新版也没有新增剪切、标签页或递归搜索。
新版粘贴仅复制，遇到同名项目报告失败并保留现有内容。筛选只作用于当前目录。
主题与隐藏项选择目前只保留在本次进程中。

## 原代码的问题

以下位置均对应基准提交，不是新版文件的行号。

| 优先级 | 位置 | 触发方式与影响 | 本次处理 |
| --- | --- | --- | --- |
| P1 | ItemInteractions.cs:664–739 | CloneDirectoryAsync 是 async void，递归调用未 await，且 passNum 是不会重置的静态变量。连续复制或多层目录复制可能混用状态，刷新早于完成，异常无法由调用者接住。 | 新版改为逐层 await 的 Task，操作状态使用局部变量；建立连续复制和多级目录回归测试。旧版复制代码保留为迁移对照，仍有此风险。 |
| P1 | ItemInteractions.cs:717–723 | 替换目录时先删除目标，再开始复制。源路径不可读、磁盘断开或后续失败时，旧目标内容已经丢失。 | 新版拒绝覆盖；先复制到目标同级的唯一临时项，全部完成后用不覆盖移动提交，失败清理临时项。 |
| P1 | ItemInteractions.cs:584–660, 779–810 | 首次粘贴直接进入冲突选择；选择回调再次调用 async void 后立即重置全局标志。异步恢复时标志可能已变回 false；isLoopPaused 也没有形成实际等待机制。 | 新版每次操作快照源项目和目标路径；以可等待 Task 顺序复制并独立记录失败。 |
| P2 | ItemInteractions.cs:499–535 | 删除时遍历实时 SelectedItems，跨 await 后选择可能改变；首个拒绝访问会中断剩余项目，列表刷新只在全部成功时执行，InvalidOperationException 被吞掉。 | 原版已改为选择快照、逐项错误收集和 finally 刷新，并阻止重复删除。新版删除前确认，交给系统回收站，失败后也刷新。 |
| P2 | FileLoader.cs:211–352 | GetItemsAsync 使用 async void，并共享 folder、folderList、fileList 等状态。快速切换目录时，先前请求仍可能把结果加到当前集合。 | 新版读取结果使用局部快照；NavigationSession 用取消与请求序号保证仅最新请求提交，失败不破坏当前路径与历史。 |
| P2 | Files.csproj:15–31, 285–331 | 目标包含旧预览 Windows SDK，使用预发布 UI 包、仓库不存在的临时证书，并写入开发者个人打包目录与证书信息。其他机器难以复现打包。 | 新版使用 SDK 风格项目与固定稳定依赖，不依赖旧证书或个人目录，采用免 MSIX 的自包含目录部署。旧项目打包配置仍需单独维护。 |
| P2 | GenericFileBrowser.xaml.cs:188–210 | 重命名直接操作磁盘，缺少一致的名称、设备保留字与冲突校验。异常可能从 async void 事件逸出。 | 新版集中校验名称，拒绝覆盖，明确显示失败原因。 |

## 技术选择

| 方案 | 适配评估 |
| --- | --- |
| WinUI 3 + Windows App SDK 2.5.1 + .NET 10 | 采用。项目目标是 Windows 文件管理器，原生窗口、Windows Shell 与剪贴板是核心需求；旧版 C# 与 XAML 的知识可延续。 |
| 继续 UWP / WinUI 2 | 仅作为旧版本保留，不作为本次升级的主线。窗口、全信任文件访问与桌面交互仍受旧应用模型约束。 |
| .NET MAUI / Avalonia | 在有明确跨平台目标时重新评估。当前迁移需要 Windows Shell 集成，增加另一层框架没有明确收益。 |
| 预览或实验性 SDK | 本次未采用。首先保证依赖可还原、文件操作有回归覆盖，再评估新控件。 |

新版目标为 Windows 10 2004（19041）及以上，建议使用仍受微软支持的 Windows 11。
编译目标 SDK 为 26100；.NET 10 为 LTS；Windows App SDK 固定到调查时的稳定版 2.5.1。
自包含发布目录必须整体分发，不是只拷贝一个 exe。当前没有 MSIX、商店身份或自动更新。

资料来源（2026-10-21 查询）：

- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
- https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/migrate-winforms-patterns
- https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs
- https://dotnet.microsoft.com/en-us/platform/support/policy
- https://github.com/microsoft/winappCli/tree/main/samples/winui-unpackaged-app

## 验证证据与边界

- 已在 Linux、.NET SDK 10.0.402 上运行核心回归：36/36 通过。
- Files.Core Release 编译通过：0 warning，0 error。
- 新版界面后端 C# 使用实际还原的 Windows SDK / WinUI 引用做了独立 API 编译检查：
  0 warning，0 error。该检查临时提供 XAML 字段声明，不运行 XAML 编译或界面。
- 新版 NuGet 依赖已成功还原。
- 完整 WinUI 构建在 Linux 的 Windows XamlCompiler.exe 启动处停止（Exec format error）。
  因此完整 Windows 构建、XAML 生成、启动、界面交互与回收站行为都尚未通过验收。
- 已加入 GitHub Actions：Windows/Linux 核心测试及 Windows x64/ARM64 发布构建。
  工作流尚未上传或运行，不能把配置文件视为验证通过。
- 初次交付时，向 GitHub 创建分支返回 403 Resource not accessible by integration。
  后续已补齐仓库所属账号的 GitHub App 安装授权；推送与远程验证结果以升级 PR 为准。

## 文件操作的限制

- 复制按单个根项目暂存和提交；多选项目不是跨文件的整体事务，失败前已完成的项目会保留。
- 复制数据流，不复制 ACL、备用数据流、扩展属性或完整时间戳，不能当作备份工具。
- 拒绝符号链接与目录联接，避免常见的路径别名和自递归；不是对抗恶意并发改链的安全沙箱。
- 取消时尽力清理临时项；权限变化或文件占用可能留下 .files-copy-*，不删除源或已有目标。
- 系统回收站操作保留 Windows 的确认和错误对话框。网络盘/可移动介质可能不支持回收站；
  系统永久删除确认仍需 Windows 实测，不能承诺所有位置都可恢复。
- 大目录仍需完整枚举和排序后显示；列表虚拟化且一次替换数据源，但不是分页加载。
- 外观、辅助功能、放大文本、断网磁盘和高 DPI 需按验收清单逐项实测。

## Windows 验收清单

1. 执行 README 的核心测试与 x64 发布命令，确认构建无错；在 ARM64 机器上验证 ARM64 包。
2. 打开系统盘、U 盘、无权限目录与不可用网络目录，确认错误可见且仍能切换到其他位置。
3. 快速连续切换多个大目录，确认旧目录结果不会覆盖新目录；测试后退失败后的历史记录。
4. 复制多级目录与空目录；复制到自身/后代/同名目标应拒绝，原有数据应保持。
5. 在复制中关闭窗口，重新启动检查已完成项目与临时项；模拟目标无空间、文件占用和拔盘。
6. 多选复制/粘贴，验证剪贴板文本提示、外部剪切按复制处理及逐项失败提示。
7. 创建/重命名空白、中文、保留设备名、同名项目；确认失败不覆盖其他文件。
8. 在测试目录删除并从回收站还原；在无回收站位置测试系统确认与取消，不使用重要文件。
9. 在浅色、深色、系统高对比度，100%/150%/200% DPI 和放大文本下检查布局与键盘焦点。
10. 使用纯净 Windows 用户账号启动完整发布目录，确认没有依赖旧 UWP 证书、旧设置或机器目录。
