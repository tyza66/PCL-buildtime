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

## Phase 6：Mod 管理页

### 目标

- 新增“Mod管理”导航页，扫描 `<游戏目录>/mods` 下的 `.jar` 与 `.jar.disabled` 文件。
- 支持按名称搜索、启用/禁用（`.jar` 与 `.jar.disabled` 重命名）、删除，并显示大小与修改时间。
- 文件操作封装为 `IModsService`，页面通过 ViewModel 调用，保持单元测试可验证；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Mods/ModInfo.cs`
- Create: `PCL.Avalonia/Services/Mods/IModsService.cs`
- Create: `PCL.Avalonia/Services/Mods/ModsService.cs`
- Create: `PCL.Avalonia/ViewModels/Pages/ModsPageViewModel.cs`
- Create: `PCL.Avalonia/Views/Pages/ModsPageView.axaml` / `.axaml.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs`
- Modify: `PCL.Avalonia/Views/MainWindow.axaml`
- Create: `PCL.Avalonia.Tests/ModsServiceTests.cs`
- Create: `PCL.Avalonia.Tests/ModsPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [ ] 新建 `ModsServiceTests`：mods 目录缺失返回空；扫描识别 `.jar` 与 `.jar.disabled` 并忽略其他文件；`SetEnabled` 通过重命名切换且可反向恢复；`Delete` 删除文件。
- [ ] 新建 `ModsPageViewModelTests`：构造时加载 Mod 列表；切换命令调用服务并更新条目；删除命令调用服务并移除条目；搜索文本过滤列表。
- [ ] 修改 `MainWindowViewModelTests`：构造参数注入假 `IModsService`，导航用例断言“Mod管理”页。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~ModsServiceTests|FullyQualifiedName~ModsPageViewModelTests|FullyQualifiedName~MainWindowViewModelTests"`，确认先红。

### 任务 2：实现服务与页面

- [ ] `ModInfo`：`FileName`、`DisplayName`、`FilePath`、`IsEnabled`、`SizeBytes`、`LastModifiedUtc`。
- [ ] `ModsService`：扫描 `mods` 目录，按显示名排序；启用/禁用重命名；删除文件。
- [ ] `ModsPageViewModel`：加载列表、搜索过滤、异步切换与删除，失败时给出中文状态消息。
- [ ] `MainWindowViewModel` 注入 `IModsService` 并新增“Mod管理”导航项；`MainWindow.axaml` 注册页面模板。
- [ ] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- “Mod管理”页可搜索、启用/禁用、删除本地 `mods` 目录中的 Mod，文件系统状态与列表同步。

## Phase 7：离线账号管理

### 目标

- 新增“账号”导航页，管理本地离线账号：添加、删除、设为默认，并持久化到配置目录 `accounts.json`。
- `SessionState` 持有当前选中账号，启动页优先使用账号名，无账号时回退到设置页的用户名。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Accounts/Account.cs`
- Create: `PCL.Avalonia/Services/Accounts/IAccountService.cs`
- Create: `PCL.Avalonia/Services/Accounts/JsonAccountService.cs`
- Create: `PCL.Avalonia/ViewModels/Pages/AccountsPageViewModel.cs`
- Create: `PCL.Avalonia/Views/Pages/AccountsPageView.axaml` / `.axaml.cs`
- Modify: `PCL.Avalonia/Services/SessionState.cs`
- Modify: `PCL.Avalonia/ViewModels/Pages/LaunchPageViewModel.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs`
- Modify: `PCL.Avalonia/Views/MainWindow.axaml` / `.axaml.cs`
- Create: `PCL.Avalonia.Tests/AccountServiceTests.cs`
- Create: `PCL.Avalonia.Tests/AccountsPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/LaunchPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [x] 新建 `AccountServiceTests`：文件缺失返回空；添加离线账号持久化并成为默认；删除默认账号回退到剩余账号；`SetDefaultAccount` 持久化选择。
- [x] 新建 `AccountsPageViewModelTests`：构造加载账号并把默认账号写入 `SessionState`；添加账号；设为默认；删除账号后会话默认同步。
- [x] 修改 `LaunchPageViewModelTests`：`FakeLauncher` 记录最后一次 `BuildLaunchPlan` 的 `AppSettings`；新增用例断言选中账号名覆盖设置用户名。
- [x] 修改 `MainWindowViewModelTests`：注入假 `IAccountService`，导航用例断言“账号”页。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~AccountServiceTests|FullyQualifiedName~AccountsPageViewModelTests|FullyQualifiedName~LaunchPageViewModelTests|FullyQualifiedName~MainWindowViewModelTests"`，确认先红。

