// Windows 发布版不弹出控制台窗口
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod catalog;
mod commands;
mod config;
mod launcher;
mod menuscan;
mod registry;
mod scanner;
mod icon;

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            commands::get_config,
            commands::save_config,
            commands::scan_tools,
            commands::detect_terminals,
            commands::apply_to_registry,
            commands::remove_all,
            commands::preview_command,
            commands::export_config,
            commands::import_config,
            commands::restart_explorer,
            commands::get_tool_icon,
            commands::scan_existing_menus,
            commands::toggle_existing,
            commands::delete_existing,
            commands::add_existing,
        ])
        .run(tauri::generate_context!())
        .expect("启动 RightMenu 失败");
}
