const invoke = window.__TAURI__.core.invoke;

const qs = (s, r = document) => r.querySelector(s);
const qsa = (s, r = document) => [...r.querySelectorAll(s)];

// 前端状态
let config = null;          // 仅用于持久化默认项：终端 + 默认位置
let terminals = [];         // 可用终端
let entries = [];           // 系统右键菜单扫描结果（唯一列表数据源）
let scanResults = [];       // 最近一次「已装工具」检测结果
let currentLocFilter = "";  // 位置 Tab 当前选中值（"" = 全部）

const LOCATION_LABELS = {
  background: "文件夹空白",
  folder: "文件夹",
  desktop: "桌面",
  drive: "磁盘",
};

// ---------- 工具函数 ----------
function setStatus(msg, kind = "") {
  const el = qs("#status");
  el.textContent = msg;
  el.className = kind;
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"]/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])
  );
}

function defaultTemplate(kind) {
  return kind === "gui" ? '{exe} "%V"' : "{exe}";
}

// 把名称转成安全的注册表键名（字母数字/点/下划线/连字符）
function sanitizeKey(s) {
  const cleaned = String(s)
    .trim()
    .replace(/[^A-Za-z0-9._-]/g, "_")
    .replace(/^_+|_+$/g, "");
  return cleaned || "Item" + Date.now().toString(36);
}

// 从命令串里粗略提取可执行文件路径（用于取图标）
function exeFromCommand(cmd) {
  if (!cmd) return "";
  const m = cmd.match(/"([A-Za-z]:\\[^"]+\.(?:exe|cmd|bat))"/i) || cmd.match(/^"([^"]+)"/) || cmd.match(/^(\S+)/);
  return m ? m[1] : "";
}

async function persist() {
  try {
    await invoke("save_config", { config });
  } catch (e) {
    setStatus("保存设置失败: " + e, "err");
  }
}

// ---------- 图标缓存 ----------
const iconCache = new Map();

async function getIcon(exe) {
  if (!exe) return null;
  if (iconCache.has(exe)) return iconCache.get(exe);
  try {
    const uri = await invoke("get_tool_icon", { exe });
    iconCache.set(exe, uri || null);
    return uri || null;
  } catch {
    iconCache.set(exe, null);
    return null;
  }
}

// ---------- 扫描 / 渲染统一列表 ----------
async function refresh() {
  setStatus("正在扫描右键菜单…");
  try {
    entries = await invoke("scan_existing_menus");
  } catch (e) {
    return setStatus("扫描失败: " + e, "err");
  }
  renderList();
  const count = groupEntries(entries).length;
  setStatus(`共 ${count} 个右键菜单启动项`, "ok");
}

function filteredEntries() {
  const loc = currentLocFilter;
  return entries.filter((e) => {
    if (loc && e.location !== loc) return false;
    return true;
  });
}

// 把过滤后的条目按「同一启动项」（同 hive + 同键名）分组，合并多个挂载位置
function groupEntries(list) {
  const map = new Map();
  for (const e of list) {
    const gkey = e.hive + "\u0000" + e.keyName;
    let g = map.get(gkey);
    if (!g) {
      g = {
        hive: e.hive,
        keyName: e.keyName,
        name: e.name,
        command: e.command,
        icon: e.icon,
        kind: e.kind,
        isOurs: e.isOurs,
        needsAdmin: e.needsAdmin,
        locations: [],
      };
      map.set(gkey, g);
    }
    g.locations.push({ location: e.location, enabled: e.enabled });
    if (!g.command && e.command) g.command = e.command;
  }
  return [...map.values()];
}