### 任务 2：实现账号服务与页面

- [x] `Account`：`Id`、`Name`、`Type`（`offline`）、`CreatedAt`；`JsonAccountService` 原子读写 `accounts.json`。
- [x] `SessionState` 增加 `SelectedAccount`；`LaunchPageViewModel` 启动时用账号名覆盖 `AppSettings.UserName`。
- [x] `AccountsPageViewModel`：加载、添加、删除、设为默认，并把默认账号同步到会话。
- [x] `MainWindowViewModel` 注入 `IAccountService` 并新增“账号”导航项；`MainWindow.axaml` 注册页面模板。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- 账号页可添加、删除、切换默认离线账号；启动页使用默认账号名离线启动。

## Phase 8：Modrinth Mod 在线下载

### 目标

- 新增“Mod下载”导航页，通过 Modrinth API 搜索 Mod，按游戏版本与加载器过滤，并安装到当前游戏目录的 `mods` 文件夹。
- 复用现有 `IDownloadClient` 流式下载与校验能力，支持进度显示、取消与已存在跳过。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Mods/ModrinthProject.cs` / `ModrinthProjectVersion.cs` / `ModrinthFile.cs`
- Create: `PCL.Avalonia/Services/Mods/IModrinthApi.cs` / `ModrinthApi.cs`
- Create: `PCL.Avalonia/Services/Mods/IModsDownloadService.cs` / `ModsDownloadService.cs`
- Create: `PCL.Avalonia/ViewModels/Pages/ModsDownloadPageViewModel.cs`
- Create: `PCL.Avalonia/Views/Pages/ModsDownloadPageView.axaml` / `.axaml.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs`
- Modify: `PCL.Avalonia/Views/MainWindow.axaml` / `.axaml.cs`
- Create: `PCL.Avalonia.Tests/ModrinthApiTests.cs` / `ModsDownloadServiceTests.cs` / `ModsDownloadPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [x] 新建 `ModrinthApiTests`：用假 `IDownloadClient` 验证搜索 JSON 解析与 facets 查询参数；验证版本 JSON 解析、文件字段与按项目/版本筛选参数。
- [x] 新建 `ModsDownloadServiceTests`：安装主文件到 `mods` 目录并带期望大小/SHA-1 校验；文件已存在时跳过下载；过滤路径穿越文件名。
- [x] 新建 `ModsDownloadPageViewModelTests`：搜索命令加载结果；安装命令调用服务、展示进度并标记已安装；无适配版本时给出中文提示。
- [x] 修改 `MainWindowViewModelTests`：注入假 `IModrinthApi` / `IModsDownloadService`，导航用例断言“Mod下载”页。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~ModrinthApiTests|FullyQualifiedName~ModsDownloadServiceTests|FullyQualifiedName~ModsDownloadPageViewModelTests|FullyQualifiedName~MainWindowViewModelTests"`，确认先红。

### 任务 2：实现 API 客户端与下载服务

