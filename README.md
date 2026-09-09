# ⚡ CliManager (Cli-Manager)

> **专为开发者（特别是 AI CLI 重度用户）量身打造的 Windows 文件夹空白处右键菜单管理工具。**  
> 优雅组织 Claude Code、OpenCode、Codex CLI、Gemini CLI、Kimi CLI、Warp 等终端与工具，杜绝右键菜单臃肿杂乱。

---

## ✨ 核心特性

- **📂 自定义右键菜单文件夹（一级级联子菜单）**：类似系统原生“新建 ▷”，可自由创建“AI 编程工具 ▷”、“常用终端 ▷”等，将大量 CLI 集中收拢。
- **🎯 顶级直出与级联随心混合**：高频核心工具支持一键直接排布在右键一级菜单；低频工具归入级联文件夹。
- **🖱️ 全流程拖拽自定义排序**：支持通过鼠标拖拽调整工具顺序与归属；通过注册表加权字典序算法保证右键菜单实际展示顺序与 UI **100% 严格一致**。
- **🚀 零提权运行与零运行时依赖（直写架构）**：
  - 受管注册表写入 100% 限制在 `HKCU`，日常运行免管理员提权。
  - 直接写入标准命令行调用字符串，**即使卸载或移动 CliManager，右键菜单依然长久可用**，杜绝死链。
- **🔍 智能探测与内置预设中心**：自动扫描系统与环境已安装的 CLI 工具（Claude Code、OpenCode、Codex CLI、Gemini CLI、Kimi CLI 等），一键快速配置。
- **🔄 存量右键菜单迁移向导**：
  - 自动扫描并识别既有 HKCU / HKLM 右键菜单项；
  - 智能识别旧版 RightMenu 工具遗留配置与无效死链；
  - 自动备份后一键导入接管；非提权模式下自动对 HKLM 冲突项生成覆盖遮罩，避免菜单项重复。
- **🪟 Windows 11 经典菜单一键治理**：内置恢复 Win10 经典右键菜单开关与状态探测，让自定义菜单直接展示在首层，无需每次多点“显示更多选项”。
- **🎨 现代 Fluent Design 设计**：采用 Windows 11 Fluent 质感与深浅主题自动跟随，提供直观的树形编排和属性检查面板。

---

## 🏗️ 架构设计与技术栈

项目严格遵守 Core / UI 分层原则，核心业务与平台 UI 完全解耦：

```text
Cli-Manager
├── src/
│   ├── CliManager.Core       # 核心引擎类库（零 WPF/UI 依赖）
│   │   ├── Models/           # 配置数据模型（config.json）
│   │   ├── Registry/         # 注册表同步引擎、加权排序、遮罩覆盖、MUI 字符串解析
│   │   ├── Launch/           # 终端启动链模板与参数防注入转义引擎
│   │   ├── Detection/        # 本机 CLI 环境智能探测与预设规则库
│   │   ├── Migration/        # 存量右键项扫描、死链检测与接管迁移
│   │   └── Icons/            # 图标提取与资源定位服务
│   └── CliManager.App        # WPF 宿主程序（Fluent UI + MVVM）
│       ├── ViewModels/       # 响应式视图模型（CommunityToolkit.Mvvm）
│       ├── Views/            # 界面视图与弹窗交互
│       └── Converters/       # 数据转换器
├── tests/
│   └── CliManager.Tests      # 核心引擎单元测试（覆盖转义规范、排序模型、迁移解析等）
├── docs/                     # 架构方案设计书与 POC 验证报告
└── build.ps1                 # 全自动化编译与发布脚本
```

- **开发框架**：C# 13 / .NET 8 LTS (`net8.0-windows`)
- **界面与样式**：[WPF-UI](https://wpfui.lepo.co/) (Fluent Design System)
- **MVVM 架构**：CommunityToolkit.Mvvm (源生成器，零反射损耗)
- **测试框架**：xUnit + FluentAssertions (51 项核心单测全覆盖)

---

## 🚀 快速上手

### 运行环境要求
- **操作系统**：Windows 10 1809+ / Windows 11 (x64)
- **运行环境**：
  - 若运行便携发布包（推荐）：无需额外安装运行时（自包含）。
  - 若运行框架依赖版：需要已安装 [.NET Desktop Runtime 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)。

### 源码构建与发布

1. **克隆仓库**：
   ```bash
   git clone https://github.com/anyaoqi/Cli-Manager.git
   cd Cli-Manager
   ```

2. **运行单元测试**：
   ```powershell
   dotnet test tests/CliManager.Tests/CliManager.Tests.csproj
   ```

3. **一键构建并发布独立免安装程序**：
   ```powershell
   .\build.ps1 -Publish
   ```
   发布产物将输出在 `artifacts/publish/` 目录下，双击 `CliManager.App.exe` 即可直接运行。

4. **一键生成发布压缩包与 EXE 安装包**：
   ```powershell
   .\build.ps1 -Installer -Zip -AppVersion 0.0.2
   ```
   产物输出至 `artifacts/release/`：包含便携版 ZIP 压缩包与基于 Inno Setup 自动化打包的单文件 EXE 安装包，支持非管理员免提权安装、桌面与开始菜单快捷方式、双语引导界面及干净卸载。

5. **CI/CD 自动化持续交付**：
   项目配置了 GitHub Actions 自动化流水线（`.github/workflows/release.yml`）。当向仓库推送版本标签（例如 `git tag v0.0.2 && git push origin v0.0.2`）时，云端工作流将自动触发编译、全量单测、打包 EXE 安装包与 ZIP 压缩包，并自动发布 GitHub Release 及生成 SHA-256 校验清单。

---

## 📖 使用指南

### 1. 组织菜单与工具
1. 打开应用，在左侧导航栏点击 **“＋ 新建分组”** 创建如 `AI 编程工具` 的菜单文件夹。
2. 点击 **“＋ 添加工具”**，选择目标 CLI 的可执行文件（或通过“刷新探测”直接导入检测到的工具）。
3. 选择启动终端宿主（Windows Terminal / CMD / PowerShell / 自定义终端），配置环境变量或参数。
4. 鼠标上下拖拽列表项，即可实时调整在右键菜单中的高低排布顺序。
5. 点击右下角 **“保存并同步到右键菜单”** 即可立即生效。

### 2. 存量菜单项一键迁移
1. 切换至 **“设置与迁移”** 页面，或在顶部迁移提示卡片中点击“去迁移”。
2. 系统自动列出本机已注册的既有右键条目（智能标记死链与重复项）。
3. 点击 **“一键导入接管选中项”**，工具将自动备份原配置并生成受管菜单。

### 3. Windows 11 经典菜单治理
- 在设置页中可一键开启 **“使用 Win10 经典菜单”**，让右键菜单直接显示在桌面/文件夹一级右键上，免除二级点击展开。

---

## 🛡️ 数据隔离与安全性

- **受管标记**：所有由 CliManager 生成的注册表条目均携带 `CliManager.Managed = 1` 标识，绝不误触系统或其他第三方软件的右键项。
- **自动备份保障**：每次同步注册表前，系统均会在 `%APPDATA%\CliManager\backups` 自动生成当前配置的 `.reg` 备份，支持快速恢复与回退。
- **配置文件便携性**：支持绿色便携模式（优先读取程序同目录下的 `config.json`，若无则自动落入 `%APPDATA%\CliManager\`）。

---

## 📄 开源许可证

本项目基于 [MIT License](LICENSE) 开源发布。
