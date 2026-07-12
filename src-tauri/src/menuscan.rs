//! 扫描并管理系统中「已存在」的右键菜单启动项。
//!
//! 与 `registry.rs`（只管理本工具自建的子菜单）不同，本模块面向注册表里
//! 各个上下文菜单挂载点下所有由任意软件写入的 shell 动词：解析其显示名、
//! 命令、图标、启用状态，并支持启用/禁用（可逆）、删除、新增。

use serde::Serialize;
use winreg::enums::*;
use winreg::RegKey;

/// 一个上下文菜单挂载位置（相对 `Software\Classes`）。
struct Location {
    /// 稳定标识，前端与命令参数使用
    key: &'static str,
    /// 相对 `Software\Classes` 的 shell 容器路径
    shell_rel: &'static str,
}

/// 我们扫描的挂载点：文件夹空白 / 文件夹本身 / 桌面空白 / 磁盘。
/// 这些是开发者「在此打开」类启动项最常出现的位置。
fn locations() -> [Location; 4] {
    [
        Location {
            key: "background",
            shell_rel: r"Directory\Background\shell",
        },
        Location {
            key: "folder",
            shell_rel: r"Directory\shell",
        },
        Location {
            key: "desktop",
            shell_rel: r"DesktopBackground\Shell",
        },
        Location {
            key: "drive",
            shell_rel: r"Drive\shell",
        },
    ]
}

/// 两个 hive：每用户（HKCU，无需提权）与本机（HKLM，需管理员）。
fn hives() -> [(&'static str, isize); 2] {
    [("hkcu", HKEY_CURRENT_USER), ("hklm", HKEY_LOCAL_MACHINE)]
}

/// 扫描到的一个现存右键菜单项。
#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct ExistingEntry {
    /// 所属 hive: "hkcu" | "hklm"
    pub hive: String,
    /// 挂载位置: "background" | "folder" | "desktop" | "drive"
    pub location: String,
    /// shell 动词子键名（用于定位与操作）
    pub key_name: String,
    /// 解析后的显示名（已展开 @dll,-id 间接字符串）
    pub name: String,
    /// 命令字符串（若为子菜单/委托执行则可能为空）
    pub command: String,
    /// 图标值（原始，如 "C:\\foo.exe,0" 或 "@shell32.dll,-5"）
    pub icon: String,
    /// 是否启用（无 LegacyDisable 即启用）
    pub enabled: bool,
    /// 类型: "command"(普通命令) | "submenu"(子菜单) | "delegate"(IExplorerCommand 等)
    pub kind: String,
    /// 是否由本工具（RightMenu）创建
    pub is_ours: bool,
    /// 修改是否需要管理员权限（HKLM 项为 true）
    pub needs_admin: bool,
}

/// 完整拼出某挂载点在指定 hive 下的注册表路径。
fn shell_path(shell_rel: &str) -> String {
    format!(r"Software\Classes\{shell_rel}")
}

/// 解析可能是间接字符串（@file,-id）的显示名；失败时回退到给定 key 名。
fn resolve_display_name(raw: &str, key_name: &str) -> String {
    let trimmed = raw.trim();
    if trimmed.is_empty() {
        return key_name.to_string();
    }
    if let Some(stripped) = trimmed.strip_prefix('@') {
        // 间接字符串：@<模块路径>,-<资源ID>
        let _ = stripped;
        if let Some(resolved) = load_indirect_string(trimmed) {
            let r = resolved.trim().to_string();
            if !r.is_empty() {
                return r;
            }
        }
        // 解析失败：退回 key 名，避免展示原始 @... 串
        return key_name.to_string();
    }
    trimmed.to_string()
}

/// 调用 Win32 SHLoadIndirectString 解析 @module,-id 形式的资源字符串。
#[cfg(windows)]
fn load_indirect_string(source: &str) -> Option<String> {
    use windows::core::{HSTRING, PCWSTR};
    use windows::Win32::UI::Shell::SHLoadIndirectString;

    let src = HSTRING::from(source);
    let mut buf = vec![0u16; 1024];
    unsafe {
        SHLoadIndirectString(PCWSTR(src.as_ptr()), &mut buf, None).ok()?;
    }
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    Some(String::from_utf16_lossy(&buf[..len]))
}

#[cfg(not(windows))]
fn load_indirect_string(_source: &str) -> Option<String> {
    None
}