- [x] `ModrinthApi`：用 `IDownloadClient.GetStringAsync` 请求搜索与版本接口，按游戏版本/加载器构造 facets。
- [x] `ModsDownloadService`：选主文件（无主文件取第一个），下载到 `<mc>/mods`，已存在跳过，文件名经 `Path.GetFileName` 清理。
- [x] `ModsDownloadPageViewModel`：搜索、选择版本、下载安装、进度与取消，中文状态消息。
- [x] `MainWindowViewModel` 注入 `IModrinthApi` / `IModsDownloadService` 并新增“Mod下载”导航项；`MainWindow.axaml` 注册页面模板。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- Mod 下载页可按版本/加载器搜索并安装 Mod；安装后 Mod 管理页可见。
- GitHub Actions 三平台测试与 7 RID 打包通过。

## Phase 9：CurseForge Mod 下载

### 目标

- 在“Mod下载”页增加来源切换：Modrinth / CurseForge。
- 通过 CurseForge API 搜索 Mod、按游戏版本与加载器过滤文件，并安装到当前游戏目录的 `mods` 文件夹。
- 复用现有 `IDownloadClient` 下载与校验能力；CurseForge 搜索接口需要新增 POST JSON 支持。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Mods/CurseForgeProject.cs` / `CurseForgeModFile.cs`
- Create: `PCL.Avalonia/Services/Mods/ICurseForgeApi.cs` / `CurseForgeApi.cs`
- Create: `PCL.Avalonia/Services/Mods/ICurseForgeDownloadService.cs` / `CurseForgeDownloadService.cs`
- Modify: `PCL.Avalonia/Services/Downloads/IDownloadClient.cs` / `HttpDownloadClient.cs`
- Modify: `PCL.Avalonia/ViewModels/Pages/ModsDownloadPageViewModel.cs`
- Modify: `PCL.Avalonia/Views/Pages/ModsDownloadPageView.axaml`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs`
- Modify: `PCL.Avalonia/Views/MainWindow.axaml.cs`
- Create: `PCL.Avalonia.Tests/CurseForgeApiTests.cs` / `CurseForgeDownloadServiceTests.cs`
- Modify: `PCL.Avalonia.Tests/ModsDownloadPageViewModelTests.cs` / `MainWindowViewModelTests.cs`
- Modify: 既有测试中的假 `IDownloadClient` 实现 `PostJsonAsync`

### 任务 1：先写失败测试