function renderList() {
  const list = qs("#item-list");
  const empty = qs("#empty");
  list.innerHTML = "";
  const groups = groupEntries(filteredEntries());
  empty.classList.toggle("hidden", groups.length > 0);
  if (groups.length === 0) {
    empty.textContent =
      entries.length > 0
        ? "没有符合筛选条件的项。"
        : "没有找到右键菜单启动项。点击「扫描已装工具」快速添加，或「新增启动项」手动添加。";
    return;
  }

  const LOC_ORDER = { background: 0, folder: 1, desktop: 2, drive: 3 };
  groups.forEach((g) => {
    const allEnabled = g.locations.every((l) => l.enabled);
    const li = document.createElement("li");
    li.className = "item" + (allEnabled ? "" : " disabled");
    const iconExe = exeFromCommand(g.command);
    const adminBadge = g.needsAdmin
      ? `<span class="badge admin" title="修改需管理员权限">HKLM</span>`
      : "";
    const oursBadge = g.isOurs ? `<span class="badge src">本工具</span>` : "";
    const kindBadge =
      g.kind !== "command"
        ? `<span class="badge k">${g.kind === "submenu" ? "子菜单" : "委托"}</span>`
        : "";
    const locBadges = g.locations
      .slice()
      .sort((a, b) => (LOC_ORDER[a.location] ?? 9) - (LOC_ORDER[b.location] ?? 9))
      .map((l) => {
        const dis = l.enabled ? "" : " off";
        return `<span class="badge loc${dis}" title="${l.enabled ? "已启用" : "已禁用"}">${
          LOCATION_LABELS[l.location] || l.location
        }</span>`;
      })
      .join("");
    li.innerHTML = `
      <div class="tool-icon-wrap"><span class="tool-icon-fallback gui"></span></div>
      <div class="info">
        <div class="name">
          ${escapeHtml(g.name)}
          ${locBadges}${adminBadge}${oursBadge}${kindBadge}
        </div>
        <div class="cmd" title="${escapeHtml(g.command)}">${escapeHtml(g.command || "（无命令 / 子菜单）")}</div>
      </div>
      <div class="ctrls">
        <label class="switch" title="启用 / 禁用">
          <input type="checkbox" ${allEnabled ? "checked" : ""} data-act="toggle">
          <span class="slider"></span>
        </label>
        <button class="btn btn-sm" data-act="del">删除</button>
      </div>`;

    li.querySelector('[data-act="toggle"]').addEventListener("change", (ev) =>
      toggleGroup(g, ev.target)
    );
    li.querySelector('[data-act="del"]').addEventListener("click", () => deleteGroup(g));
    list.appendChild(li);

    if (iconExe) {
      getIcon(iconExe).then((uri) => {
        if (!uri) return;
        const wrap = li.querySelector(".tool-icon-wrap");
        if (wrap) wrap.innerHTML = `<img class="tool-icon" src="${uri}" alt="" />`;
      });
    }
  });
}

// 填充某个终端下拉选择框，并选中当前 config.terminal
function fillTerminalSelect(sel) {
  if (!sel) return;
  sel.innerHTML = terminals
    .map((t) => `<option value="${t}" ${t === config.terminal ? "selected" : ""}>${t}</option>`)
    .join("");
}

// 更新本地 entries 里某 (hive,keyName) 的所有位置的 enabled 状态
function setLocalEnabled(hive, keyName, enable) {
  entries.forEach((x) => {
    if (x.hive === hive && x.keyName === keyName) x.enabled = enable;
  });
}

// ---------- 启用/禁用、删除（按分组，作用于该项的所有位置） ----------
async function toggleGroup(group, checkbox) {
  const enable = checkbox.checked;
  const errs = [];
  for (const l of group.locations) {
    try {
      await invoke("toggle_existing", {
        hive: group.hive,
        location: l.location,
        keyName: group.keyName,
        enable,
      });
      l.enabled = enable;
    } catch (e) {
      errs.push(String(e));
    }
  }
  if (errs.length) {
    checkbox.checked = !enable; // 回滚显示
    setStatus("操作失败: " + errs[0], "err");
  } else {
    setLocalEnabled(group.hive, group.keyName, enable);
    checkbox.closest(".item").classList.toggle("disabled", !enable);
    setStatus(`已${enable ? "启用" : "禁用"}「${group.name}」（${group.locations.length} 个位置）`, "ok");
  }
}

async function deleteGroup(group) {
  const warn = group.needsAdmin
    ? "（本机 HKLM 项，需管理员权限，删除后不可恢复）"
    : "（删除后不可恢复；如只想临时隐藏请改用左侧开关禁用）";
  const locNames = group.locations.map((l) => LOCATION_LABELS[l.location] || l.location).join("、");
  if (!(await confirmDialog(`确定删除「${group.name}」？将从 ${locNames} 移除。${warn}`))) return;
  const errs = [];
  for (const l of group.locations) {
    try {
      await invoke("delete_existing", {
        hive: group.hive,
        location: l.location,
        keyName: group.keyName,
      });
    } catch (e) {
      errs.push(String(e));
    }
  }
  entries = entries.filter((x) => !(x.hive === group.hive && x.keyName === group.keyName));
  renderList();
  if (errs.length) setStatus("部分位置删除失败: " + errs[0], "err");
  else setStatus(`已删除「${group.name}」`, "ok");
}

