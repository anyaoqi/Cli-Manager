# CliManager Phase 0 真机 POC 结论

> 日期：2026-09-08  
> 环境：Windows 11 25H2 (build 26200.9168)、非管理员 shell、经典菜单已启用（外部 CLSID 键）  
> 结论：**四项假设全部通过，方案无需回滚修订，可进入 Phase 1**。转义规范 §3.3.2 按实测修正两条规则。  
> POC 脚本归档于 `poc/phase0/`（.reg 源文件 + 受控实验脚本），测试项已全部清理。

---

## POC-1 级联渲染（两种写法均验证）✅

| 写法 | 测试项 | 结果 |
|---|---|---|
| `SubCommands=""` + 子级 `shell` 节点 | `CliMgrPOC_A` | ✅ 正确渲染一级级联，子项完整 |
| `ExtendedSubCommandsKey` 指向自身 | `CliMgrPOC_B` | ✅ 正确渲染一级级联（值为 `Directory\Background\shell\CliMgrPOC_B`，经 HKCR 合并视图解析成功） |

**决策**：引擎采用 `SubCommands=""` 写法（更简单、无跨键引用、Diff & Apply 更容易做原子性）；`ExtendedSubCommandsKey` 作为文档备注保留。

## POC-2 免提权写入 ✅

全部 `reg import`（建键/删键/改名，含中文键值）在**非管理员** shell 中一次成功，零 UAC。数量级：30+ 个键值操作。

## POC-3 排序与刷新 ✅（结论优于预期）

1. **排序**：子菜单严格按子键名字典序渲染；`{Order:D3}_{SafeId}` 数字前缀方案有效，且 POC 项（数字开头）整体排在字母开头的非受管项（Cursor、git_gui 等）**上方**，形成连续区块——与方案 §3.2.2 预测一致。
2. **刷新**：`reg import` 后**重新打开菜单即生效**——新增键（050 导入后立即出现）、删键、改名换序（010/030 交换后顺序立即变化）均**无需 `SHChangeNotify`、无需重启 explorer**。静态菜单每次打开时重新枚举注册表。
   - 引擎仍保留一次 `SHChangeNotify(SHCNE_ASSOCCHANGED)` 调用用于**图标缓存**刷新（图标提取/更换场景），菜单结构变更本身无需通知。

## POC-4 启动链模板 ✅（全部真机点击验证）

测试目录：`C:\Users\admin\Desktop\CliMgrPOC 测试 目录`（中文 + 空格）。

| 模板 | 验证点 | 结果 |
|---|---|---|
| `cmd.exe /d /k pushd "%V" & set "HTTP_PROXY=…" & cd & set HTTP_PROXY` | pushd 定位、env 编译注入 | ✅ `cd` 输出正确目录；`HTTP_PROXY=http://127.0.0.1:7899` |
| `wt.exe -d "%V" cmd.exe /d /k "set … & set HTTP_PROXY & cd"` | WT 宿主、`-d` 定位、内层 cmd 承载 env | ✅ WT 窗口打开于正确目录，env 生效 |
| `powershell.exe -NoExit -Command "$env:HTTP_PROXY='…'; Set-Location -LiteralPath '%V'; …"` | PS 宿主、单引号 %V、`$env:` 前缀、-NoExit | ✅ 全部生效 |
| `%` 字面量行为 | §3.3.2 规则 7 取证 | ✅ 见下节规则修正 |

其他观察：菜单启动的 cmd/pwsh 默认由 Windows Terminal 承载（系统"默认终端应用"设置），行为正常。

---

## 关键发现：`%` 在静态动词命令中的完整替换模型（实测）

链路为**两段独立替换**，任何一段都可能"吃掉" `%`：

### 第一段：explorer 动词参数替换（启动前，作用于存储的原始字符串）
- explorer 对存储值做单遍扫描，`%` 后跟**动词参数字符**（`%V`、`%W`、`%L`、`%1`-`%9` 等）即替换：
  - 决定性实验（050 项）：存储 `echo W=%WINDIR% & echo V2=%VX%`，实测输出：
    - `W=C:\Users\admin\Desktop\CliMgrPOC 测试 目录INDIR` → `%WINDIR%` 被拆成 `%W`（替换为当前文件夹）+ `INDIR%`
    - `V2=C:\Users\admin\Desktop\CliMgrPOC 测试 目录X` → `%VX%` 被拆成 `%V`（替换为当前文件夹）+ `X`
  - 即：**`%WINDIR%` 这类引用在静态动词命令中天然损坏**（W 是动词参数字符）。
- 非动词参数字符（如 `%T`、`%P`、`%`后跟 `\`）原样保留（PCT2 实验：`D:\100%\dir\tool.exe` 完整输出）。

### 第二段：cmd 自身的 `%VAR%` 展开（收到命令行后）
- 已定义变量 → 展开为值；未定义 → 原样保留（本地受控实验 D/E）。
- **`%%` 在 cmd 命令行上下文不折叠**（实测输出字面 `%%`）——批处理文件的 `%%` 转义规则在此**不适用**。

### 由此固化的引擎规则（取代原 §3.3.2 规则 7、修正规则 6）
1. 编译器**禁止**把 `%ENV%` 形式的环境变量引用写入 command 字符串；环境变量一律走 `set "K=V" &&` / `$env:K='V';` 前缀（原方案已定，实测确认必要性）。
2. 用户输入（exe 路径/参数）中的**单个字面 `%` 通常安全**，但编译器必须校验：`%` 后不得紧跟动词参数字符（`V W L D H I S M 0-9` 等），且整条编译结果不得出现成对 `%...%` 可折叠为已定义变量的组合；校验不通过 → UI 警告并要求用户改写。
3. 删除原规则 6 中"字面 `%` 按 `%%` 处理"的建议（命令行上下文无效）。
4. Icon 值同样遵守上述校验。

---

## 遗留项（不影响进入 Phase 1）

| 项 | 状态 | 处理 |
|---|---|---|
| UNC 路径（`\\server\share`） | 未做真机菜单验证（本机无可用共享） | `pushd` UNC 行为为 Windows 文档化行为；Phase 4 端到端补验 |
| `%V` 为空（库文件夹等特殊位置） | 未模拟 | Phase 4 用"此电脑/库"场景补验优雅失败 |
| 图标缓存刷新 | 未观察到图标问题 | 引擎保留 SHChangeNotify 调用，Phase 3 更换图标场景验证 |
