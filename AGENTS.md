# PCL2-R 仓库指示

本文件是本仓库的组成部分，不是可选的参考资料。处理仓库内容前必须完整读取并遵循。

## 项目身份

- 本仓库是 PCL 的 Avalonia 跨平台重实现，产品名称统一为 **PCL2-R**（面向用户的所有界面文案、窗口标题、安装包文件名均使用该名称）。
- 主工程为 `PCL.Avalonia`（Avalonia 11.3.22，.NET 8），解决方案 `PCL.Avalonia.sln`（注意拼写是 Avalonia）。仓库同时保留上游 WPF 工程 `Plain Craft Launcher 2`，仅用于 Windows 版 legacy 打包，新功能一律只做在 Avalonia 工程。
- 使用中文沟通；通用代码需同时考虑 Windows 与 macOS，平台特有代码必须保持明确边界；文本文件统一 UTF-8 无 BOM。

## 发版流程（用户规定的固定收尾动作）

用户明确要求：每完成一批功能改动后，必须更新版本号并发布，不得只提交代码。

1. 改版本号，三处同步为 `1.0.YYYYMMDD`：
   - `PCL.Avalonia/PCL.Avalonia.csproj` 的 `<Version>`
   - `PCL.Avalonia/Assets/Info.plist` 的 `CFBundleVersion`
   - `PCL.Avalonia/Assets/Info.plist` 的 `CFBundleShortVersionString`
2. 版本号规则：格式 `1.0.YYYYMMDD`，不加任何后缀，且必须**大于**上一个已发布版本（按日期向后顺延，已被占用的日期跳过）。
3. 版本号单独作为一次提交，例如 `chore: 版本号升至 1.0.YYYYMMDD`。
4. 打 tag：`git tag v1.0.YYYYMMDD`（tag 必须带 `v` 前缀，工作流只认 `v*`）。
5. 推送：`git push origin main && git push origin v1.0.YYYYMMDD`。
6. tag 推送会触发三个工作流，全部盯到绿灯：
   - `Build Avalonia Desktop Packages`（主工作流：3 个测试 job + 6 个打包 job：win-x64/win-x86/win-arm64 NSIS setup.exe、osx-universal dmg、linux-x64/linux-arm64 AppImage，全部挂到 Release）
   - `Build WPF Windows Packages`（tags 触发，产出 legacy WPF zip，x64/x86 各带三份 checksum）
   - `Build`（legacy WPF build，无害但需确认不红）
   查看命令：`gh run list --repo tyza66/PCL-buildtime --limit 3`
7. 发版成功后核对 Release 资产，6 个 Avalonia 包齐全且各带 `.md5` / `.sha1` / `.sha256` 三份 sidecar（`build-scripts/checksums.py` 生成）：
   `gh release view vX --repo tyza66/PCL-buildtime --json assets --jq '.assets[].name'`

## 环境注意事项

- `dotnet` 位于 `~/.dotnet`，执行前先 `export PATH="$HOME/.dotnet:$PATH"`。
- 测试基线：`dotnet test PCL.Avalonia.sln --nologo` → 475 通过 / 2 跳过（`JavaListServiceTests` 的平台相关用例）/ 0 失败。提交前应保持该基线（数字随新用例增长，以「0 失败」为准）。
- macOS universal dmg 机制（`build-scripts/macos-universal-dmg.sh`）：CoreCLR 硬编码从 appbase 根目录加载 `System.Private.CoreLib.dll`（PE R2R 镜像，无法 lipo 合并），所以单 bundle 内放两份完整 publish（`Contents/MacOS/osx-x64`、`osx-arm64`），根目录放 fat Mach-O C 分发器，按 `uname -m` exec 对应子目录的真 apphost。改分发器或目录名时注意两处字符串必须成对：C 源码里的 `osx-%s` 架构串（`x64`/`arm64`）与 payload 目录名。
- `gh` 默认仓库是上游 `Meloong-Git/PCL`，所有 `gh release` / `gh run` 命令必须显式带 `--repo tyza66/PCL-buildtime`。
- Avalonia Headless 测试平台不会因右键 `MouseDown` 抛 `ContextRequestedEvent`，右键菜单相关行为不要在 headless 测试里断言，需在 macOS 真机验证。
- 在 `PCL.Avalonia` 命名空间内不能直接写 `Avalonia.Threading`（会解析成 `PCL.Avalonia.Threading`），需先 `using Avalonia.Threading;` 再使用 `Dispatcher`。
- 工作目录在外置卷 `/Volumes/Old_Solidity`，偶发 I/O error，批量文件操作需包重试；对文件执行 patch 前先 `tail` / `nl -ba` 确认真实内容。
