# KyoshinShindoPlaceEditor

简体中文 | [日本語](README.md)

用于编辑强震监测图像取色坐标的 Windows Forms 工具。
此 fork 基于 [ingen084 的原版](https://github.com/ingen084/KyoshinShindoPlaceEditor) 0.0.6.0，加入简体中文界面、新版 JSON 兼容、读取异常处理、官方 CSV 下载、撤销和选点辅助功能。

## 当前版本的改动

- 汉化界面和错误提示，保留数据中的日本观测点名称、地区名及代码。
- 修复监测底图、地表和地下震度图地址中的失效 `/new/` 路径。
- 兼容 `kyoshin-monitor-observation-points` 的 `intensity-points.json`，继续支持原版 PascalCase JSON。
- 读取无效数据失败时保留已有数据；绘制发生异常后恢复窗口操作状态。
- 内嵌 K-NET、KiK-net 官方 CSV 下载，支持 NIED 当前 11 列及旧版 14 列格式。
- 加入 `Ctrl+Z` 撤销，最多保留 50 步。
- 选中点黄色闪烁，并显示原图像素大小为 3×3 的安全框。**仅显示和提醒，不禁止重叠或保存。**

## 启动与基本操作

需要 Windows 和 .NET Framework 4.7 或更高版本。将发行版 DLL 和配置文件保留在 EXE 同目录，运行 `KyoshinShindoPlaceEditor.exe`。

1. 通过“文件 → 打开”加载支持的点位文件。
2. 在右侧列表中选择观测点，再在左上方图像中单击左键，设置该点的图像坐标。
3. 在图像中的已有点位上单击中键（滚轮按钮），可以选中对应的观测点。
4. 右键点击列表项，可以切换停用状态或清除图像坐标。
5. 通过“文件 → 保存”或“另存为”写入文件。**修改不会自动保存。**

按住鼠标右键拖动可平移图像或地图，请注意不要将图像拖出可视区域。
“刷新图像”为手动更新；“监测图像来源”可切换地表与地下震度。

### 两张地图的区别

- **左上方**是 352×400 原图像素坐标系的强震监测图像。这里修改的是图像取色坐标，不是经纬度。支持 1x～5x 显示。
- **左下方**是内嵌的静态地理地图，按选中站点的经纬度显示红色标记，用于核对真实地理位置。这里不能修改经纬度，没有在线瓦片或滚轮缩放；红点可能在可视范围外，可右键拖动查找。

### 闪烁与 3×3 安全框

选中点每 500 毫秒黄色闪烁一次，周围的安全框保持显示。
安全框覆盖原图的 3×3 像素，随显示倍率同比放大。
范围内没有其他点位时为青色；其他点位的实际取色坐标落入范围时为红色，底部状态栏显示数量。停用站点也参与判断。

判断使用实际取色坐标，即新版 JSON 的 `center_point + offset`。
**安全框仅供参考，允许将点移到重叠位置，也允许继续编辑和保存。**

### 撤销

使用“编辑 → 撤销”或 `Ctrl+Z`，最多保留 50 步。
支持移动和清除坐标、切换停用状态、排序、NIED / EqWatch 数据导入；整次导入作为一步撤销。
保存后仍可撤销，但需要再次保存才会写回文件。
成功打开文件会清空历史，打开失败则保留。缩放、选择、拖动视图、下载不计入撤销历史。目前没有重做功能。

## 文件格式

| 格式 | 打开 / 保存 | 说明 |
| --- | --- | --- |
| JSON（`.json`） | 支持 | 新版小写字段与原版 PascalCase 格式 |
| MessagePack（`.mpk`） | 支持 | 原版格式 |
| LZ4 MessagePack（`.mpk.lz4`） | 支持 | 原版格式 |
| 编辑器专用 CSV（`.csv`） | 支持 | 与 NIED CSV 不同 |
| NIED CSV | 专用导入 | 使用“导入 → NIED 观测点信息” |
| ProtocolBuffers（`.pbf`） | 不支持 | 另存为列表中有选项，但保存分支未实现 |
| `.kmop` / `sitepub_all_utf8.csv` | 不支持 | 尚未实现新版格式或 Web 合并 CSV 的直接读取 |

### intensity-points.json

可直接读取小写字段及 `point.center_point`、`point.offset`。
打开新版 JSON 后再保存为 JSON，会继续使用新版结构，保留额外字段、`sub_region` 和未修改经纬度的原始精度。
移动后的偏移量在各轴 -8～7 范围内时保留原中心；超出时使用新中心并将偏移归零。
需要保留这些信息时请使用 JSON，转换为 CSV / MessagePack 不会保留新版专有字段。

建议首次编辑时先“另存为”一个 JSON 副本，再核对内容。

### 编辑器专用 CSV

不含表头，列顺序如下。未设置的图像 X / Y 为空。原版库的 CSV 处理对缺失值和带引号的内容存在限制，日常编辑建议使用 JSON。

```text
网络类型(0=未知,1=KiK-net,2=K-NET),代码,停用(True/False),名称,地区,纬度,经度,图像X,图像Y,区域ID,府县预报区ID,旧纬度,旧经度
```

## NIED 官方 CSV

1. 通过“导入 → NIED 官方下载”分别下载 K-NET 和 KiK-net 文件。
2. 保存到 EXE 同目录，文件名分别为 `sitepub_knet_sj.csv`、`sitepub_kik_sj.csv`（默认名称）。
3. 使用“导入 → NIED 观测点信息”，一起导入两份文件。

来源为 [NIED 官方观测点列表](https://www.kyoshin.bosai.go.jp/ja/stationlist/)。软件通过官网表单获取文件，并非使用固定的 CSV 直链。网络请求在后台执行，验证后保存未经转换的原始 CSV 字节；失败时不替换已有文件。

支持 Shift-JIS 编码的当前 11 列和旧版 14 列文件，按站点代码匹配并更新名称、地理信息及停用状态。已有图像坐标保持不变，传入旧地理坐标缺失时保留已有值。两份文件全部校验后才更新，更新中途失败会恢复原数据。

**官方 CSV 不包含图像取色坐标。** 新增站点需要另行设置图像坐标。请使用专用导入，不要通过普通“文件 → 打开”读取。官网调整接口后，可通过同一菜单打开官方页面下载。

## EqWatch 导入

将 `v5 Kyoshin Monitor Base Data for EqWatch` 格式的 `Kansokuten.dat` 复制到程序的**工作目录**，使用“导入 → EqWatch 的 Kansokuten.dat”。建议以 EXE 所在目录作为工作目录启动。

EqWatch 没有站点代码，导入通过网络类型、名称和地区匹配。未匹配站点会获得 `_EQW编号`，因此应先导入 NIED 数据。存在名称差异（如 `烏海` 与 `鳥海`）时，请核对复制后的文件。已有图像坐标时可以选择补全模式。数据使用须遵循 EqWatch 作者的要求。

## 编译与测试

需要 Windows、PowerShell、Visual Studio / Build Tools（包含 .NET 桌面开发所需的 MSBuild 和 C# 编译器），以及原版 0.0.6.0 发行版 DLL。目标框架仍为 .NET Framework 4.7。

```powershell
git clone --recurse-submodules https://github.com/YASJUN/KyoshinShindoPlaceEditor.git
cd KyoshinShindoPlaceEditor
.\Build-Chinese.ps1 -OriginalReleaseDir 'C:\Tools\KyoshinShindoPlaceEditor.0.0.6.0'
```

请将 `OriginalReleaseDir` 改为原版 DLL 的实际目录。脚本会按需从 NuGet 获取 .NET Framework 4.7 参考程序集，并引用原版 `KyoshinMonitorLib.dll` 编译，不更新依赖 DLL，不自动部署。旧依赖可能触发 `System.Net.Http` 的 `MSB3277` 版本冲突警告。

输出为 `KyoshinShindoPlaceEditor\bin\Release\KyoshinShindoPlaceEditor.exe`。使用时先备份原发行目录，再替换其中的 EXE，保留原 DLL 和 `KyoshinShindoPlaceEditor.exe.config`；不要将整个构建输出覆盖到发行目录。

```powershell
.\tests\Test-Editor.ps1 `
  -ReleaseDir 'C:\Tools\KyoshinShindoPlaceEditor.0.0.6.0' `
  -SamplePath 'C:\Data\intensity-points.json'
# 在上述命令后追加 -Network，可实际验证两份 NIED 官方下载
.\tests\Test-ImageResources.ps1
```

测试需另行准备兼容的真实 JSON 样本。测试使用副本，不改写输入样本。默认回归测试共 12 项，加入 `-Network` 后为 13 项。图像资源测试也需要联网。

主要新增文件：`ObservationPointJsonDocument.cs` 负责 JSON 兼容与校验，`NiedCsvFile.cs` 负责下载和解析，`MainForm.Editing.cs` 负责撤销，`MainForm.Selection.cs` 负责选点显示，`MainForm.Downloads.cs` 负责下载菜单。

## 配置与已知限制

- 编辑既有 `KyoshinShindoPlaceEditor.exe.config` 中的 `AutoLoadFilePath` 可指定启动时读取的文件，支持绝对和相对路径。
- 实时图像不会自动刷新；电脑时间、网络连接及远端服务状态会影响获取结果。
- 原版“退出”菜单没有绑定点击处理，请使用窗口关闭按钮退出。
- 尚未加入经纬度编辑、地图自动居中和重做功能。
- 此 fork 的修改并非原作者发布的更新。请向此 fork 的 Issue 提供输入格式、复现步骤和错误内容。

## 作者、许可与数据

原作者为 **ingen084**。请阅读 [LICENSE.md](LICENSE.md) 及各依赖库的许可。此 fork 不替换原许可，也不将整个项目重新声明为 MIT。

原 README 对源码使用的说明为：原则上禁止引用等使用；确需使用时，可联系原作者申请有限许可。原作者希望使用生成文件时尽量告知，署名为可选；将软件附带在公开应用中分发需要咨询原作者。原再分发说明还要求遵循各依赖库许可并附带 `LICENSE.md`。此处保留这些原版条件，不额外授予许可；日文原文及许可文件为依据。

NIED、EqWatch 数据分别遵循各自使用条件。左下角地图署名为 © OpenStreetMap contributors。原作者声明不对使用软件造成的损害承担责任。
