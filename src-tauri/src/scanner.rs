use crate::catalog::{catalog, CatalogTool};
use serde::Serialize;
use std::env;
use std::path::{Path, PathBuf};

/// 扫描到的工具（前端据 id 过滤掉已添加的）
#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct DetectedTool {
    pub id: String,
    pub name: String,
    pub kind: String,
    pub exe: String,
    pub icon: String,
    pub command_template: String,
}

const EXTS: &[&str] = &["exe", "cmd", "bat", "ps1"];

/// 收集所有候选搜索目录：PATH + 一些常见安装位置
fn search_dirs() -> Vec<PathBuf> {
    let mut dirs: Vec<PathBuf> = Vec::new();

    if let Some(path) = env::var_os("PATH") {
        for p in env::split_paths(&path) {
            if !p.as_os_str().is_empty() {
                dirs.push(p);
            }
        }
    }

    // npm 全局
    if let Some(appdata) = env::var_os("APPDATA") {
        dirs.push(PathBuf::from(&appdata).join("npm"));
    }
    // 用户级安装（VS Code / Cursor 等）
    if let Some(local) = env::var_os("LOCALAPPDATA") {
        let base = PathBuf::from(&local).join("Programs");
        for sub in [
            "Microsoft VS Code\\bin",
            "cursor\\resources\\app\\bin",
            "Windsurf\\bin",
        ] {
            dirs.push(base.join(sub));
        }
    }
    // Git Bash 常见位置
    for pf in ["ProgramFiles", "ProgramFiles(x86)"] {
        if let Some(p) = env::var_os(pf) {
            dirs.push(PathBuf::from(&p).join("Git\\bin"));
            dirs.push(PathBuf::from(&p).join("Git\\usr\\bin"));
        }
    }

    // 去重，保持顺序
    let mut seen = std::collections::HashSet::new();
    dirs.retain(|d| seen.insert(d.clone()));
    dirs
}

/// 在搜索目录中查找某个基名的可执行文件，返回首个命中的完整路径
fn find_executable(dirs: &[PathBuf], base: &str) -> Option<PathBuf> {
    for dir in dirs {
        for ext in EXTS {
            let candidate = dir.join(format!("{base}.{ext}"));
            if candidate.is_file() {
                return Some(candidate);
            }
        }
    }
    None
}

/// 对 GUI 工具，尝试把 .cmd 提升为真正的 .exe（在 cmd 所在目录及其上两级查找）
fn resolve_prefer_exe(found: &Path, prefer: &[&str]) -> Option<PathBuf> {
    if prefer.is_empty() {
        return None;
    }
    let mut dir = found.parent();
    for _ in 0..3 {
        let Some(d) = dir else { break };
        for name in prefer {
            let candidate = d.join(name);
            if candidate.is_file() {
                return Some(candidate);
            }
        }
        dir = d.parent();
    }
    None
}

fn detect_one(dirs: &[PathBuf], tool: &CatalogTool) -> Option<DetectedTool> {
    for base in tool.probes {
        let Some(found) = find_executable(dirs, base) else {
            continue;
        };

        // Git Bash：排除 System32 下的 WSL bash.exe
        if tool.id == "git-bash" {
            let lower = found.to_string_lossy().to_lowercase();
            if !lower.contains("git") {
                continue;
            }
        }

        let exe = resolve_prefer_exe(&found, tool.prefer_exe).unwrap_or(found);
        let exe_str = exe.to_string_lossy().to_string();

        return Some(DetectedTool {
            id: tool.id.to_string(),
            name: tool.name.to_string(),
            kind: tool.kind.to_string(),
            exe: exe_str.clone(),
            icon: format!("{exe_str},0"),
            command_template: tool.template.to_string(),
        });
    }
    None
}

/// 扫描系统中所有已安装的已知工具
pub fn scan() -> Vec<DetectedTool> {
    let dirs = search_dirs();
    catalog()
        .iter()
        .filter_map(|tool| detect_one(&dirs, tool))
        .collect()
}
