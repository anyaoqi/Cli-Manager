const invoke = window.__TAURI__.core.invoke;

const qs = (s, r = document) => r.querySelector(s);
const qsa = (s, r = document) => [...r.querySelectorAll(s)];

let config = null;          // 当前配置（前端唯一状态）
let terminals = [];         // 可用终端
let editingId = null;       // 正在编辑的项 id（null＝新增）
let scanResults = [];       // 最近一次扫描结果

// ---------- 工具函数 ----------
function setStatus(msg, kind = "") {
  const el = qs("#status");
  el.textContent = msg;
  el.className = kind;
}

function defaultTemplate(kind) {
  return kind === "gui" ? '{exe} "%V"' : "{exe}";
}

async function persist() {
  try {
    await invoke("save_config", { config });
  } catch (e) {
    setStatus("保存失败: " + e, "err");
  }
}

// ---------- 图标缓存 ----------
const iconCache = new Map(); // exe 路径 -> data URI | null

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

// ---------- 渲染 ----------
async function renderItems() {
  const list = qs("#item-list");
  const empty = qs("#empty");
  list.innerHTML = "";

  const items = [...config.items].sort((a, b) => a.order - b.order);
  empty.classList.toggle("hidden", items.length > 0);

  // 批量取最终命令预览
  const previews = await Promise.all(
    items.map((it) =>
      invoke("preview_command", { item: it, terminal: config.terminal }).catch(() => "")
    )
  );
  // 批量取工具图标（带缓存，避免重复提取）
  const icons = await Promise.all(items.map((it) => getIcon(it.exe)));

  items.forEach((it, idx) => {
    const li = document.createElement("li");
    li.className = "item" + (it.enabled ? "" : " disabled");
    const iconHtml = icons[idx]
      ? `<img class="tool-icon" src="${icons[idx]}" alt="" />`
      : `<span class="tool-icon-fallback ${it.type}"></span>`;
    li.innerHTML = `
      <label class="switch">
        <input type="checkbox" ${it.enabled ? "checked" : ""} data-act="toggle">
        <span class="slider"></span>
      </label>
      <div class="tool-icon-wrap">${iconHtml}</div>
      <div class="info">
        <div class="name">
          ${escapeHtml(it.name)}
          <span class="badge ${it.type}">${it.type.toUpperCase()}</span>
          <span class="badge src">${it.source === "auto" ? "自动" : "自定义"}</span>
        </div>
        <div class="cmd" title="${escapeHtml(previews[idx])}">${escapeHtml(previews[idx] || it.commandTemplate)}</div>
      </div>
      <div class="ctrls">
        <button class="btn btn-sm" data-act="up" ${idx === 0 ? "disabled" : ""}>↑</button>
        <button class="btn btn-sm" data-act="down" ${idx === items.length - 1 ? "disabled" : ""}>↓</button>
        <button class="btn btn-sm" data-act="edit">编辑</button>
        <button class="btn btn-sm" data-act="del">删除</button>
      </div>`;
    li.querySelector('[data-act="toggle"]').addEventListener("change", (e) => {
      it.enabled = e.target.checked;
      persist();
      li.classList.toggle("disabled", !it.enabled);
    });
    li.querySelector('[data-act="up"]').addEventListener("click", () => moveItem(it.id, -1));
    li.querySelector('[data-act="down"]').addEventListener("click", () => moveItem(it.id, 1));
    li.querySelector('[data-act="edit"]').addEventListener("click", () => openEdit(it));
    li.querySelector('[data-act="del"]').addEventListener("click", () => deleteItem(it.id));
    list.appendChild(li);
  });
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"]/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])
  );
}

function renderSettings() {
  qs("#parent-label").value = config.parentLabel;
  const sel = qs("#terminal");
  sel.innerHTML = terminals
    .map((t) => `<option value="${t}" ${t === config.terminal ? "selected" : ""}>${t}</option>`)
    .join("");
  qsa("#global-positions input").forEach((cb) => {
    cb.checked = config.positions.includes(cb.value);
  });
}

// ---------- 列表操作 ----------
function reindex() {
  [...config.items]
    .sort((a, b) => a.order - b.order)
    .forEach((it, i) => (it.order = i));
}

async function moveItem(id, dir) {
  const sorted = [...config.items].sort((a, b) => a.order - b.order);
  const i = sorted.findIndex((x) => x.id === id);
  const j = i + dir;
  if (j < 0 || j >= sorted.length) return;
  [sorted[i].order, sorted[j].order] = [sorted[j].order, sorted[i].order];
  await persist();
  renderItems();
}

async function deleteItem(id) {
  const item = config.items.find((x) => x.id === id);
  const name = item ? item.name : "该项";
  if (!(await confirmDialog(`确定删除「${name}」？`))) return;
  config.items = config.items.filter((x) => x.id !== id);
  reindex();
  await persist();
  renderItems();
}

