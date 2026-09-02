# TeamSpace 依赖清单（升级审阅）

本文列出本插件（`SwCheckinConflictButtonAddin`）所有依赖 **TeamSpace（TS）客户端** 与 **PLM 后端** 的点。TS 升级后按此表核对：变了就要评估是否同步改插件。

对照基线（编写时）：

- TS 客户端反编译：`CNetOp` / `WebPlmMiddle` **1.26.8.803**（`H:\code\ai\TS2024`）
- 后端：`H:\code\gitlabv11\5.1230`
- 冲突窗类型：`HustCAD.CNetOp.FrmDocExist`

本插件 **不引用** TS 任何 DLL（无编译期程序集依赖），全部是运行时：窗口标题、WinForms 控件、反射字段名、HTTP 约定。

---

## 审阅怎么用

1. 拿新旧 TS 的 `CNetOp.dll`、`WebPlmMiddle.dll` 以及冲突窗截图/列名。
2. 按下面「影响等级」从高到低看。
3. **高**：功能会直接失效（找不到窗、拿不到 OID/Token、接口 404）。
4. **中**：部分行失败或操作回写失败。
5. **低**：仅文案/路径展示不准，主流程还能走。

建议每次升级至少做一次：打开「检入文档冲突处理」→ 点本插件按钮 → 进度能走完 → 网格有权限 → 确认后冲突表操作列被改掉。

---

## 1. 进程与加载方式

| 点 | 现状 | 等级 | 升级时看什么 |
| --- | --- | --- | --- |
| 同进程 | 插件作为 SW Add-in 进 `SLDWORKS.exe`，TS 检入插件也必须在同一进程 | 高 | TS 若改成独立进程弹窗，本插件钩不到、也 `FromHandle` 不到 |
| 同 AppDomain WinForms | `Control.FromHandle` 拿到 `FrmDocExist` | 高 | TS 若改 WPF / 跨 AppDomain，注入失败，只剩标题栏 overlay（无表格、无确认回写） |
| 程序集名 | 运行时搜 `HustCAD*`、`CNetOp`、`WebPlmMiddle`、`TeamSpace`、`NetOp` | 中 | 改名后会话反射可能失败，HTTP 调不成 |

代码：`ConflictWindowWatcher.cs`、`HostFormButtonInjector.cs`、`TsSession.cs`。

---

## 2. 发现并注入冲突窗

| 点 | 约定 | 等级 | 代码 |
| --- | --- | --- | --- |
| 窗口标题 | **精确** `检入文档冲突处理` | 高 | `AddinOptions.TargetWindowTitle` |
| WinForms 类名 | 前缀 `WindowsForms10.Window`（后缀会话会变） | 中 | `AddinOptions.WinFormsClassPrefix` |
| 注入位置 | `Form.Controls` 客户区右上角；失败才标题栏 overlay | 中 | `HostFormButtonInjector` |
| 子场景标题 | 子文件夹映射时 TS 标题会改成「文档冲突提示」，**当前不匹配** | 低 | `FrmDocExist_Load` |

升级：改标题、改成非 WinForms、或客户区被 Dock=Fill 面板盖住导致按钮不可见。

---

## 3. 读冲突列表（尽量不绑类型）

不引用 `FrmDocExist.RowData` / `CNetFileInfo`，但约定了 **控件形态**。

| 点 | 约定 | 等级 | 代码 |
| --- | --- | --- | --- |
| 表格类型 | `DataGridView`（按列头/列名打分选一张） | 高 | `ConflictFormReader.FindBestGrid` |
| 表格名（优先特征） | `dgv_clashlist`；列 `DocCode` / `FileName` / `col_op` | 中 | 列名模糊匹配 |
| 编号列 | 表头或 Name 含：编号、文档编号、**代号**、编码、Code、Number、**DocCode** | 中 | 当前表头是「文档代号」 |
| 名称列 | 名称、文档名称、**文件名**、Name、FileName | 中 | 当前「文件名」 |
| 操作列 | 表头含「操作」或 ComboBox/Button 列；当前「冲突处理操作」/`col_op` | 高 | 克隆下拉项、确认时回写 |
| 行数据 OID | `DataGridViewRow.Tag` 上取服务器 CAD OID（见下节） | 高 | `TryReadDocOid` |

