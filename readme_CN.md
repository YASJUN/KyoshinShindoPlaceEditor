# KyoshinShindoPlaceEditor

简体中文 | [日本語](README.md)

用于编辑强震监测图像取色坐标的 Windows 工具。**0.0.7.0 默认启动 WebView2 + Canvas 界面**，经典 Windows Forms 界面保留为兼容入口。
此 fork 基于 [ingen084 的原版](https://github.com/ingen084/KyoshinShindoPlaceEditor) 0.0.6.0，加入简体中文界面、新版 JSON 兼容、读取异常处理、官方 CSV 下载、撤销和选点辅助功能。

## 0.0.8.0 更新

- 新增“NIED 增量更新”：打开现有点位文件后，一次下载 K-NET 和 KiK-net 最新数据，按代码更新官方信息并追加新站点。
- 清除图像坐标前增加确认提示，默认选择“否”；工具栏、右键菜单及经典界面均适用，取消不会修改文档。

## 当前版本的改动

- 用 Canvas 重写地图绘制，用虚拟滚动列表替换逐行创建的原生站点列表。现代界面不会创建原生站点行，单点编辑只回传该站点的变化。
- 底图随程序附带；实时监测图像在后台获取并设定超时，加载失败不阻止选点、拖动和保存。
- 汉化界面和错误提示，保留数据中的日本观测点名称、地区名及代码。
- 修复监测底图、地表和地下震度图地址中的失效 `/new/` 路径。
- 兼容 `kyoshin-monitor-observation-points` 的 `intensity-points.json`，继续支持原版 PascalCase JSON。
- 读取无效数据失败时保留已有数据；绘制发生异常后恢复窗口操作状态。
- 内嵌 K-NET、KiK-net 官方 CSV 下载，支持 NIED 当前 11 列及旧版 14 列格式。
- 加入 `Ctrl+Z` 撤销，最多保留 50 步。
- 支持按站点代码合并已有坐标文件，保留当前 NIED 信息并补入图像坐标。
- 修复单点编辑重建整个列表的卡顿；选点时可联动定位上下地图，也可点击 OSM 站点来选择。
- 选中点黄色闪烁，并显示原图像素大小为 3×3 的安全框。**仅显示和提醒，不禁止重叠或保存。**

## 启动与基本操作

现代界面需要 Windows 10/11、.NET Framework 4.7 或更高版本及 Microsoft Edge WebView2 Runtime。保留发行目录内的 `Modern`、`runtimes`、WebView2 DLL、原版 DLL 和配置文件，运行 `KyoshinShindoPlaceEditor.exe`。

1. 点击工具栏“打开”加载点位文件，也可用 `Ctrl+O`。
2. 在右侧列表中选择观测点，再在左上方图像中单击左键，设置该点的图像坐标。
3. 在图像中的已有点位上单击中键（滚轮按钮），可以选中对应的观测点。
4. 右键点击列表项，可以切换停用状态或清除图像坐标。工具栏下方也有“清除图像坐标”按钮；清除前会显示站点代码、名称和当前坐标，确认后只清除图像坐标，保留站点记录和经纬度，可用 `Ctrl+Z` 撤销。
5. 点击“保存”或“另存为”，快捷键为 `Ctrl+S` / `Ctrl+Shift+S`。**修改不会自动保存**；关闭或打开其他文件时会提示未保存的修改。

按住鼠标右键拖动可平移图像或地图，请注意不要将图像拖出可视区域。
“刷新图像”为手动更新；图像上方的下拉框可切换地表与地下震度。右侧搜索框可筛选代码、名称和地区，列表支持方向键、Home / End。

### 经典界面

需要旧菜单、EqWatch 导入或排查兼容性时，可运行：

```powershell
.\KyoshinShindoPlaceEditor.exe --legacy
```

经典界面继续使用“文件 / 导入 / 视图”菜单；下文保留的菜单路径指此界面。现代界面直接使用工具栏同名按钮。经典界面的原生列表仍可能存在重绘卡顿，通常请使用默认现代界面。

### 两张地图的区别

- **左上方**是 352×400 原图像素坐标系的强震监测图像。这里修改的是图像取色坐标，不是经纬度。支持 1x～5x 显示。
- **左下方**是内嵌的静态 OSM 地理地图，按站点经纬度显示标记，用于核对真实地理位置。绿色表示已设置图像坐标，蓝色表示尚未设置，灰色表示停用，选中点用红圈标识。左键或中键点击站点附近可选中最近站点。现代界面支持滚轮缩放，范围为 0.5×～4×；没有在线瓦片，也不能修改经纬度。

现代界面的“选点联动定位”（经典界面为“视图 → 选点时同步定位上下地图”）默认开启。通过列表、上图中键或 OSM 点选站点时，两张图自动定位到该站；`Ctrl+L` 或“定位当前站点”可手动定位。关闭联动后仍可分别右键拖动视图。
只有经纬度、没有图像坐标的站点只能在下图定位，不会自动推算或写入上图坐标。两张图采用不同坐标系，拖动距离不作机械同步。

### 闪烁与 3×3 安全框

选中点每 500 毫秒黄色闪烁一次，周围的安全框保持显示。
安全框覆盖原图的 3×3 像素，随显示倍率同比放大。
范围内没有其他点位时为青色；其他点位的实际取色坐标落入范围时为红色，底部状态栏显示数量。停用站点也参与判断。

判断使用实际取色坐标，即新版 JSON 的 `center_point + offset`。
**安全框仅供参考，允许将点移到重叠位置，也允许继续编辑和保存。**

### 撤销

点击“撤销”、使用 `Ctrl+Z`，或在经典界面使用“编辑 → 撤销”，最多保留 50 步。
支持移动和清除坐标、切换停用状态、排序、站点文件合并、NIED / EqWatch 数据导入；整次导入或合并作为一步撤销。
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
| ProtocolBuffers（`.pbf`） | 不支持 | 仅经典界面残留未实现的选项 |
| `.kmop` / `sitepub_all_utf8.csv` | 不支持 | 尚未实现新版格式或 Web 合并 CSV 的直接读取 |

### intensity-points.json

可直接读取小写字段及 `point.center_point`、`point.offset`。
打开新版 JSON 后再保存为 JSON，会继续使用新版结构，保留额外字段、`sub_region` 和未修改经纬度的原始精度。
移动后的偏移量在各轴 -8～7 范围内时保留原中心；超出时使用新中心并将偏移归零。
需要保留这些信息时请使用 JSON，转换为 CSV / MessagePack 不会保留新版专有字段。

建议首次编辑时先“另存为”一个 JSON 副本，再核对内容。

### 将已有坐标文件合并到 NIED 数据

普通“文件 → 打开”仍会替换当前数据；需要混合两个来源时，请使用专用合并入口：

1. 点击“导入 NIED CSV”，选择官方原始文件，可同时选择 K-NET 和 KiK-net。
2. 点击“合并坐标文件”，打开已有图像坐标的 `intensity-points.json` 或受支持的编辑器 JSON / CSV / MessagePack 文件。
3. 按站点代码合并后，使用“另存为”保存为 JSON。

相同代码保留当前名称、地区、经纬度和停用状态，并用传入的非空图像坐标补充或更新；空坐标不会清除已有坐标。未匹配站点追加到当前数据。同代码网络类型冲突、数据校验失败或合并 CSV 有读取失败行时，拒绝整次合并并保留原数据。

现代 JSON 的中心、偏移和附加字段会一并保留；已有附加字段优先保留当前值，图像坐标结构跟随传入的非空坐标。整次合并可撤销，合并本身不会将保存目标改成导入文件。

### 编辑器专用 CSV

不含表头，列顺序如下。未设置的图像 X / Y 为空。原版库的 CSV 处理对缺失值和带引号的内容存在限制，日常编辑建议使用 JSON。

```text
网络类型(0=未知,1=KiK-net,2=K-NET),代码,停用(True/False),名称,地区,纬度,经度,图像X,图像Y,区域ID,府县预报区ID,旧纬度,旧经度
```

## NIED 官方 CSV

### 在线增量更新现有 JSON

1. 点击“打开”载入现有的 `intensity-points.json` 或其他兼容点位文件。
2. 点击工具栏“NIED 增量更新”。程序在后台下载并校验 K-NET 和 KiK-net 两份最新官方 CSV，无需先手动保存文件。
3. 完成后查看新增、更新、未变化和官方列表外保留的站点数量，再点击“保存”或“另存为”。

相同代码更新官方名称、地区、经纬度和停用状态；已有图像坐标、JSON 中心和偏移及其他附加字段保持不变。新代码追加为图像坐标未设置的站点。官方列表里未出现的原站点继续保留，不自动删除或推断停用。

两份文件全部下载、校验完成后才更新。任一下载失败、数据无效或同代码网络类型冲突，整次更新失败，原文档和历史保持不变。下载期间可选点、搜索和查看地图，修改与文件操作暂时禁用，完成或失败后恢复。整次更新为一步撤销；相同数据重复更新不新增历史，也不会自动写入打开的文件。

### 下载和手动导入

1. 通过工具栏“NIED 下载”分别下载 K-NET 和 KiK-net 原始文件。
2. 点击“导入 NIED CSV”，选择一份或同时多选两份文件。现代界面不要求固定文件名或目录。
3. 如使用经典界面，则需将两份文件保存到 EXE 同目录，分别命名为 `sitepub_knet_sj.csv`、`sitepub_kik_sj.csv`，再使用“导入 → NIED 观测点信息”。

来源为 [NIED 官方观测点列表](https://www.kyoshin.bosai.go.jp/ja/stationlist/)。软件通过官网表单获取文件，并非使用固定的 CSV 直链。网络请求在后台执行，验证后保存未经转换的原始 CSV 字节；失败时不替换已有文件。

支持 Shift-JIS 编码的当前 11 列和旧版 14 列文件，按站点代码匹配并更新名称、地理信息及停用状态。已有图像坐标保持不变，传入旧地理坐标缺失时保留已有值。两份文件全部校验后才更新，更新中途失败会恢复原数据。

**官方 CSV 不包含图像取色坐标。** 新增站点需要另行设置图像坐标。请使用专用导入，不要通过普通“文件 → 打开”读取。官网调整接口后，可通过同一菜单打开官方页面下载。

## EqWatch 导入

此入口目前保留在 `--legacy` 经典界面。

将 `v5 Kyoshin Monitor Base Data for EqWatch` 格式的 `Kansokuten.dat` 复制到程序的**工作目录**，使用“导入 → EqWatch 的 Kansokuten.dat”。建议以 EXE 所在目录作为工作目录启动。

EqWatch 没有站点代码，导入通过网络类型、名称和地区匹配。未匹配站点会获得 `_EQW编号`，因此应先导入 NIED 数据。存在名称差异（如 `烏海` 与 `鳥海`）时，请核对复制后的文件。已有图像坐标时可以选择补全模式。数据使用须遵循 EqWatch 作者的要求。

## 编译与测试

需要 Windows、PowerShell、Visual Studio / Build Tools（包含 .NET 桌面开发所需的 MSBuild 和 C# 编译器），以及原版 0.0.6.0 发行版 DLL。目标框架仍为 .NET Framework 4.7。

```powershell
git clone --recurse-submodules https://github.com/YASJUN/KyoshinShindoPlaceEditor.git
cd KyoshinShindoPlaceEditor
.\Build-Chinese.ps1 -OriginalReleaseDir 'C:\Tools\KyoshinShindoPlaceEditor.0.0.6.0'
```

请将 `OriginalReleaseDir` 改为原版 DLL 的实际目录。脚本会按需从 NuGet 获取 .NET Framework 4.7 参考程序集及固定版本 `Microsoft.Web.WebView2 1.0.4314.35`，并引用原版 `KyoshinMonitorLib.dll` 编译。不升级原版数据处理 DLL，不自动部署。旧依赖可能触发 `System.Net.Http` 的 `MSB3277` 版本冲突警告。

输出目录为 `KyoshinShindoPlaceEditor\bin\Release`。先备份发行目录，再复制新 EXE、`Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、`Modern`、`runtimes`、`WebView2-LICENSE.txt` 和 `WebView2-NOTICE.txt`；保留原版数据处理 DLL、CSV、JSON 和配置文件。仅替换 EXE 不足以运行 WebView2 界面。

```powershell
.\tests\Test-Editor.ps1 `
  -ReleaseDir 'C:\Tools\KyoshinShindoPlaceEditor.0.0.6.0' `
  -SamplePath 'C:\Data\intensity-points.json'
# 在上述命令后追加 -Network，可实际验证两份 NIED 官方下载
.\tests\Test-Editor.ps1 -VisibleOnly
.\tests\Test-ImageResources.ps1
```

测试需准备包含 1,749 个站点的兼容真实 JSON 样本，并安装 WebView2 Runtime。测试使用副本，不改写输入样本。默认回归测试共 19 项，加入 `-Network` 后为 20 项。`-VisibleOnly` 单独运行窗口场景，包含 100 次选点、缩放、34 次真实编辑、联网更新桥接、清除确认及取消、撤销和 JSON 元数据检查，并生成 `tests\bin\modern-editor.png`。可用 `-NiedUpdateOnly` 或 `-ClearOnly` 单独验证对应逻辑。`-Network` 会实际下载官方数据并验证增量更新；图像资源测试也需要联网。

性能验证包含实际显示的窗口和消息循环，而非仅测导入函数。现代模式不创建原生站点行，前端通常只保留约 30～40 行；单点编辑和单步撤销通过局部消息更新。具体测量与未采用的方案见 [PERF.md](PERF.md)，耗时会随硬件与环境变化。

`ModernEditorForm*.cs` 负责 WebView2 宿主、受限消息接口和后台图像下载；`Modern/` 是本地界面；`MainForm.ModernModel.cs` 将既有 JSON、CSV、合并和撤销逻辑提供给新界面。经典界面及其单元回归测试继续保留。

## 配置与已知限制

- 编辑既有 `KyoshinShindoPlaceEditor.exe.config` 中的 `AutoLoadFilePath` 可指定启动时读取的文件，支持绝对和相对路径。
- 实时图像不会自动刷新；电脑时间、网络连接及远端服务状态会影响获取结果。
- 经典界面的原版“退出”菜单没有绑定点击处理，请使用窗口关闭按钮退出。
- 尚未加入经纬度编辑和重做功能；地图为内嵌静态图片，覆盖范围不随选点扩展。
- 此 fork 的修改并非原作者发布的更新。请向此 fork 的 Issue 提供输入格式、复现步骤和错误内容。

## 作者、许可与数据

原作者为 **ingen084**。请阅读 [LICENSE.md](LICENSE.md) 及各依赖库的许可。此 fork 不替换原许可，也不将整个项目重新声明为 MIT。

原 README 对源码使用的说明为：原则上禁止引用等使用；确需使用时，可联系原作者申请有限许可。原作者希望使用生成文件时尽量告知，署名为可选；将软件附带在公开应用中分发需要咨询原作者。原再分发说明还要求遵循各依赖库许可并附带 `LICENSE.md`。此处保留这些原版条件，不额外授予许可；日文原文及许可文件为依据。

NIED、EqWatch 数据分别遵循各自使用条件。左下角地图署名为 © OpenStreetMap contributors。WebView2 SDK 及 Runtime 分别遵循 Microsoft 的许可；打包 SDK 文件时请同时保留其许可文件。原作者声明不对使用软件造成的损害承担责任。
