use crate::config::{self, Config, MenuItem};
use crate::launcher::{self, render_command};
use crate::menuscan::{self, ExistingEntry};
use crate::registry::{self, ApplyReport};
use crate::scanner::{self, DetectedTool};
use crate::icon;

#[tauri::command]
pub fn get_config() -> Config {
    config::load()
}

#[tauri::command]
pub fn save_config(config: Config) -> Result<(), String> {
    config::save(&config)
}

#[tauri::command]
pub fn scan_tools() -> Vec<DetectedTool> {
    scanner::scan()
}

#[tauri::command]
pub fn detect_terminals() -> Vec<String> {
    launcher::detect_terminals()
}

/// 保存配置并全量同步到注册表
#[tauri::command]
pub fn apply_to_registry(config: Config) -> Result<ApplyReport, String> {
    config::save(&config)?;
    registry::apply(&config)
}

/// 移除所有右键菜单（保留本地配置）
#[tauri::command]
pub fn remove_all() -> Result<(), String> {
    registry::cleanup()
}

/// 给定单项 + 终端，返回最终命令字符串（供 UI 实时预览）
#[tauri::command]
pub fn preview_command(item: MenuItem, terminal: String) -> String {
    render_command(&item, &terminal)
}

/// 把当前配置导出到指定文件路径（JSON）
#[tauri::command]
pub fn export_config(path: String) -> Result<(), String> {
    config::export_to_file(&path)
}

/// 从指定文件路径导入配置，校验后保存并返回新配置
#[tauri::command]
pub fn import_config(path: String) -> Result<Config, String> {
    config::import_from_file(&path)
}

/// 重启 Windows 资源管理器（用于右键菜单不刷新的极少数场景）
#[tauri::command]
pub fn restart_explorer() -> Result<(), String> {
    // 先终止 explorer.exe：用 status() 等待 taskkill 真正完成
    // （explorer 未运行时 taskkill 返回非零退出码，属正常情况，不视为错误）
    let _ = std::process::Command::new("taskkill")
        .args(["/f", "/im", "explorer.exe"])
        .status()
        .map_err(|e| format!("终止 explorer 失败: {e}"))?;
    // 等待短暂时间确保进程完全退出
    std::thread::sleep(std::time::Duration::from_millis(300));
    // 重新启动 explorer.exe（spawn 不等待，explorer 为长期运行进程）
    std::process::Command::new("cmd")
        .args(["/c", "start", "", "explorer.exe"])
        .spawn()
        .map_err(|e| format!("启动 explorer 失败: {e}"))?;
    Ok(())
}

/// 获取 exe 文件图标的 data URI（data:image/png;base64,...），失败返回 null
#[tauri::command]
pub fn get_tool_icon(exe: String) -> Option<String> {
    icon::extract_icon_data_uri(&exe)
}

// ---------- 系统现有右键菜单项（方向2） ----------

/// 扫描系统里所有已存在的右键菜单启动项（各挂载点 + HKCU/HKLM）
#[tauri::command]
pub fn scan_existing_menus() -> Vec<ExistingEntry> {
    menuscan::scan()
}

/// 启用/禁用某个现存菜单项（可逆，用 LegacyDisable）
#[tauri::command]
pub fn toggle_existing(
    hive: String,
    location: String,
    key_name: String,
    enable: bool,
) -> Result<(), String> {
    menuscan::toggle(&hive, &location, &key_name, enable)
}

/// 删除某个现存菜单项
#[tauri::command]
pub fn delete_existing(hive: String, location: String, key_name: String) -> Result<(), String> {
    menuscan::delete(&hive, &location, &key_name)
}

/// 新增一个启动动词到指定挂载点
#[tauri::command]
pub fn add_existing(
    hive: String,
    location: String,
    key_name: String,
    display_name: String,
    command: String,
    icon: String,
) -> Result<(), String> {
    menuscan::add(&hive, &location, &key_name, &display_name, &command, &icon)
}
