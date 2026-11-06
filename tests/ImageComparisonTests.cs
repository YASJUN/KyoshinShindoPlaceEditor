using System;
using System.Drawing;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using KyoshinShindoPlaceEditor;
using Microsoft.Web.WebView2.WinForms;

internal static partial class EditorRegressionTests
{
    private static async Task VerifyComparisonWindow(ModernEditorForm editor, WebView2 editorBrowser)
    {
        var serializer = new JavaScriptSerializer();
        Assert(await editorBrowser.ExecuteScriptAsync("document.getElementById('send-comparison').hidden") == "true", "Send button is visible without a comparison window.");
        await editorBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new { expression = "window.editorTest.command('compare')", awaitPromise = true, returnByValue = true }));
        var child = (Form)typeof(ModernEditorForm).GetField("_comparison", PrivateInstance).GetValue(editor);
        Assert(child != null && child.Visible, "Comparison did not open as a separate window.");
        Assert(child.Owner == null && !child.TopMost, "Comparison still floats above its owner.");
        try
        {
            var browser = (WebView2)child.GetType().GetField("_browser", PrivateInstance).GetValue(child);
            Func<string, Task> wait = async condition => {
                for (var i = 0; i < 200; i++)
                {
                    if (browser.CoreWebView2 != null && await browser.ExecuteScriptAsync(condition) == "true") return;
                    await Task.Delay(25);
                }
                throw new Exception("Comparison condition timed out: " + condition);
            };
            await wait("!!document.getElementById('refresh') && !document.getElementById('refresh').disabled && document.getElementById('stations').textContent !== '—'");
            var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(editor);
            var original = Path.Combine(tempDir, "comparison-before.json");
            Invoke(model, "ModernSave", original);
            var dirty = typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(editor);
            string encoded;
            using (var bitmap = new Bitmap(352, 400))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(Brushes.Blue, 19, 19, 3, 3);
                graphics.FillRectangle(Brushes.Red, 79, 99, 3, 3);
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                encoded = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
            }
            var frameType = typeof(ModernEditorForm).Assembly.GetType("KyoshinShindoPlaceEditor.MonitorFrame");
            Action<string> setFrame = source => {
                var frame = Activator.CreateInstance(frameType);
                frameType.GetProperty("image").SetValue(frame, encoded);
                frameType.GetProperty("source").SetValue(frame, source);
                frameType.GetProperty("stamp").SetValue(frame, "20261106104951");
                var task = typeof(Task).GetMethod("FromResult").MakeGenericMethod(frameType).Invoke(null, new[] { frame });
                var field = child.GetType().GetField("_download", PrivateInstance);
                var lambda = Expression.Lambda(field.FieldType, Expression.Constant(task, task.GetType()), Expression.Parameter(typeof(string), "source"));
                field.SetValue(child, lambda.Compile());
            };
            setFrame("surface");
            await browser.ExecuteScriptAsync("document.getElementById('refresh').click()");
            await wait("!document.getElementById('refresh').disabled && document.getElementById('missing').textContent !== '—'");
            Assert(await browser.ExecuteScriptAsync("document.getElementById('stamp').textContent.includes('2026-11-06')") == "true", "Comparison timestamp missing.");
            await browser.ExecuteScriptAsync("document.getElementById('added-list').querySelector('button').click()");
            Assert(await browser.ExecuteScriptAsync("document.getElementById('zoom').value === '5'") == "true", "Difference navigation did not zoom.");
            await browser.ExecuteScriptAsync("document.getElementById('reset').click()");
            await VerifyComparisonNavigation(editor, editorBrowser, browser, wait);
            child.ClientSize = new Size(900, 650);
            await Task.Delay(100);
            Assert(await browser.ExecuteScriptAsync("document.documentElement.scrollWidth <= innerWidth") == "true", "Comparison layout overflows at minimum size.");
            using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "comparison-window.png")))
                await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
            setFrame("borehole");
            await browser.ExecuteScriptAsync("document.getElementById('source').value='borehole';document.getElementById('source').dispatchEvent(new Event('change'));");
            await wait("!document.getElementById('refresh').disabled && document.getElementById('missing').textContent !== '—'");
            Assert(await browser.ExecuteScriptAsync("!document.getElementById('notice').classList.contains('error')") == "true", "Borehole refresh failed.");
            await VerifyEditorToComparison(editor, editorBrowser, child, browser, wait);
            await VerifySpatialSearch(editorBrowser, browser, wait);
            var output = Path.Combine(tempDir, "comparison-after.json");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(original), JsonRows(output)), "Comparison modified station data.");
            Assert(Equals(dirty, typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(editor)), "Comparison marked the document dirty.");
            typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(editor, new object[] { original });
            await browser.ExecuteScriptAsync("document.getElementById('send-main').click()");
            await wait("!document.getElementById('send-main').disabled");
            Assert(await browser.ExecuteScriptAsync("document.getElementById('notice').textContent.includes('其他文件')") == "true", "Stale comparison navigated a different document.");
            await browser.ExecuteScriptAsync("document.getElementById('snapshot').click()");
            await wait("!document.getElementById('snapshot').disabled");
            await browser.ExecuteScriptAsync("document.getElementById('send-main').click()");
            await wait("!document.getElementById('send-main').disabled");
            Assert(await browser.ExecuteScriptAsync("!document.getElementById('notice').classList.contains('error')") == "true", "Resynced comparison cannot navigate.");
            Console.WriteLine("PASS Comparison window: separate form, native bridge, synthetic frame, timestamp, navigation, 900px layout, borehole, unchanged JSON");
        }
        finally { child.Close(); }
        await Task.Delay(50);
        Assert(await editorBrowser.ExecuteScriptAsync("document.getElementById('send-comparison').hidden") == "true", "Send button remains visible after comparison closes.");
    }

    private static async Task VerifyEditorToComparison(ModernEditorForm editor, WebView2 main, Form child, WebView2 comparison, Func<string, Task> wait)
    {
        var json = new JavaScriptSerializer();
        var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(editor);
        Func<string, Task> evaluateMain = async expression => {
            var result = await main.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                json.Serialize(new { expression, awaitPromise = true, returnByValue = true }));
            Assert(!Object(json.DeserializeObject(result)).ContainsKey("exceptionDetails"), "Main-to-comparison command failed: " + result);
        };
        await evaluateMain("window.editorTest.select('OKN006');window.editorTest.setZoom(4)");
        Assert(await main.ExecuteScriptAsync("!document.getElementById('send-comparison').hidden && !document.getElementById('send-comparison').disabled") == "true", "Send button is unavailable for a known station.");
        child.WindowState = FormWindowState.Minimized;
        await evaluateMain("document.getElementById('send-comparison').click()");
        await wait("document.getElementById('point-title').textContent.includes('OKN006')");
        await Task.Delay(60);
        Assert(child.WindowState != FormWindowState.Minimized && child.Owner == null && !child.TopMost, "Send failed to restore an independent comparison window.");
        Assert(await comparison.ExecuteScriptAsync("document.getElementById('source').value==='borehole' && document.getElementById('zoom').value==='4' && document.getElementById('point-info').textContent.includes('图像坐标')") == "true", "Send changed source or lost zoom/details.");
        Assert(await comparison.ExecuteScriptAsync("['known','latest','difference'].every(id=>{const c=document.getElementById(id),m=c.getContext('2d').getTransform(),p=m.transformPoint({x:86.5,y:140.5});return Math.abs(p.x-c.width/2)<1&&Math.abs(p.y-c.height/2)<1;})") == "true", "Known coordinates were not centered on all three maps.");
        var beforeCount = await comparison.ExecuteScriptAsync("document.getElementById('stations').textContent");
        await evaluateMain("window.editorTest.select('GIF001');document.getElementById('send-comparison').click()");
        await wait("document.getElementById('point-title').textContent.includes('GIF001') && document.getElementById('point-info').textContent.includes('估算中心')");
        Assert(await comparison.ExecuteScriptAsync("document.getElementById('stations').textContent") == beforeCount, "Estimate entered the comparison mask.");
        await evaluateMain("window.editorTest.command('fillDrafts',{candidates:[{code:'GIF001',x:170,y:246}]})");
        try
        {
            await evaluateMain("document.getElementById('send-comparison').click()");
            await wait("document.getElementById('point-info').textContent.includes('待核对草稿') && document.getElementById('point-info').textContent.includes('170, 246')");
            Assert(await comparison.ExecuteScriptAsync("document.getElementById('stations').textContent") == beforeCount, "Draft entered the comparison mask.");
        }
        finally
        {
            Invoke(model, "ModernUndo");
            await evaluateMain("window.editorTest.command('ready')");
        }
        Console.WriteLine("PASS Editor to comparison: button lifecycle, known/draft/estimated coordinates, 4x, all canvases, source preservation, restore without topmost, no mask mutation");
    }

    private static async Task VerifySpatialSearch(WebView2 main, WebView2 comparison, Func<string, Task> wait)
    {
        var json = new JavaScriptSerializer();
        await main.ExecuteScriptAsync("window.editorTest.select('OKN006');window.editorTest.setZoom(4);document.getElementById('dismiss').click()");
        await Task.Delay(60);
        Func<string, double, double, Task> mouse = async (type, x, y) => {
            await main.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                json.Serialize(new { type, x, y, button = "right", buttons = type == "mouseReleased" ? 0 : 2, clickCount = 1 }));
        };
        foreach (var canvas in new[] { "monitor", "geographic" })
        {
            var position = Object(json.DeserializeObject(await main.ExecuteScriptAsync("(()=>{const r=document.getElementById('" + canvas + "').getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})()")));
            var x = Convert.ToDouble(position["x"]); var y = Convert.ToDouble(position["y"]);
            await mouse("mousePressed", x, y); await mouse("mouseMoved", x + 20, y + 10); await mouse("mouseReleased", x + 20, y + 10);
            Assert(await main.ExecuteScriptAsync("document.getElementById('map-context-menu').hidden") == "true", "Right drag opened search menu: " + canvas);
            // Return to the station, then allow a tiny hand movement without treating it as a drag.
            await main.ExecuteScriptAsync("window.editorTest.select('OKN006')");
            await Task.Delay(40);
            await mouse("mousePressed", x, y); await mouse("mouseMoved", x + 1, y + 1); await mouse("mouseReleased", x + 1, y + 1);
            Assert(await main.ExecuteScriptAsync("!document.getElementById('map-context-menu').hidden") == "true", "Right click did not open search menu: " + canvas);
            await main.ExecuteScriptAsync("document.querySelector('[data-station-search=nearby]').click();document.getElementById('dismiss').click()");
            Assert(await main.ExecuteScriptAsync("window.editorTest.state.filteredCodes.includes('OKN006') && !document.getElementById('spatial-filter').hidden && window.editorTest.state.spatialQuery.space==='" + (canvas == "monitor" ? "image" : "map") + "'") == "true", "Nearby search did not include clicked station: " + canvas);
            await main.ExecuteScriptAsync("document.getElementById('spatial-radius').value='0.1';document.getElementById('spatial-radius').dispatchEvent(new Event('change'))");
            Assert(await main.ExecuteScriptAsync("window.editorTest.state.spatialQuery.radius===0.1") == "true", "Radius adjustment failed.");
            await mouse("mousePressed", x, y); await mouse("mouseReleased", x, y);
            await main.ExecuteScriptAsync("document.querySelector('[data-station-search=nearest]').click();document.getElementById('dismiss').click()");
            Assert(await main.ExecuteScriptAsync("window.editorTest.state.filteredCodes.length>0 && document.getElementById('only-unmarked').checked && document.getElementById('station-status').value==='active' && [...document.querySelectorAll('.station-row')].every(r=>r.children[2].textContent==='否' && r.children[6].textContent==='未设置')") == "true", "Nearest search did not filter unmarked active candidates: " + canvas);
            await main.ExecuteScriptAsync("document.getElementById('search').value='NO_MATCH';document.getElementById('search').dispatchEvent(new Event('input'))");
            Assert(await main.ExecuteScriptAsync("window.editorTest.state.filteredCodes.length===0") == "true", "Text filter not combined with spatial search.");
            await main.ExecuteScriptAsync("document.getElementById('clear-spatial').click()");
            Assert(await main.ExecuteScriptAsync("window.editorTest.state.filteredCodes.length===window.editorTest.state.count && document.getElementById('spatial-filter').hidden") == "true", "Clear filters did not restore list.");
        }
        await main.ExecuteScriptAsync("document.getElementById('send-comparison').click()");
        await wait("document.getElementById('point-title').textContent.includes('OKN006')");
        await comparison.ExecuteScriptAsync("document.getElementById('send-search').click();document.getElementById('send-nearest').click()");
        await wait("!document.getElementById('send-nearest').disabled");
        await Task.Delay(60);
        Assert(await main.ExecuteScriptAsync("(()=>{const s=window.editorTest.state;return s.spatialQuery.mode==='nearest' && s.spatialQuery.x===86.5 && s.spatialQuery.y===140.5 && s.filteredCodes.length>0;})()") == "true", "Comparison selected station search failed.");
        // Plain station navigation must still reveal the selected marked station after nearest filtering.
        await comparison.ExecuteScriptAsync("document.getElementById('send-main').click()");
        await wait("!document.getElementById('send-main').disabled");
        Assert(await main.ExecuteScriptAsync("!window.editorTest.state.spatialQuery && window.editorTest.state.filteredCodes.includes('OKN006')") == "true", "Plain navigation did not clear hiding spatial filter.");
        await comparison.ExecuteScriptAsync("document.getElementById('reset').click();document.getElementById('send-search').click();document.getElementById('send-nearby').click()");
        await wait("!document.getElementById('send-nearby').disabled");
        Assert(await main.ExecuteScriptAsync("(()=>{const s=window.editorTest.state;return s.spatialQuery.mode==='nearby' && s.spatialQuery.x===176 && s.spatialQuery.y===200 && s.selected==='OKN006';})()") == "true", "Comparison current-view search changed selection or center.");
        Console.WriteLine("PASS Spatial search: native right click vs drag on both canvases, radius, filters/reset, selected/view comparison search, plain navigation");
    }

    private static async Task VerifyComparisonNavigation(ModernEditorForm editor, WebView2 main, WebView2 comparison, Func<string, Task> wait)
    {
        var json = new JavaScriptSerializer();
        // Click a sparse Okinawa station at its rendered pixel center, using actual pointer input.
        var row = Object(Array.Find(JsonRows(samplePath), value => (string)Object(value)["code"] == "OKN006"));
        var point = Object(row["point"]);
        var x = Convert.ToInt32(Object(point["center_point"])["x"]) + Convert.ToInt32(Object(point["offset"])["x"]);
        var y = Convert.ToInt32(Object(point["center_point"])["y"]) + Convert.ToInt32(Object(point["offset"])["y"]);
        Func<string, double, double, Task> mouse = async (type, sx, sy) => {
            await comparison.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                json.Serialize(new { type, x = sx, y = sy, button = "left", buttons = type == "mouseReleased" ? 0 : 1, clickCount = 1 }));
        };
        var pos = Object(json.DeserializeObject(await comparison.ExecuteScriptAsync(
            "(()=>{const c=document.getElementById('known');c.scrollIntoView({block:'center'});const r=c.getBoundingClientRect(),p=c.getContext('2d').getTransform().transformPoint({x:" + (x + .5).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",y:" + (y + .5).ToString(System.Globalization.CultureInfo.InvariantCulture) + "});return {x:r.left+p.x/devicePixelRatio,y:r.top+p.y/devicePixelRatio};})()")));
        var px = Convert.ToDouble(pos["x"]); var py = Convert.ToDouble(pos["y"]);
        await mouse("mousePressed", px, py); await mouse("mouseReleased", px, py);
        await wait("document.getElementById('point-title').textContent.includes('OKN006')");
        Assert(await comparison.ExecuteScriptAsync("document.getElementById('point-info').textContent.includes('经纬度') && document.getElementById('point-info').textContent.includes('图像坐标')") == "true", "Station details missing.");
        await main.ExecuteScriptAsync("document.getElementById('search').value='NO_MATCH';document.getElementById('search').dispatchEvent(new Event('input'));document.getElementById('sync').checked=false;");
        await comparison.ExecuteScriptAsync("document.getElementById('send-main').click()");
        await wait("!document.getElementById('send-main').disabled");
        Assert(await main.ExecuteScriptAsync("window.editorTest.state.selected === 'OKN006' && !!document.querySelector('[data-code=OKN006]')") == "true", "Selected station was not revealed in the main window.");
        var centered = "(()=>{const c=document.getElementById('monitor'),m=c.getContext('2d').getTransform(),p=m.transformPoint({x:" + (x + .5).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",y:" + (y + .5).ToString(System.Globalization.CultureInfo.InvariantCulture) + "});return Math.abs(p.x-c.width/2)<1 && Math.abs(p.y-c.height/2)<1;})()";
        await Task.Delay(60);
        Assert(await main.ExecuteScriptAsync(centered) == "true", "Station transfer did not center the image with auto-link disabled.");
        await comparison.ExecuteScriptAsync("document.getElementById('clear-selection').click();document.getElementById('zoom').value='3';document.getElementById('zoom').dispatchEvent(new Event('change'));document.getElementById('known').scrollIntoView({block:'center'});");
        pos = Object(json.DeserializeObject(await comparison.ExecuteScriptAsync("(()=>{const r=document.getElementById('known').getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})()")));
        px=Convert.ToDouble(pos["x"]); py=Convert.ToDouble(pos["y"]);
        await mouse("mousePressed",px,py); await mouse("mouseMoved",px+30,py+21); await mouse("mouseReleased",px+30,py+21);
        Assert(await comparison.ExecuteScriptAsync("document.getElementById('point-title').textContent === '未选择点位'") == "true", "Dragging accidentally selected a station.");
        // Original view center (176,200), translated by (30,21) screen pixels at 3x.
        await main.ExecuteScriptAsync("document.getElementById('sync').checked=true;");
        editor.WindowState = FormWindowState.Minimized;
        await comparison.ExecuteScriptAsync("document.getElementById('send-main').click()");
        await wait("!document.getElementById('send-main').disabled");
        await Task.Delay(60);
        Assert(editor.WindowState != FormWindowState.Minimized, "Navigation did not restore the main window.");
        Assert(await main.ExecuteScriptAsync("(()=>{const c=document.getElementById('monitor'),m=c.getContext('2d').getTransform(),p=m.transformPoint({x:166,y:193});return Math.abs(p.x-c.width/2)<1&&Math.abs(p.y-c.height/2)<1&&document.getElementById('zoom-value').textContent==='3×'&&window.editorTest.state.selected==='OKN006';})()") == "true", "View transfer changed selection or lost center/zoom.");
        await main.ExecuteScriptAsync("document.getElementById('sync').checked=true;");
        await comparison.ExecuteScriptAsync("document.getElementById('reset').click();window.scrollTo(0,0);");
        Console.WriteLine("PASS Comparison navigation: native point picking/details, hidden station reveal, no-link location, drag vs click, center/zoom transfer");
    }
}
