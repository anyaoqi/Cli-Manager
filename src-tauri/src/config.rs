use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

/// 单个右键菜单项
#[derive(Serialize, Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct MenuItem {
    pub id: String,
    pub name: String,
    pub enabled: bool,
    /// "cli" | "gui"
    #[serde(rename = "type")]
    pub kind: String,
    /// 可执行文件真实路径（claude.cmd / Code.exe ...）
    pub exe: String,
    /// 图标，形如 "C:\\path\\foo.exe,0"，留空则用 exe
    #[serde(default)]
    pub icon: String,
    /// 内层命令模板，占位符 {exe} / %V。默认 cli="{exe}"，gui="{exe} \"%V\""
    pub command_template: String,
    pub order: i64,
    /// "auto" | "custom"
    #[serde(default = "default_source")]
    pub source: String,
    /// 该项适用的位置，留空表示使用全局 positions
    #[serde(default)]
    pub positions: Vec<String>,
}

fn default_source() -> String {
    "custom".to_string()
}

/// 全局配置
#[derive(Serialize, Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct Config {
    pub version: u32,
    pub parent_label: String,
    /// "wt" | "cmd" | "powershell" | "pwsh"
    pub terminal: String,
    /// 默认位置: "background" | "folder" | "desktop"
    pub positions: Vec<String>,
    pub items: Vec<MenuItem>,
}

impl Default for Config {
    fn default() -> Self {
        Config {
            version: 1,
            parent_label: "在此打开".to_string(),
            terminal: "wt".to_string(),
            positions: vec![
                "background".to_string(),
                "folder".to_string(),
                "desktop".to_string(),
            ],
            items: Vec::new(),
        }
    }
}

impl MenuItem {
    /// 该项实际生效的位置（优先用自身 positions，否则回退全局）
    pub fn effective_positions<'a>(&'a self, global: &'a [String]) -> &'a [String] {
        if self.positions.is_empty() {
            global
        } else {
            &self.positions
        }
    }

    /// 实际用于注册表的图标值
    pub fn icon_value(&self) -> String {
        if self.icon.trim().is_empty() {
            format!("{},0", self.exe)
        } else {
            self.icon.clone()
        }
    }
}

/// %APPDATA%\RightMenu\config.json
pub fn config_path() -> PathBuf {
    let base = dirs::config_dir().unwrap_or_else(|| PathBuf::from("."));
    base.join("RightMenu").join("config.json")
}

pub fn load() -> Config {
    let path = config_path();
    match fs::read_to_string(&path) {
        Ok(text) => serde_json::from_str(&text).unwrap_or_default(),
        Err(_) => Config::default(),
    }
}

pub fn save(config: &Config) -> Result<(), String> {
    let path = config_path();
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("创建配置目录失败: {e}"))?;
    }
    let text = serde_json::to_string_pretty(config).map_err(|e| format!("序列化失败: {e}"))?;
    fs::write(&path, text).map_err(|e| format!("写入配置失败: {e}"))?;
    Ok(())
}

/// 把当前配置导出到指定文件路径（JSON）
pub fn export_to_file(path: &str) -> Result<(), String> {
    let cfg = load();
    let text = serde_json::to_string_pretty(&cfg).map_err(|e| format!("序列化失败: {e}"))?;
    fs::write(path, text).map_err(|e| format!("导出失败: {e}"))?;
    Ok(())
}

/// 从指定文件路径导入配置，校验后保存并返回新配置
pub fn import_from_file(path: &str) -> Result<Config, String> {
    let text = fs::read_to_string(path).map_err(|e| format!("读取文件失败: {e}"))?;
    let cfg: Config = serde_json::from_str(&text).map_err(|e| format!("解析配置失败: {e}"))?;
    save(&cfg)?;
    Ok(cfg)
}