// ---------- 新增（手动） ----------
function openEdit() {
  qs("#f-name").value = "";
  qs("#f-kind").value = "cli";
  qs("#f-exe").value = "";
  qs("#f-template").value = defaultTemplate("cli");
  qs("#f-icon").value = "";
  fillTerminalSelect(qs("#f-terminal"));
  // 默认位置沿用全局设置
  qsa("#item-positions input").forEach(
    (cb) => (cb.checked = config.positions.includes(cb.value))
  );
  qs("#edit-modal").classList.remove("hidden");
  updatePreview();
}

function readEditForm() {
  const kind = qs("#f-kind").value;
  let tpl = qs("#f-template").value.trim();
  if (!tpl) tpl = defaultTemplate(kind);
  return {
    name: qs("#f-name").value.trim(),
    type: kind,
    exe: qs("#f-exe").value.trim(),
    commandTemplate: tpl,
    icon: qs("#f-icon").value.trim(),
    positions: qsa("#item-positions input:checked").map((cb) => cb.value),
  };
}

// 用后端渲染最终命令串
async function renderCommand(f) {
  const item = { id: "preview", enabled: true, order: 0, source: "custom", ...f };
  return invoke("preview_command", { item, terminal: config.terminal });
}

async function updatePreview() {
  try {
    qs("#f-preview").textContent = (await renderCommand(readEditForm())) || "—";
  } catch {
    qs("#f-preview").textContent = "—";
  }
}

async function saveEdit() {
  const f = readEditForm();
  if (!f.name) return setStatus("请填写显示名称", "err");
  if (!f.exe && f.commandTemplate.includes("{exe}"))
    return setStatus("请填写可执行文件路径", "err");
  if (f.positions.length === 0) return setStatus("请至少选择一个显示位置", "err");

  const command = await renderCommand(f);
  const keyName = "RightMenu." + sanitizeKey(f.name);
  const icon = f.icon || (f.exe ? `${f.exe},0` : "");

  const added = await addToLocations(keyName, f.name, command, icon, f.positions);
  closeModals();
  await refresh();
  if (added > 0) setStatus(`已新增「${f.name}」到 ${added} 个位置`, "ok");
}

// 把同一项写入多个挂载点（HKCU）；返回成功数量，逐条报告失败
async function addToLocations(keyName, name, command, icon, positions) {
  let ok = 0;
  const errs = [];
  for (const location of positions) {
    try {
      await invoke("add_existing", {
        hive: "hkcu",
        location,
        keyName,
        displayName: name,
        command,
        icon,
      });
      ok++;
    } catch (e) {
      errs.push(`${LOCATION_LABELS[location] || location}: ${e}`);
    }
  }
  if (errs.length) setStatus("部分位置未添加 — " + errs.join("；"), "err");
  return ok;
}

// ---------- 扫描已装工具 → 快速添加 ----------
async function openScan() {
  setStatus("正在检测已安装工具…");
  try {
    scanResults = await invoke("scan_tools");
  } catch (e) {
    return setStatus("检测失败: " + e, "err");
  }
  const body = qs("#scan-body");
  if (scanResults.length === 0) {
    body.innerHTML = `<div class="scan-empty">未检测到已知的开发工具。</div>`;
  } else {
    body.innerHTML = scanResults
      .map(
        (t) => `
      <label class="scan-item" data-id="${t.id}">
        <input type="checkbox" value="${t.id}" checked>
        <div class="tool-icon-wrap"><span class="tool-icon-fallback ${t.kind}"></span></div>
        <div class="meta">
          <div class="n">${escapeHtml(t.name)} <span class="badge ${t.kind}">${t.kind.toUpperCase()}</span></div>
          <div class="p" title="${escapeHtml(t.exe)}">${escapeHtml(t.exe)}</div>
        </div>
      </label>`
      )
      .join("");
    scanResults.forEach((t) => {
      getIcon(t.exe).then((uri) => {
        if (!uri) return;
        const wrap = body.querySelector(`.scan-item[data-id="${t.id}"] .tool-icon-wrap`);
        if (wrap) wrap.innerHTML = `<img class="tool-icon" src="${uri}" alt="" />`;
      });
    });
  }
  setStatus(`检测完成，发现 ${scanResults.length} 个工具`);
  fillTerminalSelect(qs("#scan-terminal"));
  qs("#scan-modal").classList.remove("hidden");
}

