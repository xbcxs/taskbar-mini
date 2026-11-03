# TaskbarMini

Windows 11 x64 任务栏小工具，仅显示：

```text
CPU:12% 52°C
M:48% D:3%
```

原生子窗口嵌入主屏任务栏，在通知区域左侧显示，两行文字左对齐，外侧右间距为零。使用等宽字体和独立固定数值栏位，各栏位预留三位整数宽度并右对齐，标签与数值之间不额外留白，两组数据之间留一个空格，默认字号 11 DIP。数字位数变化时窗口、后续字段和单位位置保持固定。指标每秒刷新，仅在显示内容或布局变化时重绘，完整画面缓冲后一次显示。字体和颜色跟随任务栏 DPI/系统深浅色。空间紧张时适度缩小字体；仍不足时自动隐藏，腾出空间后恢复。没有独立悬浮窗、网速、图表、历史、插件管理或额外托盘图标。

## 使用

解压整个 `TaskbarMini-win-x64` 文件夹到固定位置，双击 `TaskbarMini.exe`。无需单独安装 .NET。

- 普通启动会请求管理员权限，用于 CPU 温度与最高权限登录任务；取消授权后仍可显示其余指标。
- 首次正常启动会启用当前用户的登录自动启动。右键小工具仅有“开机启动”和“退出”。
- 管理员模式使用当前用户的登录计划任务，避免每次登录弹 UAC；普通模式使用当前用户 Run 启动项，以普通权限运行。
- 退出只关闭当前实例；若要下次登录不再启动，先取消“开机启动”。
- 移动程序文件夹后，以管理员权限启动，在右键菜单取消再勾选“开机启动”，更新路径。删除程序前也先关闭“开机启动”。
- 重复启动会提示已有实例；任务栏无空余区域时，关闭/合并部分任务按钮或减少常驻托盘图标即可恢复。

## 指标含义

- CPU：优先 Windows Processor Utility；不可用时回退 Processor Time / GetSystemTimes。不同采样时刻与回退口径可能导致和任务管理器的数字不同。
- 内存：物理内存占用比例。
- 磁盘：最忙物理磁盘的活动率，采用 `100 - % Idle Time`，不是磁盘空间占用。
- 温度：CPU Package 优先，其次 AMD Tctl/Tdie，最后取最热核心。无效、无权限、驱动不可用或采样超时均显示 `--`，不使用主板温度代替 CPU 温度。

温度依赖 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 的传感器支持。可能需要管理员权限及其支持的 PawnIO 驱动；本工具不自动安装驱动或更改 Windows 安全设置。普通权限实机验证未读取到 CPU 温度，管理员与驱动环境下的温度仍需验证。

## 开发

需要 x64 .NET SDK 10.0.402 或同版本系列的更新补丁。

```powershell
dotnet test tests/TaskbarMini.Tests/TaskbarMini.Tests.csproj
dotnet publish src/TaskbarMini.App/TaskbarMini.App.csproj -c Release -r win-x64 --self-contained true -o ../.ai/dist/TaskbarMini-win-x64
```

Windows 实机验证（临时嵌入、采样、截图后退出，不注册启动项、不弹 UAC）：

```powershell
& ../.ai/dist/TaskbarMini-win-x64/TaskbarMini.exe --verify ../.ai/verification/taskbar-mini/manual 20
```

退出码：0 成功，1 启动/退出异常，2 实机验收未全部满足，3 已有实例。以进程退出码和 `verification.json` 同时判断；截图必须包含真实数字。验证主屏任务栏句柄、WS_CHILD、图标边界、实时数据、渲染像素、可控宿主重建/缩放/空间不足恢复和启动项未变化。实际 Explorer 重启、锁屏/重新登录、自动隐藏、全屏及其他显示器未自动操作。

临时产物、NuGet 缓存、构建输出统一在上级工程根 `.ai/`，不进入版本库。

## 依赖

仅使用 Windows/.NET API 和 LibreHardwareMonitorLib（MPL-2.0）。第三方说明见 `THIRD-PARTY-NOTICES.md`。任务栏嵌入方法参考 TrafficMonitor，自主实现窗口与界面，没有移植其源文件和其他功能。