- [x] 扩展 `IDownloadClient`：新增 `PostJsonAsync(urls, json, cancellationToken)`。
- [x] 新建 `CurseForgeApiTests`：用假 `IDownloadClient` 验证搜索 POST 的 JSON body 含 `gameId=432`、`classId=6` 与搜索词；验证文件列表 GET 的 `gameVersion` 与 `modLoaderType` 参数；验证 camelCase JSON 解析。
- [x] 新建 `CurseForgeDownloadServiceTests`：下载主文件到 `mods` 目录并带大小/SHA-1 校验；已存在跳过；过滤路径穿越文件名。
- [x] 修改 `ModsDownloadPageViewModelTests`：构造新增 CurseForge API/下载服务参数；新增来源切换后搜索走 CurseForge、安装 CurseForge 文件的用例。
- [x] 修改 `MainWindowViewModelTests`：注入假 `ICurseForgeApi` / `ICurseForgeDownloadService`。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln --filter "FullyQualifiedName~CurseForgeApiTests|FullyQualifiedName~CurseForgeDownloadServiceTests|FullyQualifiedName~ModsDownloadPageViewModelTests|FullyQualifiedName~MainWindowViewModelTests"`，确认先红。

### 任务 2：实现 CurseForge 客户端与下载服务

- [x] `CurseForgeApi`：搜索走 `PostJsonAsync` 到 `/v1/mods/search`；文件列表走 `GetStringAsync` 到 `/v1/mods/{id}/files`；加载器映射 Fabric/Forge/NeoForge/Quilt/Paper/Spigot 的 `modLoaderType`。
- [x] `CurseForgeDownloadService`：选择最新文件下载到 `<mc>/mods`，已存在跳过，文件名经 `Path.GetFileName` 清理。
- [x] `ModsDownloadPageViewModel` 增加来源属性，按来源调用对应 API 并共享进度/取消/状态逻辑。
- [x] `MainWindow.axaml` 注册不变（同一页面 VM），`ModsDownloadPageView` 增加来源选择控件。
- [x] `MainWindow.axaml.cs` 注入真实 CurseForge 服务。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- Mod 下载页可在 Modrinth / CurseForge 之间切换并搜索、安装 Mod。
- GitHub Actions 三平台测试与 7 RID 打包通过。

## Phase 10：整合包下载

### 目标

- 新增“整合包”导航页，通过 CurseForge 搜索整合包并按游戏版本过滤。
- 选择整合包后获取最新支持文件，下载对应 `.zip` 到游戏目录下的 `downloads` 文件夹。
- 复用 `IDownloadClient` 大小/SHA-1 校验、进度、取消能力；原 WPF 工程零改动。

### 修改文件

- Modify: `PCL.Avalonia/Services/Mods/ICurseForgeApi.cs` / `CurseForgeApi.cs`（搜索支持 `classId`，新增 `GetModpackFilesAsync`）
- Create: `PCL.Avalonia/Services/Mods/ICurseForgeModpackService.cs` / `CurseForgeModpackService.cs`
- Create: `PCL.Avalonia/ViewModels/Pages/IntegrationPacksPageViewModel.cs`
- Create: `PCL.Avalonia/Views/Pages/IntegrationPacksPageView.axaml` / `.axaml.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs` / `Views/MainWindow.axaml` / `Views/MainWindow.axaml.cs`
- Create: `PCL.Avalonia.Tests/CurseForgeModpackServiceTests.cs`
- Create: `PCL.Avalonia.Tests/IntegrationPacksPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/CurseForgeApiTests.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [x] `CurseForgeApiTests`：新增 `SearchProjectsAsync` 支持 `classId` 参数断言；新增 `GetModpackFilesAsync` 拼接 `/v1/mods/{id}/files` 并解析文件。
- [x] `CurseForgeModpackServiceTests`：搜索只保留 classId=4471 的整合包；文件列表只取包含 `modpack` 的 manifest；下载落盘 `downloads` 目录并带校验；已存在跳过；过滤路径穿越。
- [x] `IntegrationPacksPageViewModelTests`：搜索加载整合包；安装调用服务并标记已安装；无适配版本时提示中文。
- [x] `MainWindowViewModelTests`：注入假 `ICurseForgeModpackService`，导航断言“整合包”页。

### 任务 2：实现整合包服务与页面

- [x] `ICurseForgeApi` 扩展 `GetModpackFilesAsync(projectId, gameVersion, cancellationToken)`，内部请求文件列表并筛选 `modpack` 类型的 manifest。
- [x] `CurseForgeModpackService`：搜索 `classId=4471` 整合包、选最新适配文件，下载到 `<mc>/downloads/<filename>`，已存在跳过。
- [x] `IntegrationPacksPageViewModel`：搜索、安装下载、进度/取消、中文状态。
- [x] `MainWindowViewModel` 注入服务并新增“整合包”导航项；`MainWindow.axaml` 注册页面模板；`MainWindow.axaml.cs` 创建真实服务。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- 整合包页可搜索并下载 CurseForge 整合包，下载产物出现在 `downloads` 目录。
- GitHub Actions 三平台测试与 7 RID 打包通过。

## Phase 11：整合包解压安装

### 目标

