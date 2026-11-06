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
        await editorBrowser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new { expression = "window.editorTest.command('compare')", awaitPromise = true, returnByValue = true }));
        var child = (Form)typeof(ModernEditorForm).GetField("_comparison", PrivateInstance).GetValue(editor);
        Assert(child != null && child.Visible, "Comparison did not open as a separate window.");
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
            child.ClientSize = new Size(900, 650);
            await Task.Delay(100);
            Assert(await browser.ExecuteScriptAsync("document.documentElement.scrollWidth <= innerWidth") == "true", "Comparison layout overflows at minimum size.");
            using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "comparison-window.png")))
                await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
            setFrame("borehole");
            await browser.ExecuteScriptAsync("document.getElementById('source').value='borehole';document.getElementById('source').dispatchEvent(new Event('change'));");
            await wait("!document.getElementById('refresh').disabled && document.getElementById('missing').textContent !== '—'");
            Assert(await browser.ExecuteScriptAsync("!document.getElementById('notice').classList.contains('error')") == "true", "Borehole refresh failed.");
            var output = Path.Combine(tempDir, "comparison-after.json");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(original), JsonRows(output)), "Comparison modified station data.");
            Assert(Equals(dirty, typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(editor)), "Comparison marked the document dirty.");
            Console.WriteLine("PASS Comparison window: separate form, native bridge, synthetic frame, timestamp, navigation, 900px layout, borehole, unchanged JSON");
        }
        finally { child.Close(); }
    }
}