升级：改成 DevExpress/Infragistics、列头改名、不再把 `RowData` 放 `Tag`，都会导致读不到行或 OID。

---

## 4. CAD OID（反射字段，无类型引用）

从 `row.Tag` 按名字取，任一层命中即可：

1. Tag 自身：`PlmDocId`、`plmDocId`、`PLMDocId`、`DocId`、`docId`、`objoid`、`ObjOid`、`Oid`
2. 子对象：`SrvFileInfo` / `NetFileInfo` / `FileInfo` / `CadDoc` 上的 `PlmDocId` 等  
   （TS 现状：`FrmDocExist.RowData.SrvFileInfo.PlmDocId`）
3. `Tag.Tag`（常为冲突 `DataRow`）的 `objoid`

| 等级 | 升级时看什么 |
| --- | --- |
| 高 | 改名 `PlmDocId` / 不再挂 `SrvFileInfo` / Tag 不再是 `RowData` |

没有 OID 则不会调 `getCADDocListByOIDS`，该行权限为「未知」。

---

## 5. 操作列与确认回写

| 点 | 约定 | 等级 |
| --- | --- | --- |
| 下拉选项 | 克隆冲突表操作列的 Items（多语言来自 TS） | 高 |
| 默认项匹配 | 模糊匹配「本地+检出/替换服务器」与「服务器替换本地」 | 中 |
| 回写 | **只写操作单元格 `Value`**，不调用 `SetRowOpValue` 等 TS 私有方法。冲突窗点确定时会自己从格子同步到 `RowData.Op` | 中 |

确认按钮才会回写；取消不写。族表是否联动取决于 TS 是否在单元格值变化时处理，插件不再调 `OperateFamilyTable`。

TS 三个操作语义（`RowData.OpType`）：`WithSrv`、`LocalInsteadSrv`、`WithLocal`。插件默认只用前两个。

---

## 6. 从 TS 取 URL / Token（一条路径）

入口：`TsSessionLocator`。调 EPM 需要 **地址 + Token + 用户 OID**（信封字段 `userID`，空则后端报「参数'userid'不能为空」）。

只反射：

1. `NetOpFactory.GetNetOp()`（类型名 `HustCAD.NetOp.NetOpFactory` 或 `HustCAD.CNetOp.NetOpFactory`）
2. 实例上的 `webPLM` → `AddressURL`、`UserTsToken`
3. `UserCookieControl._userInfo.UserOID`（`UserInfo` 属性只有 setter）
4. **`HustCAD.Session.SessionInfo.UserInfo.UserOID`**（登录后真正写入处；不扫全程序集属性）
5. 仍空时尝试从 Token JWT payload 取 oid

**不再**扫描 `OptionConfigs`、全程序集 `HustCAD*` 业务对象。

| 等级 | 升级时看什么 |
| --- | --- |
| 高 | `NetOpFactory.GetNetOp` / `webPLM` / `AddressURL` / Token 改名或不再同进程 |

---

## 7. HTTP：自己封装，但协议跟 TS/后端走

插件 **不调用** `WebPlmMiddleInterface.CommonHttpPostData`，自己 `HttpWebRequest` POST JSON。

### 7.1 公共约定

| 点 | 约定 | 等级 |
| --- | --- | --- |
| EPM 根路径 | `{origin}/teamspace/rest/epm` | 高 |
| EPM 包体 | `{ orderID: 111, clientID: 111, userID, input: { ... } }` | 高 |
| 成功判断 | `result == SUCCESS` 或 `success == true` | 高 |
| Header | `authorization`、`ootb-auth-token` = TS Token；可选 `Cookie`；`Accept-Language: zh-CN` | 高 |
| Content-Type | `application/json` | 高 |

TS 的 `GetInterfaceUrl` 会按 `serviceUrlMap` 改写最后一段 path。插件 **没有** 做这层映射，默认直打 `AddressURL + "/" + 接口名`。若升级后接口只挂在 map 后的新 path 上，这里会 404。

### 7.2 接口一览

