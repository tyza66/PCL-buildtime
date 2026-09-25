# PCL2 Avalonia 跨平台壳实施计划

日期：2026-09-25

## 目标

在 `main` 分支直接落地 Avalonia Phase 1 跨平台壳，不创建分支；原有 WPF 工程零改动；打包与验证全部由 GitHub Actions 完成。

## 任务 1：工程骨架

- 创建 `PCL.Avalonia.sln`、`PCL.Avalonia/`（WinExe、net8.0）与 `PCL.Avalonia.Tests/`（xUnit）。
- `dotnet new sln/console/xunit` 生成后再用明确版本替换 csproj，删除模板默认源文件。
- 锁定 Avalonia `11.3.22`、`Avalonia.Themes.Fluent`、`Avalonia.Fonts.Inter`、CommunityToolkit.Mvvm `8.4.2`。
- 关键属性：`AssemblyName=PCL2.Avalonia`、`AvaloniaUseCompiledBindingsByDefault=true`、Nullable + ImplicitUsings。

## 任务 2：TDD 测试先行

- 先写 `JsonSettingsServiceTests.cs`，覆盖：文件缺失回默认、正常往返、损坏 JSON 回默认。
- 先写 `MainWindowViewModelTests.cs`，覆盖：构造应用保存主题并选中第一页、切换导航页、切换主题并持久化。
- 用假 `ISettingsService` / `IThemeService`，不触碰真实 UI 线程。
- `dotnet test` 先失败，再进任务 3。

## 任务 3：服务层

- `AppSettings`：不可变 record，默认 `UseDarkTheme = true`。
- `IPlatformService` / `PlatformService`：Windows `%APPDATA%\PCL2Avalonia`，macOS `~/Library/Application Support/PCL2Avalonia`，Linux `$XDG_CONFIG_HOME/PCL2Avalonia` 或 `~/.config/PCL2Avalonia`。
- `ISettingsService` / `JsonSettingsService`：路径注入；损坏文件回退默认；`.tmp` + `File.Move(overwrite)` 原子保存。
- `IThemeService` / `AvaloniaThemeService`：通过 `Application.RequestedThemeVariant` 应用明暗主题。

## 任务 4：导航与主题 VM

- `NavItemViewModel`：标题 + 页面 VM；`MainWindowViewModel` 持有 5 个固定项：启动 / 下载 / 版本 / 设置 / 其他。
- `SelectedItem` 变化时更新 `CurrentPage`，页面均为占位 VM。
- `ToggleThemeCommand`：翻转 `UseDarkTheme`、调用主题服务、保存设置。

## 任务 5：Avalonia UI

- `Program.cs`：`UsePlatformDetect().WithInterFont()`。
- `App.axaml`：FluentTheme + 合并 `avares://PCL2.Avalonia/Themes/ThemeColors.axaml`，默认深色。
- `Themes/ThemeColors.axaml`：`ThemeDictionaries` 的 Light / Dark 两套颜色。
- `MainWindow`：左侧导航栏（ListBox + 底部主题切换按钮），右侧 `ContentControl` + 5 个 `DataTemplate`。
- `Views/Pages`：5 个占位页，`x:DataType` 指向各自页面 VM。
- `Assets/Info.plist`：macOS `.app` 组装用。

## 任务 6：GitHub Actions

- 覆盖 `.github/workflows/build-desktop.yml` 为 Avalonia 三平台矩阵：
  - 测试 job：windows / macos / ubuntu，`dotnet test`。
  - 发布矩阵 7 个 RID：`win-x64`、`win-x86`、`win-arm64`、`osx-x64`、`osx-arm64`、`linux-x64`、`linux-arm64`。
  - macOS 组装 `.app/Contents/{MacOS,Info.plist}`；Windows/Linux 打 zip/tar.gz；均附 SHA256。
  - `v*` tag 与 `workflow_dispatch` 发布 GitHub Release；PR/main 仅留 artifact。
- 新建 `.github/workflows/build-wpf-windows.yml`，原样保留现有 WPF Windows 打包逻辑。
- 用 `actionlint` 校验 workflow；本地 `dotnet build` + `dotnet test` 收尾。

## 收尾

- 全量改动提交到 `main`，git 日志用中文描述。
- 验收：测试全绿；workflow 语法可解析；原 WPF 文件零改动。

## Phase 2：版本页与离线启动基础

### 目标

- 设置页可配置游戏目录、Java 路径、用户名与最大内存，持久化到 `settings.json`。
- 版本页真实枚举 `<游戏目录>/versions/*/<id>.json`，展示 id、类型、发布时间，支持刷新与选中。
- 启动页基于选中版本和版本 JSON 构建启动参数，支持 `inheritsFrom` 继承链、libraries 本地 classpath、native 库解压、`arguments.jvm/game` 与 `${...}` 标记替换，并以离线账号启动。
- 原 WPF 工程保持零改动，全部逻辑在 `PCL.Avalonia` 内重写，保持单元测试可验证。

