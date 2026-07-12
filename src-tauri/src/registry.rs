use crate::config::{Config, MenuItem};
use crate::launcher::render_command;
use serde::Serialize;
use winreg::enums::*;
use winreg::RegKey;

/// 一个挂载位置：父菜单键 + 对应的命令存储键
struct Mount {
    pos: &'static str,
    /// 父项路径（HKCU 下）
    parent_path: &'static str,
    /// ExtendedSubCommandsKey 的值（相对 HKEY_CLASSES_ROOT 解析）
    store_rel: &'static str,
    /// 命令存储路径（HKCU 下）
    store_path: &'static str,
}

fn mounts() -> [Mount; 3] {
    [
        Mount {
            pos: "background",
            parent_path: r"Software\Classes\Directory\Background\shell\RightMenu.Tools",
            store_rel: "RightMenu.Store.Background",
            store_path: r"Software\Classes\RightMenu.Store.Background",
        },
        Mount {
            pos: "folder",
            parent_path: r"Software\Classes\Directory\shell\RightMenu.Tools",
            store_rel: "RightMenu.Store.Folder",
            store_path: r"Software\Classes\RightMenu.Store.Folder",
        },
        Mount {
            pos: "desktop",
            parent_path: r"Software\Classes\DesktopBackground\Shell\RightMenu.Tools",
            store_rel: "RightMenu.Store.Desktop",
            store_path: r"Software\Classes\RightMenu.Store.Desktop",
        },
    ]
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ApplyReport {
    pub positions_written: usize,
    pub items_written: usize,
}

/// 删除本工具写入的所有键（幂等，忽略不存在）
pub fn cleanup() -> Result<(), String> {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    for m in mounts() {
        let _ = hkcu.delete_subkey_all(m.parent_path);
        let _ = hkcu.delete_subkey_all(m.store_path);
    }
    Ok(())
}

/// 全量同步：先清理，再按启用项重建
pub fn apply(config: &Config) -> Result<ApplyReport, String> {
    cleanup()?;
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let mut positions_written = 0usize;
    let mut items_written = 0usize;

    for m in mounts() {
        let mut items: Vec<&MenuItem> = config
            .items
            .iter()
            .filter(|it| {
                it.enabled
                    && it
                        .effective_positions(&config.positions)
                        .iter()
                        .any(|p| p == m.pos)
            })
            .collect();
        if items.is_empty() {
            continue;
        }
        items.sort_by_key(|it| it.order);

        // 父菜单项
        let (parent, _) = hkcu
            .create_subkey(m.parent_path)
            .map_err(|e| format!("创建父菜单失败: {e}"))?;
        parent
            .set_value("MUIVerb", &config.parent_label)
            .map_err(|e| e.to_string())?;
        parent
            .set_value("ExtendedSubCommandsKey", &m.store_rel.to_string())
            .map_err(|e| e.to_string())?;
        if let Some(first) = items.first() {
            let icon = first.icon_value();
            if !icon.is_empty() {
                let _ = parent.set_value("Icon", &icon);
            }
        }

        // 命令存储中的各子项
        for (idx, it) in items.iter().enumerate() {
            let sub = format!(
                r"{}\shell\{:03}_{}",
                m.store_path,
                idx,
                sanitize(&it.id)
            );
            let (k, _) = hkcu
                .create_subkey(&sub)
                .map_err(|e| format!("创建子项失败: {e}"))?;
            k.set_value("MUIVerb", &it.name).map_err(|e| e.to_string())?;
            let icon = it.icon_value();
            if !icon.is_empty() {
                let _ = k.set_value("Icon", &icon);
            }
            let (cmd, _) = hkcu
                .create_subkey(format!(r"{sub}\command"))
                .map_err(|e| e.to_string())?;
            cmd.set_value("", &render_command(it, &config.terminal))
                .map_err(|e| e.to_string())?;
            items_written += 1;
        }
        positions_written += 1;
    }

    Ok(ApplyReport {
        positions_written,
        items_written,
    })
}

/// 注册表子键名只保留安全字符
fn sanitize(s: &str) -> String {
    s.chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || c == '-' || c == '_' {
                c
            } else {
                '_'
            }
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::MenuItem;

    fn sample_item(id: &str, name: &str, kind: &str, exe: &str, tpl: &str) -> MenuItem {
        MenuItem {
            id: id.into(),
            name: name.into(),
            enabled: true,
            kind: kind.into(),
            exe: exe.into(),
            icon: String::new(),
            command_template: tpl.into(),
            order: 0,
            source: "auto".into(),
            positions: vec![],
        }
    }

    /// 端到端验证 apply() 写入的注册表结构与命令字符串，最后 cleanup() 清理干净。
    /// 直接调用真实代码（registry + launcher + config），在 HKCU 中写后即删。
    #[test]
    fn apply_then_cleanup_roundtrip() {
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        // 预清理，避免上次残留干扰
        let _ = cleanup();

        let mut cfg = Config::default();
        cfg.terminal = "wt".into();
        cfg.positions = vec!["background".into()];
        cfg.items = vec![
            sample_item("claude-code", "在 Claude Code 中打开", "cli", r"D:\nodejs\claude.cmd", "{exe}"),
            sample_item("vscode", "在 VS Code 中打开", "gui", r"D:\App\Code.exe", "{exe} \"%V\""),
        ];

        let report = apply(&cfg).expect("apply 应成功");
        assert_eq!(report.positions_written, 1);
        assert_eq!(report.items_written, 2);

        // 父键
        let parent = hkcu
            .open_subkey(r"Software\Classes\Directory\Background\shell\RightMenu.Tools")
            .expect("父键应存在");
        let muiverb: String = parent.get_value("MUIVerb").unwrap();
        assert_eq!(muiverb, "在此打开");
        let ext: String = parent.get_value("ExtendedSubCommandsKey").unwrap();
        assert_eq!(ext, "RightMenu.Store.Background");

        // CLI 子项命令：wt + cmd /k + 引号路径
        let cmd_key = hkcu
            .open_subkey(r"Software\Classes\RightMenu.Store.Background\shell\000_claude-code\command")
            .expect("claude command 键应存在");
        let cmd: String = cmd_key.get_value("").unwrap();
        assert_eq!(cmd, r#"wt.exe -d "%V" cmd /k "D:\nodejs\claude.cmd""#);

        // GUI 子项命令：.exe 直接执行 + %V 作为参数
        let gui_key = hkcu
            .open_subkey(r"Software\Classes\RightMenu.Store.Background\shell\001_vscode\command")
            .expect("vscode command 键应存在");
        let gui_cmd: String = gui_key.get_value("").unwrap();
        assert_eq!(gui_cmd, r#""D:\App\Code.exe" "%V""#);

        // 清理后键应消失
        cleanup().expect("cleanup 应成功");
        assert!(hkcu
            .open_subkey(r"Software\Classes\Directory\Background\shell\RightMenu.Tools")
            .is_err());
        assert!(hkcu
            .open_subkey(r"Software\Classes\RightMenu.Store.Background")
            .is_err());
    }
}