/// 从单个 shell 动词键读取一个 ExistingEntry。
fn read_entry(
    verb_key: &RegKey,
    hive: &str,
    location: &str,
    key_name: &str,
) -> ExistingEntry {
    // 显示名：优先 MUIVerb，其次默认值，最后 key 名
    let muiverb: String = verb_key.get_value("MUIVerb").unwrap_or_default();
    let default_val: String = verb_key.get_value("").unwrap_or_default();
    let raw_name = if !muiverb.trim().is_empty() {
        muiverb
    } else {
        default_val
    };
    let name = resolve_display_name(&raw_name, key_name);

    let icon: String = verb_key.get_value("Icon").unwrap_or_default();

    // 启用状态：存在 LegacyDisable 值即为禁用
    let disabled = verb_key.get_raw_value("LegacyDisable").is_ok();

    // 命令 / 类型判定
    let has_sub = verb_key
        .get_raw_value("ExtendedSubCommandsKey")
        .is_ok()
        || verb_key.get_raw_value("SubCommands").is_ok();
    let has_delegate = verb_key.get_raw_value("DelegateExecute").is_ok();
    let command: String = verb_key
        .open_subkey("command")
        .ok()
        .and_then(|c| c.get_value::<String, _>("").ok())
        .unwrap_or_default();

    let kind = if !command.is_empty() {
        "command"
    } else if has_sub {
        "submenu"
    } else if has_delegate {
        "delegate"
    } else {
        "command"
    }
    .to_string();

    let is_ours = key_name.starts_with("RightMenu");

    ExistingEntry {
        hive: hive.to_string(),
        location: location.to_string(),
        key_name: key_name.to_string(),
        name,
        command,
        icon,
        enabled: !disabled,
        kind,
        is_ours,
        needs_admin: hive == "hklm",
    }
}

/// Windows 内置/系统类动词（非用户启动项），不予展示。以小写比较。
const SYSTEM_VERBS: &[&str] = &[
    // 通用规范动词
    "open",
    "opennewwindow",
    "opennewprocess",
    "explore",
    "find",
    "properties",
    "runas",
    // 快速访问 / 开始屏幕
    "pintohome",
    "pintostartscreen",
    "pintostart",
    "windows.unpinfromstart",
    // 桌面系统菜单
    "display",
    "personalize",
    "editstickers",
    ".spotlightlearnmore",
    ".spotlightnextimage",
    // 现代分享/复制路径等（DelegateExecute UWP 动词，非启动器）
    "windows.share",
    "windows.modernshare",
    "windows.copyaspath",
];

/// 判断一个 shell 动词是否应出现在列表中（尽量对齐资源管理器的显示逻辑）。
/// 排除：系统规范动词、ProgrammaticAccessOnly（UI 隐藏）、AppliesTo 条件项
/// （如 BitLocker，仅特定目标才显示）、以及没有任何可调用目标的占位项。
fn should_list(verb: &RegKey, key_name: &str) -> bool {
    let lname = key_name.to_lowercase();
    if SYSTEM_VERBS.contains(&lname.as_str()) {
        return false;
    }
    // 仅供程序访问 → 不在 UI 显示
    if verb.get_raw_value("ProgrammaticAccessOnly").is_ok() {
        return false;
    }
    // 条件可见（AppliesTo）→ 通常仅对匹配的目标显示，非常驻启动项
    if verb.get_raw_value("AppliesTo").is_ok() {
        return false;
    }
    // 必须有可调用目标：command 子键 / DelegateExecute / 子菜单
    let has_command = verb.open_subkey("command").is_ok();
    let has_delegate = verb.get_raw_value("DelegateExecute").is_ok();
    let has_sub = verb.get_raw_value("ExtendedSubCommandsKey").is_ok()
        || verb.get_raw_value("SubCommands").is_ok();
    has_command || has_delegate || has_sub
}

/// 扫描所有挂载点、两个 hive 下的现存右键菜单项。
pub fn scan() -> Vec<ExistingEntry> {
    let mut out = Vec::new();
    for (hive_name, hive_id) in hives() {
        let root = RegKey::predef(hive_id);
        for loc in locations() {
            let path = shell_path(loc.shell_rel);
            let Ok(shell) = root.open_subkey(&path) else {
                continue;
            };
            for verb_name in shell.enum_keys().flatten() {
                let Ok(verb) = shell.open_subkey(&verb_name) else {
                    continue;
                };
                if !should_list(&verb, &verb_name) {
                    continue;
                }
                out.push(read_entry(&verb, hive_name, loc.key, &verb_name));
            }
        }
    }
    out
}

/// 由 location key 找到 shell 相对路径。
fn shell_rel_of(location: &str) -> Result<&'static str, String> {
    locations()
        .into_iter()
        .find(|l| l.key == location)
        .map(|l| l.shell_rel)
        .ok_or_else(|| format!("未知挂载位置: {location}"))
}

/// 由 hive 名找到 predef id。
fn hive_id_of(hive: &str) -> Result<isize, String> {
    hives()
        .into_iter()
        .find(|(h, _)| *h == hive)
        .map(|(_, id)| id)
        .ok_or_else(|| format!("未知 hive: {hive}"))
}