### 新增服务

- `MinecraftVersion`：版本元数据（Id、Folder、JsonPath、Type、ReleaseTime、MainClass、InheritsFrom）。
- `IVersionCatalogService`：读取 `versions` 目录下的版本 JSON，损坏文件跳过，按发布时间倒序。
- `IJavaService`：按「设置路径 > JAVA_HOME > 系统 PATH」解析 Java 可执行文件。
- `IGameLauncher`：解析版本 JSON、构建 `LaunchPlan`、解压 native 库并启动 Java 进程。
- `SessionState`：在版本页与启动页之间共享选中的版本。

### 测试

- `VersionCatalogServiceTests`：正常解析、无 JSON 跳过、发布时间倒序。
- `GameLauncherTests`：classpath 包含本地库与版本 jar；JVM/游戏参数含 `-Xmx`、`--gameDir`、`--assetIndex`；native 库路径与解压；标记替换。
- `SettingsPageViewModelTests`：载入与保存设置。
- `JavaServiceTests`：优先使用设置里的路径。

### 验收

- 本地 `dotnet test` 全绿。
- GitHub Actions 三平台测试通过。
- 有本地 Minecraft 安装时，选择版本后能生成可执行启动命令并启动离线游戏。

## Phase 3：版本下载与安装

### 目标

- 下载页从 Mojang / BMCLAPI 拉取官方版本清单，展示可安装的正式版与快照。
- 安装指定版本：写入版本 JSON、主 jar、当前平台所需的 libraries 与 assets，校验大小/SHA-1，已存在文件跳过。
- 设置页新增下载源（Mojang / BMCLAPI），下载页支持搜索、进度、取消与已安装标记。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 新增服务

- `IDownloadClient` / `HttpDownloadClient`：流式下载到 `.tmp` 后原子落盘，支持进度、取消、期望大小校验。
- `IVersionManifestService` / `VersionManifestService`：解析 `version_manifest_v2.json`。
- `DownloadSource` + `DownloadUrlResolver`：统一解析清单、版本 JSON、主 jar、libraries、assets 的 Mojang/BMCLAPI 地址。
- `IVersionInstaller` / `VersionInstaller`：构建下载清单并安装版本。
- `MinecraftRules`：把规则判定抽成共享逻辑，启动器与安装器复用。

### 测试

- `DownloadUrlResolverTests`：Mojang/BMCLAPI 地址映射。
- `VersionManifestServiceTests`：用假下载客户端解析清单。
- `VersionInstallerTests`：假下载客户端下验证 JSON、jar、libraries、assets 落盘、跳过已存在、进度与失败汇总。
- `DownloadPageViewModelTests`：刷新清单、过滤、安装选中版本、取消。
- 更新设置相关测试覆盖下载源字段。

### 验收

- 本地 `dotnet test` 全绿。
- GitHub Actions 三平台测试通过。
- 下载页能从真实网络源刷新并安装一个官方版本，安装后版本页可选中并离线启动。

## Phase 4：安装后自动联动版本页与启动页

### 目标

- 下载页安装成功时通过 `SessionState` 广播版本 ID。
- 版本页收到广播后自动刷新本地版本列表，并选中新安装的版本。
- 启动页通过既有 `SelectedVersion` 订阅自动显示新版本，形成“下载 → 版本 → 启动”闭环。

### 修改文件

- Modify: `PCL.Avalonia/Services/SessionState.cs`
- Modify: `PCL.Avalonia/ViewModels/Pages/DownloadPageViewModel.cs`
- Modify: `PCL.Avalonia/ViewModels/Pages/VersionPageViewModel.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs`
- Create: `PCL.Avalonia.Tests/SessionStateTests.cs`
- Create: `PCL.Avalonia.Tests/VersionPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/DownloadPageViewModelTests.cs`

### 任务 1：先写失败测试

- [ ] 新建 `PCL.Avalonia.Tests/SessionStateTests.cs`，断言 `NotifyVersionInstalled("1.20.1")` 以 `"1.20.1"` 触发 `VersionInstalled` 事件。
- [ ] 新建 `PCL.Avalonia.Tests/VersionPageViewModelTests.cs`，用假设置、假目录扫描器和共享 `SessionState` 构造版本页；先断言 `Refresh` 会选中列表首项，再断言收到 `NotifyVersionInstalled("1.20.1")` 后自动刷新并选中 `1.20.1`。
- [ ] 修改 `PCL.Avalonia.Tests/DownloadPageViewModelTests.cs` 的 helper，为 `DownloadPageViewModel` 传共享 `SessionState`；安装成功用例断言事件收到 `"1.20.1"`。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~VersionPageViewModelTests|FullyQualifiedName~SessionStateTests|FullyQualifiedName~DownloadPageViewModelTests"`，确认因缺失构造参数/事件先红。