- 整合包页“安装”按钮把已下载的 CurseForge 整合包 zip 真正安装到游戏目录。
- 解析 `manifest.json`，缺失原版时先补装对应 Minecraft 版本，再按 `files` 下载必需 Mod 到 `mods`，最后把 `overrides` 覆盖文件写入游戏目录。
- 文件名与 zip 路径均做穿越过滤；进度按“读取 manifest / 原版 / Mod / 覆盖文件”分阶段展示，支持取消。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Mods/ModpackManifest.cs`（manifest 解析模型）
- Create: `PCL.Avalonia/Services/Mods/IModpackInstallerService.cs` / `ModpackInstallerService.cs`
- Modify: `PCL.Avalonia/Services/Mods/ICurseForgeApi.cs` / `CurseForgeApi.cs`（新增 `GetFileAsync` 单文件详情接口）
- Modify: `PCL.Avalonia/ViewModels/Pages/IntegrationPacksPageViewModel.cs`（下载与完整安装两个动作、分阶段进度）
- Modify: `PCL.Avalonia/Views/Pages/IntegrationPacksPageView.axaml`（已下载/已安装标记与下载、安装按钮）
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs` / `Views/MainWindow.axaml.cs`
- Create: `PCL.Avalonia.Tests/ModpackInstallerServiceTests.cs`
- Modify: `PCL.Avalonia.Tests/CurseForgeApiTests.cs` / `CurseForgeModpackServiceTests.cs` / `ModsDownloadPageViewModelTests.cs` / `IntegrationPacksPageViewModelTests.cs` / `MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [x] `ModpackInstallerServiceTests`：缺失原版时调用 `IVersionInstaller` 补装；已存在版本 JSON 时跳过；manifest 只下载必需 Mod、跳过可选；`overrides` 解压到游戏目录；路径穿越条目被阻止；缺 `manifest.json` 抛错。
- [x] `CurseForgeApiTests`：`GetFileAsync` 请求 `/v1/mods/{id}/files/{fileId}` 并解析单文件；空响应返回 null。
- [x] `IntegrationPacksPageViewModelTests`：下载命令只下载 zip 并标记“已下载”；安装命令先下载再调用安装器，成功标记“已安装”；失败显示中文状态。
- [x] 所有实现 `ICurseForgeApi` 的测试假对象补齐 `GetFileAsync`。

### 任务 2：实现安装服务与页面

- [x] `ModpackManifest` 解析 CurseForge `manifest.json` 的 name/version/minecraft/files/overrides。
- [x] `ModpackInstallerService`：读取 manifest → 按需补装原版 → 下载必需 Mod → 解压 overrides；错误逐项汇总，进度分阶段上报。
- [x] `CurseForgeApi.GetFileAsync` 获取 manifest 指定的单个文件详情。
- [x] 整合包页拆分“下载”和“安装”按钮；安装后显示整合包名、原版版本与 Mod 数量。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿（96 个测试）。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- 整合包下载后可一键安装：原版版本、必需 Mod、覆盖配置文件均落到游戏目录。
- GitHub Actions 三平台测试与 7 RID 打包通过。

## Phase 12：微软正版账号登录

### 目标

- 账号页新增“登录微软账号”，使用与开源 PCL2 一致的设备码 OAuth 流程，浏览器打开微软验证页。
- ClientId 从环境变量 `PCL_MS_CLIENT_ID` 读取，未配置时登录页给出中文提示。
- 登录后保存正版账号（UUID、访问令牌、刷新令牌、过期时间、皮肤/披风地址），启动时用真实 UUID / Token / `userType=msa` 替换 `${auth_uuid}`、`${auth_access_token}`、`${access_token}`、`${auth_session}`、`${user_type}`。
- 令牌过期后启动页先自动刷新，刷新失败提示重新登录。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Accounts/MicrosoftAccountSession.cs`
- Create: `PCL.Avalonia/Services/Accounts/IMicrosoftAuthenticationService.cs` / `MicrosoftAuthenticationService.cs`
- Create: `PCL.Avalonia/Services/Platform/IBrowserLauncher.cs` / `DefaultBrowserLauncher.cs`
- Modify: `PCL.Avalonia/Services/Accounts/Account.cs` / `IAccountService.cs` / `JsonAccountService.cs`
- Modify: `PCL.Avalonia/Services/Minecraft/IGameLauncher.cs` / `GameLauncher.cs`（`BuildLaunchPlan` 接受账号）
- Modify: `PCL.Avalonia/ViewModels/Pages/AccountsPageViewModel.cs` / `LaunchPageViewModel.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs` / `Views/MainWindow.axaml.cs`
- Modify: `PCL.Avalonia/Views/Pages/AccountsPageView.axaml`
- Create: `PCL.Avalonia.Tests/MicrosoftAuthenticationServiceTests.cs`
- Modify: `PCL.Avalonia.Tests/AccountServiceTests.cs` / `GameLauncherTests.cs` / `LaunchPageViewModelTests.cs` / `AccountsPageViewModelTests.cs` / `MainWindowViewModelTests.cs`

