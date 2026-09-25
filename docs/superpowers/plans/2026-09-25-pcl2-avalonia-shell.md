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