/// 校验 key_name 不含路径分隔等危险字符，避免越权操作其它键。
fn ensure_safe_key(key_name: &str) -> Result<(), String> {
    if key_name.is_empty()
        || key_name.contains('\\')
        || key_name.contains('/')
        || key_name.contains("..")
    {
        return Err(format!("非法的菜单项名: {key_name}"));
    }
    Ok(())
}

/// 权限不足时给出清晰的中文提示。
fn map_write_err(hive: &str, e: std::io::Error) -> String {
    if hive == "hklm" {
        format!("修改本机(HKLM)项需要管理员权限，请以管理员身份运行后重试。({e})")
    } else {
        format!("操作失败: {e}")
    }
}

/// 启用/禁用一个现存菜单项（可逆）：
/// 禁用 = 写入空的 `LegacyDisable` 值；启用 = 删除该值。
pub fn toggle(hive: &str, location: &str, key_name: &str, enable: bool) -> Result<(), String> {
    ensure_safe_key(key_name)?;
    let shell_rel = shell_rel_of(location)?;
    let hive_id = hive_id_of(hive)?;
    let root = RegKey::predef(hive_id);
    let path = format!(r"{}\{}", shell_path(shell_rel), key_name);

    let verb = root
        .open_subkey_with_flags(&path, KEY_READ | KEY_SET_VALUE)
        .map_err(|e| map_write_err(hive, e))?;

    if enable {
        // 删除 LegacyDisable（不存在则忽略）
        let _ = verb.delete_value("LegacyDisable");
    } else {
        verb.set_value("LegacyDisable", &String::new())
            .map_err(|e| map_write_err(hive, e))?;
    }
    Ok(())
}

/// 删除一个现存菜单项（删除整个动词子键）。
pub fn delete(hive: &str, location: &str, key_name: &str) -> Result<(), String> {
    ensure_safe_key(key_name)?;
    let shell_rel = shell_rel_of(location)?;
    let hive_id = hive_id_of(hive)?;
    let root = RegKey::predef(hive_id);
    let path = format!(r"{}\{}", shell_path(shell_rel), key_name);

    root.delete_subkey_all(&path)
        .map_err(|e| map_write_err(hive, e))
}