### 任务

- [x] `MicrosoftAuthenticationService` 实现设备码申请、轮询、刷新、XBL/XSTS/Minecraft 登录、资格验证与玩家档案获取，错误映射为中文提示。
- [x] `JsonAccountService.AddMicrosoftAccount` 按 UUID 更新已有正版账号，离线账号继续写标准 `OfflinePlayer` UUID。
- [x] `GameLauncher.BuildLaunchPlan` 对微软账号写入真实 UUID / Token / `msa`。
- [x] `LaunchPageViewModel` 启动前刷新过期令牌并传入启动器。
- [x] 账号页 AXAML 增加“登录微软账号”按钮与登录状态。
- [x] 新增 `MicrosoftAuthenticationServiceTests`，用假 `HttpMessageHandler` 与假浏览器覆盖登录序列、错误映射、刷新与未配置 ClientId。
- [x] 运行 `~/.dotnet/dotnet test PCL.Avalonia.sln`，确认全绿。

### 验收

- 本地 `dotnet test` 全绿，构建 0 警告。
- 账号页可用设备码登录微软账号，登录后账号持久化并可设默认。
- 启动正版账号时使用真实 UUID / Token / `msa`，令牌过期自动刷新。
- GitHub Actions 三平台测试与 7 RID 打包通过。

## Phase 13：自定义启动参数

### 目标

- 设置页新增“JVM 启动参数”与“游戏启动参数”文本编辑区。
- 启动器解析自定义参数并按空格、引号与换行拆分，支持 `${version_name}`、`${auth_player_name}` 等既有占位符。
- 自定义参数继承默认参数一起参与去重和标记替换，用户可覆盖默认值。
- 顺手修复全量测试偶发竞态：退出流程测试等待真实清理完成、下载进度收集线程安全。

### 修改文件

- Modify: `PCL.Avalonia/Services/AppSettings.cs`（新增 `JvmArguments`、`GameArguments`）
- Modify: `PCL.Avalonia/Services/Minecraft/GameLauncher.cs`（JVM/游戏参数追加与换行切分）
- Modify: `PCL.Avalonia/ViewModels/Pages/SettingsPageViewModel.cs`
- Modify: `PCL.Avalonia/Views/Pages/SettingsPageView.axaml`
- Modify: `PCL.Avalonia/ViewModels/Pages/LaunchPageViewModel.cs`（退出日志先于运行态清理）
- Modify: `PCL.Avalonia.Tests/SettingsPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/JsonSettingsServiceTests.cs`
- Modify: `PCL.Avalonia.Tests/GameLauncherTests.cs`
- Modify: `PCL.Avalonia.Tests/LaunchPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/VersionInstallerTests.cs`

### 任务

