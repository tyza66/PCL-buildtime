# PCL2 Avalonia 跨平台壳设计与 CI 打包方案

日期：2026-09-25

## 背景

现有启动器主体是 WPF + .NET Framework 4.8 + VB.NET，并且重度依赖 Windows API（注册表、`kernel32`/`user32`/`shell32`、`bcrypt`、`msdelta`、NAudio、WebP）。直接把原工程交叉编译到 macOS 或 Linux 不可行。

本次迁移采用 Avalonia，分阶段进行。Phase 1 只交付跨平台基础壳：

- 全新 Avalonia C# 工程，不修改现有 WPF 代码。
- 完整导航壳：左侧「启动 / 下载 / 版本 / 设置 / 其他」，右侧占位页面。
- 明暗主题切换，设置通过 JSON 持久化。
- 平台抽象层，为后续迁移 Win32/注册表逻辑预留边界。
- GitHub Actions 负责三平台多架构的构建、测试与打包。

后续阶段再逐页迁移真实功能，每页迁移都必须保持 CI 绿色。

## 技术选型

- .NET 8 LTS，自包含发布。
- Avalonia 11（当前锁定 `11.3.22`）。
- `CommunityToolkit.Mvvm 8.4.2`，轻量 MVVM。
- `System.Text.Json` 读写设置，不引入额外序列化库。
- xUnit 单元测试，测试逻辑层（设置服务、导航、主题），不依赖真实 UI 线程。

## 目录结构

```text
PCL.Avalonia/
  PCL.Avalonia.csproj
  Program.cs
  App.axaml / App.axaml.cs
  Assets/Info.plist
  Services/
    IPlatformService.cs
    PlatformService.cs
    IThemeService.cs
    AvaloniaThemeService.cs
    AppSettings.cs
    ISettingsService.cs
    JsonSettingsService.cs
  ViewModels/
    MainWindowViewModel.cs
    NavItemViewModel.cs
    Pages/  (5 个占位页 VM)
  Views/
    MainWindow.axaml / MainWindow.axaml.cs
    Pages/ (5 个占位页 + 设置页)
  Themes/
    ThemeColors.axaml
PCL.Avalonia.Tests/
  PCL.Avalonia.Tests.csproj
  JsonSettingsServiceTests.cs
  MainWindowViewModelTests.cs
PCL.Avalonia.sln
.github/workflows/build-desktop.yml
.github/workflows/build-wpf-windows.yml
```

## 架构

### 导航

- `MainWindowViewModel` 持有 `ObservableCollection<NavItemViewModel>` 与 `SelectedItem`。
- 选中导航项时切换到对应页面 VM，主窗口的 `ContentControl` 通过 `DataTemplate` 渲染页面。
- 导航行为全部可在无 UI 的单元测试中验证。

### 主题

- 颜色资源使用 Avalonia `ThemeDictionaries` 的 `Light` / `Dark` 两个 key。
- `AvaloniaThemeService.Apply(bool)` 设置 `Application.RequestedThemeVariant`。
- `MainWindowViewModel.ToggleThemeCommand` 切换主题并立即写回设置。
- 主题切换入口放在左侧导航底部，设置页暂为占位页。

### 设置

- `AppSettings` 为不可变 record，Phase 1 只有 `Theme` 字段。
- `JsonSettingsService` 构造时注入设置文件路径，支持临时目录测试。
- 读取失败（文件损坏、JSON 非法）时回退默认值，不抛异常。
- 写入采用「先写 `.tmp`，再 `File.Move(overwrite)`」的原子替换方式。

### 平台边界

- `IPlatformService.GetConfigDirectory()` 返回各平台配置目录：
  - Windows：`%APPDATA%\PCL2Avalonia`
  - macOS：`~/Library/Application Support/PCL2Avalonia`
  - Linux：`$XDG_CONFIG_HOME/PCL2Avalonia` 或 `~/.config/PCL2Avalonia`
- 后续 Win32、注册表、进程 API 等平台能力统一收敛到 `Services` 或 `Platform` 命名空间，WPF 工程保持不动。

## 错误处理

- 设置文件损坏：静默回退默认值，下次保存时覆盖。
- 设置目录不存在：写入前创建目录。
- 写入失败：向上抛出，由 UI 层提示；Phase 1 不引入日志系统。

## 测试

- `JsonSettingsServiceTests`：缺失文件、正常往返、损坏文件三种行为。
- `MainWindowViewModelTests`：导航切换、主题切换、设置持久化。
- 测试在本地（macOS）和 GitHub Actions 三平台跑 `dotnet test`。

## CI 打包

### `build-desktop.yml`（Avalonia 三平台）

- 测试 job：`windows-latest` / `macos-latest` / `ubuntu-latest`。
- 发布矩阵：
  - Windows：`win-x64`、`win-x86`、`win-arm64`，产物为 zip + SHA256。
  - macOS：`osx-x64`、`osx-arm64`，组装 `.app` 后打 zip + SHA256。
  - Linux：`linux-x64`、`linux-arm64`，产物为 tar.gz + SHA256。
- 发布使用 `dotnet publish -r <rid> --self-contained true`。
- 打 `v*` tag 时自动上传 GitHub Release；同时支持 `workflow_dispatch`。
- PR 与 `main` 推送只跑测试与打包 artifact，不发布 Release。

### `build-wpf-windows.yml`（保留原版 Windows 打包）

- 沿用现有 WPF 打包逻辑，仅 Windows `x64` / `x86`。
- 只响应 `workflow_dispatch` 与 `v*` tag，不阻塞日常 PR。

## 验收标准

- `dotnet build` / `dotnet test` 通过。
- GitHub Actions 三平台测试全部通过。
- 八个 RID（win x64/x86/arm64、osx x64/arm64、linux x64/arm64）均产出带 SHA256 的压缩包。
- 手动启动任一本地产物能看到导航壳、切换页面、切换主题，重启后主题保持。
- 现有 WPF 工程文件零改动。