/// 新增一个启动动词。`command` 为最终命令串（如 `"C:\\App\\Code.exe" "%V"`）。
/// 默认写入 HKCU（无需提权）；`hive`="hklm" 时写本机（需管理员）。
pub fn add(
    hive: &str,
    location: &str,
    key_name: &str,
    display_name: &str,
    command: &str,
    icon: &str,
) -> Result<(), String> {
    ensure_safe_key(key_name)?;
    if display_name.trim().is_empty() {
        return Err("显示名不能为空".into());
    }
    if command.trim().is_empty() {
        return Err("命令不能为空".into());
    }
    let shell_rel = shell_rel_of(location)?;
    let hive_id = hive_id_of(hive)?;
    let root = RegKey::predef(hive_id);
    let base = format!(r"{}\{}", shell_path(shell_rel), key_name);

    // 若已存在同名动词，拒绝覆盖
    if root.open_subkey(&base).is_ok() {
        return Err(format!("已存在同名菜单项: {key_name}"));
    }

    let (verb, _) = root
        .create_subkey(&base)
        .map_err(|e| map_write_err(hive, e))?;
    verb.set_value("MUIVerb", &display_name.to_string())
        .map_err(|e| map_write_err(hive, e))?;
    if !icon.trim().is_empty() {
        let _ = verb.set_value("Icon", &icon.to_string());
    }
    let (cmd, _) = root
        .create_subkey(format!(r"{base}\command"))
        .map_err(|e| map_write_err(hive, e))?;
    cmd.set_value("", &command.to_string())
        .map_err(|e| map_write_err(hive, e))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    const TEST_LOC: &str = "background";

    fn cleanup_key(key: &str) {
        let _ = delete("hkcu", TEST_LOC, key);
    }

    #[test]
    fn add_scan_toggle_delete_roundtrip() {
        const TEST_KEY: &str = "RightMenu.MenuScanTest.Roundtrip";
        cleanup_key(TEST_KEY);

        // 新增
        add(
            "hkcu",
            TEST_LOC,
            TEST_KEY,
            "扫描测试项",
            r#""C:\Windows\System32\notepad.exe" "%V""#,
            r"C:\Windows\System32\notepad.exe,0",
        )
        .expect("add 应成功");

        // 扫描应能找到，且初始为启用
        let found = scan()
            .into_iter()
            .find(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == TEST_KEY)
            .expect("scan 应找到新增项");
        assert_eq!(found.name, "扫描测试项");
        assert!(found.command.contains("notepad.exe"));
        assert!(found.enabled, "初始应为启用");
        assert!(found.is_ours, "应识别为本工具创建");
        assert!(!found.needs_admin, "HKCU 项不需管理员");
        assert_eq!(found.kind, "command");

        // 禁用后 enabled=false
        toggle("hkcu", TEST_LOC, TEST_KEY, false).expect("禁用应成功");
        let after_disable = scan()
            .into_iter()
            .find(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == TEST_KEY)
            .expect("仍应存在");
        assert!(!after_disable.enabled, "禁用后应为 false");

        // 再启用
        toggle("hkcu", TEST_LOC, TEST_KEY, true).expect("启用应成功");
        let after_enable = scan()
            .into_iter()
            .find(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == TEST_KEY)
            .expect("仍应存在");
        assert!(after_enable.enabled, "启用后应为 true");

        // 删除后扫描不到
        delete("hkcu", TEST_LOC, TEST_KEY).expect("删除应成功");
        let gone = scan()
            .into_iter()
            .any(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == TEST_KEY);
        assert!(!gone, "删除后不应再被扫描到");
    }

    #[test]
    fn rejects_unsafe_key_names() {
        assert!(ensure_safe_key("").is_err());
        assert!(ensure_safe_key(r"foo\bar").is_err());
        assert!(ensure_safe_key("..").is_err());
        assert!(ensure_safe_key("Valid.Name_123").is_ok());
    }

    #[test]
    fn add_rejects_duplicate() {
        const TEST_KEY: &str = "RightMenu.MenuScanTest.Dup";
        cleanup_key(TEST_KEY);
        add(
            "hkcu",
            TEST_LOC,
            TEST_KEY,
            "dup",
            "cmd.exe",
            "",
        )
        .expect("首次 add 应成功");
        let second = add("hkcu", TEST_LOC, TEST_KEY, "dup", "cmd.exe", "");
        assert!(second.is_err(), "重复 add 应失败");
        cleanup_key(TEST_KEY);
    }

    /// 系统规范动词（denylist）与带 AppliesTo 的条件项不应出现在扫描结果里。
    #[test]
    fn scan_filters_system_and_conditional() {
        const DENY_KEY: &str = "find"; // 命中 SYSTEM_VERBS denylist
        const COND_KEY: &str = "RightMenu.MenuScanTest.Cond"; // 带 AppliesTo
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        let shell_base = shell_path(shell_rel_of(TEST_LOC).unwrap());

        // 清理
        let _ = delete("hkcu", TEST_LOC, DENY_KEY);
        let _ = delete("hkcu", TEST_LOC, COND_KEY);

        // 1) denylist 名称：即便有 command 也应被过滤
        add("hkcu", TEST_LOC, DENY_KEY, "查找", "cmd.exe", "").expect("add find 应成功");

        // 2) 条件项：新增后手动写入 AppliesTo
        add("hkcu", TEST_LOC, COND_KEY, "条件项", "cmd.exe", "").expect("add cond 应成功");
        let cond = hkcu
            .open_subkey_with_flags(format!(r"{shell_base}\{COND_KEY}"), KEY_SET_VALUE)
            .unwrap();
        cond.set_value("AppliesTo", &String::from("System.ItemName:=\"x\""))
            .unwrap();

        let results = scan();
        assert!(
            !results
                .iter()
                .any(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == DENY_KEY),
            "denylist 动词 'find' 不应出现"
        );
        assert!(
            !results
                .iter()
                .any(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == COND_KEY),
            "带 AppliesTo 的条件项不应出现"
        );

        // 清理
        let _ = delete("hkcu", TEST_LOC, DENY_KEY);
        let _ = delete("hkcu", TEST_LOC, COND_KEY);
    }

    /// 没有任何可调用目标（无 command / DelegateExecute / 子菜单）的占位项应被过滤。
    #[test]
    fn scan_filters_targetless_entries() {
        const KEY: &str = "RightMenu.MenuScanTest.NoCmd";
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        let shell_base = shell_path(shell_rel_of(TEST_LOC).unwrap());
        let path = format!(r"{shell_base}\{KEY}");

        let _ = hkcu.delete_subkey_all(&path);
        // 仅创建键并写 MUIVerb，不建 command 子键
        let (k, _) = hkcu.create_subkey(&path).unwrap();
        k.set_value("MUIVerb", &String::from("占位项")).unwrap();

        let results = scan();
        assert!(
            !results
                .iter()
                .any(|e| e.hive == "hkcu" && e.location == TEST_LOC && e.key_name == KEY),
            "无可调用目标的占位项不应出现"
        );

        let _ = hkcu.delete_subkey_all(&path);
    }
}