async function addScanned() {
  const positions = config.positions.length ? config.positions : ["background"];
  const ids = qsa("#scan-body input:checked").map((cb) => cb.value);
  if (ids.length === 0) return setStatus("未选择任何工具", "err");

  let total = 0;
  for (const id of ids) {
    const t = scanResults.find((x) => x.id === id);
    if (!t) continue;
    const f = {
      name: t.name,
      type: t.kind,
      exe: t.exe,
      commandTemplate: t.commandTemplate,
      icon: t.icon,
      positions,
    };
    const command = await renderCommand(f);
    const keyName = "RightMenu." + sanitizeKey(t.id);
    total += await addToLocations(keyName, t.name, command, t.icon || `${t.exe},0`, positions);
  }
  closeModals();
  await refresh();
  if (total > 0) setStatus(`已添加 ${ids.length} 个工具（共 ${total} 个菜单项）`, "ok");
}

// ---------- 重启资源管理器 ----------
async function restartExplorer() {
  if (!(await confirmDialog("将终止并重启 explorer.exe，桌面和任务栏会短暂消失。是否继续？")))
    return;
  setStatus("正在重启资源管理器…");
  try {
    await invoke("restart_explorer");
    setStatus("资源管理器已重启，右键菜单应已刷新", "ok");
  } catch (e) {
    setStatus("重启失败: " + e, "err");
  }
}

// ---------- 通用确认对话框 ----------
function confirmDialog(message) {
  return new Promise((resolve) => {
    qs("#confirm-msg").textContent = message;
    const modal = qs("#confirm-modal");
    const ok = qs("#confirm-ok");
    const cancel = qs("#confirm-cancel");
    modal.classList.remove("hidden");
    const done = (val) => {
      modal.classList.add("hidden");
      ok.removeEventListener("click", onOk);
      cancel.removeEventListener("click", onCancel);
      resolve(val);
    };
    const onOk = () => done(true);
    const onCancel = () => done(false);
    ok.addEventListener("click", onOk);
    cancel.addEventListener("click", onCancel);
  });
}

function closeModals() {
  qs("#scan-modal").classList.add("hidden");
  qs("#edit-modal").classList.add("hidden");
}

// ---------- 文件选择 ----------
async function browseExe() {
  try {
    const path = await invoke("plugin:dialog|open", {
      options: {
        multiple: false,
        directory: false,
        filters: [{ name: "可执行文件", extensions: ["exe", "cmd", "bat", "ps1"] }],
      },
    });
    if (path) {
      qs("#f-exe").value = path;
      updatePreview();
    }
  } catch (e) {
    setStatus("无法打开文件选择器，请手动输入路径", "err");
  }
}

// ---------- 事件绑定 ----------
function bind() {
  qs("#btn-refresh").addEventListener("click", refresh);
  qs("#btn-scan").addEventListener("click", openScan);
  qs("#btn-add").addEventListener("click", openEdit);
  qs("#btn-restart-explorer").addEventListener("click", restartExplorer);
  qs("#scan-add").addEventListener("click", addScanned);
  qs("#edit-save").addEventListener("click", saveEdit);
  qs("#f-browse").addEventListener("click", browseExe);
  qsa("[data-close]").forEach((b) => b.addEventListener("click", closeModals));

  qs("#f-kind").addEventListener("change", (e) => {
    const tpl = qs("#f-template");
    if (tpl.value === "{exe}" || tpl.value === '{exe} "%V"' || !tpl.value)
      tpl.value = defaultTemplate(e.target.value);
    updatePreview();
  });
  ["#f-name", "#f-exe", "#f-template", "#f-icon"].forEach((s) =>
    qs(s).addEventListener("input", updatePreview)
  );

  // 终端选择位于弹层内（新增 / 扫描），改动即持久化
  qs("#f-terminal").addEventListener("change", (e) => {
    config.terminal = e.target.value;
    persist();
    updatePreview();
  });
  qs("#scan-terminal").addEventListener("change", (e) => {
    config.terminal = e.target.value;
    persist();
  });

  qsa("#location-tabs .tab").forEach((btn) =>
    btn.addEventListener("click", () => {
      qsa("#location-tabs .tab").forEach((b) => b.classList.remove("active"));
      btn.classList.add("active");
      currentLocFilter = btn.dataset.loc || "";
      renderList();
    })
  );
}

// ---------- 启动 ----------
async function init() {
  bind();
  try {
    config = await invoke("get_config");
    terminals = await invoke("detect_terminals");
  } catch (e) {
    setStatus("初始化失败: " + e, "err");
    return;
  }
  if (!terminals.includes(config.terminal)) config.terminal = terminals[0] || "cmd";
  if (!config.positions || config.positions.length === 0) config.positions = ["background", "folder"];
  await refresh();
}

window.addEventListener("DOMContentLoaded", init);
