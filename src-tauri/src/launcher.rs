use crate::config::MenuItem;
use std::env;
use std::path::PathBuf;

/// 把内层模板里的 {exe} 替换为带引号的可执行文件路径。
/// 若用户自定义模板未包含 {exe}，则原样保留（高级用法）。
fn render_inner(item: &MenuItem) -> String {
    let quoted_exe = format!("\"{}\"", item.exe);
    item.command_template.replace("{exe}", &quoted_exe)
}

/// 生成最终写入注册表 command 键的命令字符串。
///
/// - gui：目录作为参数（模板里通常含 "%V"）。.exe 直接执行；.cmd/.bat 用 cmd /c 包裹。
/// - cli：在所选终端中、于 %V 目录里运行该命令。
pub fn render_command(item: &MenuItem, terminal: &str) -> String {
    let inner = render_inner(item);

    if item.kind == "gui" {
        let lower = item.exe.to_lowercase();
        if lower.ends_with(".exe") || !inner.contains(&item.exe) {
            inner
        } else {
            // .cmd/.bat/.ps1 等需要通过 cmd 解释
            format!("cmd.exe /c \"{inner}\"")
        }
    } else {
        // cli：在终端里运行，工作目录为 %V
        match terminal {
            "wt" => format!("wt.exe -d \"%V\" cmd /k {inner}"),
            "powershell" => format!(
                "powershell.exe -NoExit -Command \"Set-Location -LiteralPath '%V'; & {inner}\""
            ),
            "pwsh" => format!(
                "pwsh.exe -NoExit -Command \"Set-Location -LiteralPath '%V'; & {inner}\""
            ),
            // 默认 cmd：/s 配合外层引号正确解析内部引号
            _ => format!("cmd.exe /s /k \"cd /d \"%V\" && {inner}\""),
        }
    }
}

/// 探测系统中可用的终端，供前端下拉选择
pub fn detect_terminals() -> Vec<String> {
    let mut found = Vec::new();
    let dirs: Vec<PathBuf> = env::var_os("PATH")
        .map(|p| env::split_paths(&p).collect())
        .unwrap_or_default();

    let on_path = |exe: &str| -> bool {
        dirs.iter().any(|d| d.join(exe).is_file())
    };

    if on_path("wt.exe") {
        found.push("wt".to_string());
    }
    // cmd 一定存在
    found.push("cmd".to_string());
    if on_path("powershell.exe") {
        found.push("powershell".to_string());
    }
    if on_path("pwsh.exe") {
        found.push("pwsh".to_string());
    }
    found
}