| 接口 | URL | input / body | 用途 | 等级 |
| --- | --- | --- | --- | --- |
| 按 OID 查 CAD | `POST {epm}/getCADDocListByOIDS` | `userOid`，`docOIDList: [{ docOid, docOType }]`，每批 40 | 编号、名称、folderId、**folderPath**、容器 | 高 |
| 文件夹全路径（备用） | `POST {epm}/getFolderPathByFolderId` | `folderOid`、`folderOtype`、`userOid` | **仅当 CAD 未返回 folderPath** | 低 |
| 批量权限 | `POST {origin}/rest/v1/webTsRemote/access/checkAccessByObjectId` | **无** EPM 信封：`{ objects: [{ oid, otype }], permissionNames: ["读取_修改", ...] }` | 文档+文件夹读写 | 高 |

权限接口路径来自后端 `ApiPathConstant`：`/rest` + `/v1/` + `webTsRemote/access`。若网关 context 不是「origin 直接接 `/rest`」（例如多了一层 `/inteplm`），要改 `OriginUrl` 拼接。

### 7.3 CAD 响应字段（`CadDocVO`）

解析名：编码用 **`code`（不用 `objnumber`）**；另有 `name`/`docName`，`fileName`/`dlgname`，`docId`/`oid`，`otype`，`folderId`/`folderOid`，`subfolderOtype`，**`folderPath`（优先作为文件夹全路径，不再二次拼接域）**，`containerType`/`containerOtype`，`containerName`，`cabinetOid`/`cabinetOtype`。

### 7.4 权限响应（`AccessBatchDTO`）

- `objectoid`：对象 OID  
- `access`：**没有的**权限，中文，`_` 拼接  
- `isAuthorized`

权限名写死：`读取`、`修改`（`AuthorityConstant`）。改英文或改码值则判断全错。

### 7.5 对象 otype（写死）

| 用途 | 值 |
| --- | --- |
| CAD | `ty.inteplm.cad.CTyCADDoc` |
| 文件夹默认 | `ty.inteplm.folder.CTySubFolder` |
| 文件柜 | `ty.inteplm.folder.CTyCabinet` |
| 产品库容器识别 | `CTyPDMLinkProduct` / 「产品」 |
| 项目库容器识别 | `CTyProject` / 「项目」 |

---

## 8. 明确不依赖的部分

- 不编译引用 `CNetOp.dll` / `WebPlmMiddle.dll`
- 不调用 TS 的 `GetFileByOIDList_Ex`、`SetRowOpValue`、`OptionConfigs`；`SessionInfo` 只读静态 `UserInfo.UserOID`
- 不读 `DataList` 字段名（用表格 `Tag`）
- 不匹配子文件夹冲突窗标题「文档冲突提示」（除非以后要支持）

---

## 9. 升级检查清单

复制此表到发版记录里勾选：

- [ ] 冲突窗标题仍是「检入文档冲突处理」，仍是 WinForms `Form` 且在 `SLDWORKS.exe` 内
- [ ] 仍能 `Control.FromHandle` 拿到 Form，右上角能看到注入按钮
- [ ] 主表仍是 `DataGridView`，有代号/文件名/操作下拉，行 `Tag` 能拿到 `PlmDocId`
- [ ] `NetOpFactory.GetNetOp` + `webPLM.AddressURL` + Token 仍能取到（看日志 `TS 会话`）
- [ ] `getCADDocListByOIDS`、`checkAccessByObjectId` 仍通；无 folderPath 时备用 `getFolderPathByFolderId`
- [ ] 确认后冲突表操作列格子被改掉（不依赖 `SetRowOpValue`）
- [ ] 操作下拉中文/多语言仍能被默认规则匹配到「本地检出替换」和「服务器替换本地」
- [ ] 权限字仍是「读取」「修改」；otype 未改

日志关键字：`冲突窗体类型=`、`首行 OID=`、`WebPlm AddressURL=`、`POST `、`getCADDocListByOIDS`、`checkAccessByObjectId`。

---

## 10. 相关源文件

| 文件 | 职责 |
| --- | --- |
| `AddinOptions.cs` | 窗口标题、按钮文案 |
| `ConflictWindowWatcher.cs` | 按标题/类名发现窗口 |
| `HostFormButtonInjector.cs` | 注入按钮 |
| `ConflictFormReader.cs` | 表格列、OID 反射 |
| `TsSession.cs` | TS 会话反射 |
| `PlmHttpClient.cs` | HTTP 头 |
| `PlmApiClient.cs` | 三个后端接口、otype、权限名、VO 字段 |
| `CadPermissionWindow.xaml.cs` | 操作文案匹配、回写冲突表操作单元格 |