- [x] 先在 `SettingsPageViewModelTests`、`JsonSettingsServiceTests`、`GameLauncherTests` 写失败测试覆盖加载、保存、参数追加与占位符替换。
- [x] `AppSettings` 增加字段并按需在 JSON 设置中往返。
- [x] `GameLauncher` 追加自定义 JVM/游戏参数，`SplitArguments` 支持换行分隔。
- [x] 设置页 AXAML 增加两个参数输入框。
- [x] 修复测试竞态：退出测试等待 `FakeGameLaunch.DisposeTcs`，进度收集改为线程安全。
- [x] 本地 `dotnet build` 0 警告，`dotnet test` 全绿。

### 验收

- 设置页可保存自定义 JVM/游戏启动参数。
- 启动时自定义参数进入实际命令行并替换占位符。
- 全量测试连续多轮稳定通过。

## Phase 14：Fabric 加载器安装

### 目标

- 新增“Fabric”导航页，输入游戏版本后从 BMCL/官方 Fabric meta 获取加载器版本列表。
- 选择加载器后自动补齐原版版本、写入 Fabric profile JSON，并按 Maven 坐标下载全部支持库到 `libraries` 目录。
- 支持库下载失败逐项汇总并继续，库名做路径穿越过滤；安装成功后通知版本目录刷新。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Minecraft/IFabricLoaderService.cs` / `FabricLoaderService.cs`
- Create: `PCL.Avalonia/ViewModels/Pages/FabricLoaderPageViewModel.cs`
- Create: `PCL.Avalonia/Views/Pages/FabricLoaderPageView.axaml` / `.axaml.cs`
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs` / `Views/MainWindow.axaml` / `Views/MainWindow.axaml.cs`
- Create: `PCL.Avalonia.Tests/FabricLoaderServiceTests.cs`
- Create: `PCL.Avalonia.Tests/FabricLoaderPageViewModelTests.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`

### 任务 1：先写失败测试

- [x] `FabricLoaderServiceTests`：meta 列表解析并按稳定版优先排序；安装时补齐原版、写 profile JSON、按 Maven 坐标下载库；已装原版跳过；单库失败继续并汇总；非法库名拒绝。
- [x] `FabricLoaderPageViewModelTests`：刷新加载版本列表；安装调用服务并通知 `SessionState`；失败显示中文摘要。
- [x] `MainWindowViewModelTests`：注入假 `IFabricLoaderService`，新增“Fabric”导航断言。

### 任务 2：实现加载器安装服务与页面

- [x] `FabricLoaderService`：BMCL + 官方 meta 双源获取版本；profile JSON 落盘到 `versions/<id>`；库下载走 BMCL Maven 镜像 + 原地址；路径穿越拒绝。
- [x] `FabricLoaderPageViewModel`：游戏版本输入、加载器列表、安装/取消、分阶段进度与中文状态。
- [x] 主窗口注入真实服务并注册页面模板。
- [x] 本地 `dotnet build` 0 警告，`dotnet test` 全绿（118 个测试）。

### 验收

- Fabric 页可按游戏版本列出稳定版优先的加载器并一键安装。
- 安装产物包含 profile JSON 与完整支持库，安装成功后在版本页可见。
- GitHub Actions 三平台测试与 7 RID 打包通过。

## Phase 15：Forge / NeoForge 加载器安装

### 目标

- 新增“Forge”导航页，可在 Forge / NeoForge 两种模式间切换。
- Forge 从 BMCL/官方列表获取指定游戏版本条目，按 installer jar > universal zip > client zip 选文件并处理版本分支特判。
- NeoForge 从 BMCL/官方 Maven API 获取全量版本，正则解析、排除 `47.1.82`、按版本号排序。
- 新版 Forge（首段 >= 20）与 NeoForge 安装：补齐原版版本、下载 installer、合并 `install_profile.json` / `version.json` 支持库并按规则过滤、下载库到 `libraries`、运行真实注入器后复制新增版本 JSON。
- 旧版 Forge（首段 < 20）支持无 `install` 与有 `install` 两种 Legacy 安装方式。
- 支持库路径做穿越过滤；单库失败逐项汇总；安装成功后通知版本目录刷新。
- 真实 Java 注入器支持 JavaWrapper 失败后无 Wrapper 重试，Java 9+ 追加 `--add-exports`。
- 保持单元测试可验证，不依赖真实网络；原 WPF 工程零改动。

