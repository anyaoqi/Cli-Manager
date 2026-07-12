/// 内置的已知工具目录。扫描器据此在系统中匹配已安装的工具。
pub struct CatalogTool {
    /// 稳定 id
    pub id: &'static str,
    /// 菜单显示名（"在 X 中打开"）
    pub name: &'static str,
    /// 可执行文件基名（不含扩展名），按顺序探测
    pub probes: &'static [&'static str],
    /// "cli" | "gui"
    pub kind: &'static str,
    /// 内层命令模板，占位符 {exe} / %V
    pub template: &'static str,
    /// GUI 工具优先匹配的真实 exe 名（用于把 code.cmd 提升为 Code.exe）
    pub prefer_exe: &'static [&'static str],
}

/// cli 默认在终端中运行该工具；gui 默认把当前目录作为参数传入。
const CLI_TPL: &str = "{exe}";
const GUI_TPL: &str = "{exe} \"%V\"";

pub fn catalog() -> &'static [CatalogTool] {
    &[
        // ---------- CLI / AI coding tools ----------
        CatalogTool {
            id: "claude-code",
            name: "在 Claude Code 中打开",
            probes: &["claude"],
            kind: "cli",
            template: CLI_TPL,
            prefer_exe: &[],
        },
        CatalogTool {
            id: "codex",
            name: "在 Codex 中打开",
            probes: &["codex"],
            kind: "cli",
            template: CLI_TPL,
            prefer_exe: &[],
        },
        CatalogTool {
            id: "gemini-cli",
            name: "在 Gemini CLI 中打开",
            probes: &["gemini"],
            kind: "cli",
            template: CLI_TPL,
            prefer_exe: &[],
        },
        CatalogTool {
            id: "opencode",
            name: "在 OpenCode 中打开",
            probes: &["opencode"],
            kind: "cli",
            template: CLI_TPL,
            prefer_exe: &[],
        },
        CatalogTool {
            id: "aider",
            name: "在 Aider 中打开",
            probes: &["aider"],
            kind: "cli",
            template: CLI_TPL,
            prefer_exe: &[],
        },
        // ---------- GUI editors ----------
        CatalogTool {
            id: "vscode",
            name: "在 VS Code 中打开",
            probes: &["code"],
            kind: "gui",
            template: GUI_TPL,
            prefer_exe: &["Code.exe"],
        },
        CatalogTool {
            id: "vscode-insiders",
            name: "在 VS Code Insiders 中打开",
            probes: &["code-insiders"],
            kind: "gui",
            template: GUI_TPL,
            prefer_exe: &["Code - Insiders.exe"],
        },
        CatalogTool {
            id: "cursor",
            name: "在 Cursor 中打开",
            probes: &["cursor"],
            kind: "gui",
            template: GUI_TPL,
            prefer_exe: &["Cursor.exe"],
        },
        CatalogTool {
            id: "windsurf",
            name: "在 Windsurf 中打开",
            probes: &["windsurf"],
            kind: "gui",
            template: GUI_TPL,
            prefer_exe: &["Windsurf.exe"],
        },
        CatalogTool {
            id: "sublime",
            name: "在 Sublime Text 中打开",
            probes: &["subl"],
            kind: "gui",
            template: GUI_TPL,
            prefer_exe: &["sublime_text.exe"],
        },
        // ---------- Terminals / shells ----------
        CatalogTool {
            id: "windows-terminal",
            name: "在 Windows Terminal 中打开",
            probes: &["wt"],
            kind: "gui",
            template: "{exe} -d \"%V\"",
            prefer_exe: &[],
        },
        CatalogTool {
            id: "powershell",
            name: "在 PowerShell 中打开",
            probes: &["pwsh", "powershell"],
            kind: "cli",
            template: CLI_TPL,
            prefer_exe: &[],
        },
        CatalogTool {
            id: "git-bash",
            name: "在 Git Bash 中打开",
            probes: &["bash"],
            kind: "gui",
            template: "{exe} --login -i",
            prefer_exe: &[],
        },
    ]
}