// ---------- 编辑/新增弹层 ----------
function openEdit(item) {
  editingId = item ? item.id : null;
  qs("#edit-title").textContent = item ? "编辑菜单项" : "添加自定义菜单";
  qs("#f-name").value = item ? item.name : "";
  qs("#f-kind").value = item ? item.type : "cli";
  qs("#f-exe").value = item ? item.exe : "";
  qs("#f-template").value = item ? item.commandTemplate : defaultTemplate("cli");
  qs("#f-icon").value = item ? item.icon : "";
  const pos = item ? item.positions : [];
  qsa("#item-positions input").forEach((cb) => (cb.checked = pos.includes(cb.value)));
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

async function updatePreview() {
  const f = readEditForm();
  const item = {
    id: "preview",
    enabled: true,
    order: 0,
    source: "custom",
    ...f,
  };
  try {
    qs("#f-preview").textContent =
      (await invoke("preview_command", { item, terminal: config.terminal })) || "—";
  } catch {
    qs("#f-preview").textContent = "—";
  }
}

async function saveEdit() {
  const f = readEditForm();
  if (!f.name) return setStatus("请填写显示名称", "err");
  if (!f.exe && f.commandTemplate.includes("{exe}"))
    return setStatus("请填写可执行文件路径", "err");

  if (editingId) {
    const it = config.items.find((x) => x.id === editingId);
    Object.assign(it, f);
  } else {
    const maxOrder = config.items.reduce((m, x) => Math.max(m, x.order), -1);
    config.items.push({
      id: "custom-" + Date.now().toString(36),
      enabled: true,
      order: maxOrder + 1,
      source: "custom",
      ...f,
    });
  }
  await persist();
  closeModals();
  renderItems();
}

// ---------- 扫描弹层 ----------
async function openScan() {
  setStatus("正在扫描…");
  try {
    scanResults = await invoke("scan_tools");
  } catch (e) {
    return setStatus("扫描失败: " + e, "err");
  }
  const existing = new Set(config.items.map((x) => x.id));
  const fresh = scanResults.filter((t) => !existing.has(t.id));
  const body = qs("#scan-body");

  if (fresh.length === 0) {
    body.innerHTML = `<div class="scan-empty">未发现新的工具${
      scanResults.length ? "（检测到的均已添加）" : ""
    }。</div>`;
  } else {
    body.innerHTML = fresh
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
    // 异步加载真实图标（不阻塞弹层显示）
    fresh.forEach((t) => {
      getIcon(t.exe).then((uri) => {
        if (!uri) return;
        const wrap = body.querySelector(`.scan-item[data-id="${t.id}"] .tool-icon-wrap`);
        if (wrap) wrap.innerHTML = `<img class="tool-icon" src="${uri}" alt="" />`;
      });
    });
  }
  setStatus(`扫描完成，发现 ${scanResults.length} 个工具`);
  qs("#scan-modal").classList.remove("hidden");
}

async function addScanned() {
  const ids = qsa("#scan-body input:checked").map((cb) => cb.value);
  let order = config.items.reduce((m, x) => Math.max(m, x.order), -1);
  ids.forEach((id) => {
    const t = scanResults.find((x) => x.id === id);
    if (!t) return;
    config.items.push({
      id: t.id,
      name: t.name,
      enabled: true,
      type: t.kind,
      exe: t.exe,
      icon: t.icon,
      commandTemplate: t.commandTemplate,
      order: ++order,
      source: "auto",
      positions: [],
    });
  });
  await persist();
  closeModals();
  renderItems();
  setStatus(`已添加 ${ids.length} 个菜单项`, "ok");
}

// ---------- 应用 / 移除 ----------
async function apply() {
  setStatus("正在写入注册表…");
  try {
    const r = await invoke("apply_to_registry", { config });
    setStatus(`已应用：${r.positionsWritten} 个位置，${r.itemsWritten} 个菜单项`, "ok");
  } catch (e) {
    setStatus("应用失败: " + e, "err");
  }
}

async function removeAll() {
  try {
    await invoke("remove_all");
    setStatus("已移除全部右键菜单（本地配置保留）", "ok");
  } catch (e) {
    setStatus("移除失败: " + e, "err");
  }
}

// ---------- 导入 / 导出配置 ----------
async function exportConfig() {
  try {
    const path = await invoke("plugin:dialog|save", {
      options: {
        defaultPath: "rightmenu-config.json",
        filters: [{ name: "JSON", extensions: ["json"] }],
      },
    });
    if (!path) return;
    await invoke("export_config", { path });
    setStatus("配置已导出到 " + path, "ok");
  } catch (e) {
    setStatus("导出失败: " + e, "err");
  }
}

async function importConfig() {
  try {
    const path = await invoke("plugin:dialog|open", {
      options: {
        multiple: false,
        directory: false,
        filters: [{ name: "JSON", extensions: ["json"] }],
      },
    });
    if (!path) return;
    config = await invoke("import_config", { path });
    if (!terminals.includes(config.terminal)) {
      config.terminal = terminals[0] || "cmd";
      await persist();
    }
    renderSettings();
    await renderItems();
    setStatus("配置已导入", "ok");
  } catch (e) {
    setStatus("导入失败: " + e, "err");
  }
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

// ---------- 通用确认对话框（不依赖原生 confirm） ----------
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
  qs("#btn-scan").addEventListener("click", openScan);
  qs("#btn-add").addEventListener("click", () => openEdit(null));
  qs("#btn-apply").addEventListener("click", apply);
  qs("#btn-remove").addEventListener("click", removeAll);
  qs("#btn-import").addEventListener("click", importConfig);
  qs("#btn-export").addEventListener("click", exportConfig);
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

  qs("#parent-label").addEventListener("change", (e) => {
    config.parentLabel = e.target.value.trim() || "在此打开";
    persist();
  });
  qs("#terminal").addEventListener("change", (e) => {
    config.terminal = e.target.value;
    persist();
    renderItems();
  });
  qsa("#global-positions input").forEach((cb) =>
    cb.addEventListener("change", () => {
      config.positions = qsa("#global-positions input:checked").map((c) => c.value);
      persist();
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
  renderSettings();
  await renderItems();
  setStatus("就绪");
}

window.addEventListener("DOMContentLoaded", init);