### 修改文件

- Create: `PCL.Avalonia/Services/Minecraft/IForgelikeLoaderService.cs` / `ForgelikeLoaderService.cs`
- Create: `PCL.Avalonia/Services/Minecraft/IForgelikeInstallRunner.cs` / `JavaForgelikeInstallRunner.cs`
- Create: `PCL.Avalonia/ViewModels/Pages/ForgelikeLoaderPageViewModel.cs`
- Create: `PCL.Avalonia/Views/Pages/ForgelikeLoaderPageView.axaml` / `.axaml.cs`
- Create: `PCL.Avalonia.Tests/ForgelikeLoaderServiceTests.cs`
- Create: `PCL.Avalonia.Tests/ForgelikeLoaderPageViewModelTests.cs`
- Create: `PCL.Avalonia.Tests/JavaForgelikeInstallRunnerTests.cs`
- Modify: `PCL.Avalonia/PCL.Avalonia.csproj`（内嵌 jar 资源与 InternalsVisibleTo）
- Modify: `PCL.Avalonia/ViewModels/MainWindowViewModel.cs` / `Views/MainWindow.axaml` / `Views/MainWindow.axaml.cs`
- Modify: `PCL.Avalonia.Tests/MainWindowViewModelTests.cs`
- Copy: `Plain Craft Launcher 2/Resources/forge-installer.jar`、`PCLCS/Launch/JavaWrapper.jar` 到 `PCL.Avalonia/Assets/`

### 任务 1：先写失败测试

- [x] `ForgelikeLoaderServiceTests`：Forge BMCL JSON 解析与文件优先级/分支特判；NeoForge 官方与 BMCL 列表解析、排除 `47.1.82` 与排序；新版 Forge / NeoForge 安装（补齐原版、下载 installer、合并库并下载、fake runner 写新增版本 JSON）；旧版 Forge 两种 Legacy 方式；库失败汇总与路径穿越拒绝。
- [x] `ForgelikeLoaderPageViewModelTests`：Forge / NeoForge 模式刷新、安装调用与 `SessionState` 通知、失败摘要、Forge 缺游戏版本提示。
- [x] `JavaForgelikeInstallRunnerTests`：Java 版本输出解析、Java 9+ 参数与 Wrapper 参数构建。
- [x] `MainWindowViewModelTests`：注入假 `IForgelikeLoaderService`，新增“Forge”导航断言。

### 任务 2：实现加载器安装服务与页面

- [x] `ForgelikeLoaderService`：双源列表、下载 URL（BMCL 优先镜像）、新版库合并/过滤/下载/去原始项、Legacy 两种安装、路径穿越拒绝、注入器调用与新增版本 JSON 复制。
- [x] `JavaForgelikeInstallRunner`：解包内嵌 jar、选择 Java、JavaWrapper 优先与失败重试、输出 `true` 判定。
- [x] `ForgelikeLoaderPageViewModel`：模式切换、版本列表、安装/取消、分阶段进度与中文状态。
- [x] 主窗口注入真实服务并注册页面模板；内嵌 `forge-installer.jar` / `JavaWrapper.jar`。
- [x] 本地 `dotnet build` 0 警告，`dotnet test` 全绿（118 + 新增测试）。

### 验收

- Forge 页可按游戏版本列出 Forge 条目、按全量列表列出 NeoForge 条目并一键安装。
- 新版安装产物包含目标版本 JSON 与完整支持库；旧版安装产物按 Legacy 方式落盘；成功后在版本页可见。
- GitHub Actions 三平台测试与 7 RID 打包通过。
