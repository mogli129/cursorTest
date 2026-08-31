# 检入冲突窗口按钮（SOLIDWORKS 2022 插件）

在对方插件弹出 **「检入文档冲突处理」** 窗口时，于标题栏右上角（关闭按钮左侧）叠加一个自定义按钮。

匹配规则：

- 标题精确为 `检入文档冲突处理`
- 类名以 `WindowsForms10.Window` 开头（完整类名里的 `3c41aa6_r45_ad1` 会变，不能写死）

## 环境

- SOLIDWORKS 2022（64 位）
- Visual Studio 2019/2022，.NET Framework 4.8 开发包
- 管理员权限（COM 注册需要）

## 编译

用 VS 打开 `SwCheckinConflictButtonAddin.sln`，配置选 **Release**，平台为 **x64**（项目已设 `PlatformTarget=x64`），生成。

输出：`SwCheckinConflictButtonAddin\bin\Release\SwCheckinConflictButtonAddin.dll`

## 安装

1. **完全退出 SOLIDWORKS**（任务栏托盘里也不能留）
2. 右键 `SwCheckinConflictButtonAddin\install.bat` → **以管理员身份运行**
3. 脚本会把 DLL 复制到 `%ProgramData%\SwCheckinConflictButtonAddin` 再注册（不要直接从 `H:` 盘注册，映射盘上 COM 经常加载失败）
4. 启动 SW 2022
5. `工具` → `插件`，勾选 **检入冲突窗口按钮**，并勾选左侧「启动时加载」

卸载用 `uninstall.bat`（同样要管理员），然后重启 SW。

### 勾选后重新打开又没勾上

说明 SOLIDWORKS 加载插件失败。按这个顺序查：

1. 确认编译的是 **x64**，不是 AnyCPU/x86
2. **管理员**运行 `install.bat`，看到“注册成功”
3. 完全退出 SW 后再打开
4. 看 `%TEMP%\SwCheckinConflictButtonAddin.log`
   - 没有 `SwAddin ctor`：COM 没创建成功（位数不对、没 /codebase、或从映射盘加载）
   - 有 ctor 没有 `ConnectToSW`：接口 QueryInterface 失败
   - 有 `ConnectToSW exception`：把这段日志发出来

## 改按钮行为

编辑 `SwCheckinConflictButtonAddin/CustomButtonActions.cs` 的 `OnClick`：

```csharp
public static void OnClick(IntPtr targetWindow)
{
    var form = Control.FromHandle(targetWindow) as Form;
    // 在这里写你的逻辑
}
```

改按钮文字、大小：编辑 `AddinOptions.cs`。

## 原理

插件以 `ISwAddin` 加载进 `SLDWORKS.exe`。`SetWinEventHook` 监听本进程窗口显示/改名/移动，400ms 定时器再扫一遍，避免漏钩。找到目标后创建一个 `WS_EX_NOACTIVATE` 的无边框窗体，Owner 设为目标窗口，并按 `DWMWA_CAPTION_BUTTON_BOUNDS` 贴在系统关闭按钮左侧。

## 日志

`%TEMP%\SwCheckinConflictButtonAddin.log`

按钮没出现时先看这个文件里有没有 `ConnectToSW`、`已附加按钮`。

## 注意

- 本机没有 SOLIDWORKS 时无法在此环境验证弹窗；请在 SW2022 上按上面步骤安装后，真正弹出「检入文档冲突处理」看按钮是否出现。
- 若对方窗口是自绘标题栏，系统关闭按钮区域可能为空，此时会退回到窗口右上角估算位置。
- 点击按钮目前只弹出提示框，把业务写进 `CustomButtonActions.OnClick` 即可。