### 任务 2：实现联动

- [ ] `SessionState` 增加 `public event EventHandler<string>? VersionInstalled;` 与 `public void NotifyVersionInstalled(string versionId)`，方法校验非空后触发事件。
- [ ] `DownloadPageViewModel` 构造参数增加 `SessionState session` 并保存；`InstallAsync` 成功分支调用 `_session.NotifyVersionInstalled(selected.Id)`。
- [ ] `VersionPageViewModel` 订阅 `_session.VersionInstalled`；事件处理中先 `Refresh()`，再按 ID 不区分大小写选中新版本。
- [ ] `MainWindowViewModel` 的下载页构造调用传入已有 `session`。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- 下载页安装成功后，切到版本页无需手动刷新即能看到并选中该版本；切到启动页可直接看到“当前版本：<id>”。

## Phase 5：启动完整性检查与进程生命周期

### 目标

- 启动前检查主 jar、必要支持库、原生库与资源索引，缺失时给出明确中文错误，不再让 Java 静默失败。
- 游戏进程退出后启动页自动结束“运行中”状态并记录退出码，取消按钮仍可主动终止进程。
- `IGameLauncher.Launch` 返回可等待退出的 `IGameLaunch`，便于测试和 UI 联动。

### 修改文件

- Modify: `PCL.Avalonia/Services/Minecraft/GameLauncher.cs`
- Modify: `PCL.Avalonia/Services/Minecraft/IGameLauncher.cs`
- Modify: `PCL.Avalonia/Services/Minecraft/GameLaunch.cs`
- Create: `PCL.Avalonia/Services/Minecraft/IGameLaunch.cs`
- Create: `PCL.Avalonia/Services/IUiDispatcher.cs`
- Create: `PCL.Avalonia/Services/AvaloniaUiDispatcher.cs`
- Modify: `PCL.Avalonia/ViewModels/Pages/LaunchPageViewModel.cs`
- Modify: `PCL.Avalonia.Tests/GameLauncherTests.cs`
- Create: `PCL.Avalonia.Tests/LaunchPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [ ] 在 `GameLauncherTests` 增加三个用例：删除主 jar 后 `BuildLaunchPlan` 抛 `InvalidOperationException` 且消息包含 jar 名；删除 `com/example/core/.../core-1.0.jar` 后同样抛错并包含 `core-1.0.jar`；删除 `assets/indexes/1.20.json` 后抛 `FileNotFoundException`。
- [ ] 新建 `LaunchPageViewModelTests`，用假 `IGameLauncher`、假 `IGameLaunch` 和同步 `IUiDispatcher` 验证：启动后 `IsRunning` 为 true，`WaitForExitAsync` 完成后 `IsRunning` 自动变 false 且日志含退出码；`Cancel` 调用 `Kill` 并立即结束运行状态。
- [ ] 修改 `MainWindowViewModelTests` 的假启动器，让 `Launch` 返回 `IGameLaunch`（仍抛 `NotSupportedException` 即可）。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~GameLauncherTests|FullyQualifiedName~LaunchPageViewModelTests|FullyQualifiedName~MainWindowViewModelTests"`，确认先红。

### 任务 2：实现启动检查

- [ ] `GameLauncher.BuildLaunchPlan` 在构建 classpath 后收集缺失文件：主 jar、非原生支持库、当前平台原生库；任一缺失则抛 `InvalidOperationException`，消息以“游戏文件不完整”开头并列出文件名。
- [ ] 资源索引文件不存在时抛 `FileNotFoundException`，消息包含索引路径。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~GameLauncherTests"`，确认绿。

### 任务 3：实现进程生命周期

- [ ] 新建 `IGameLaunch`：`ProcessId`、`HasExited`、`WaitForExitAsync(CancellationToken)`、`Kill()`、`Dispose()`；`GameLaunch` 实现并用 `Process.WaitForExitAsync`。
- [ ] `IGameLauncher.Launch` 返回 `IGameLaunch`。
- [ ] 新建 `IUiDispatcher` / `AvaloniaUiDispatcher`，包装 `Dispatcher.UIThread.Post`；`LaunchPageViewModel` 增加可选 `IUiDispatcher` 参数。
- [ ] `LaunchPageViewModel` 启动成功后后台等待 `WaitForExitAsync`，退出后经 dispatcher 清理 `_activeLaunch`、`Dispose`、置 `IsRunning=false` 并记录退出码；取消时 `Kill` 后立即清理。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- 缺失任何关键游戏文件时启动页显示明确原因；游戏自然退出后启动页自动回到可启动状态。
