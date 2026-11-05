using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using KyoshinShindoPlaceEditor;
using Microsoft.Web.WebView2.WinForms;

internal static partial class EditorRegressionTests
{
    private static async Task VerifyImageEstimates(ModernEditorForm form, WebView2 browser)
    {
        using (var map = new Bitmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modern", "base-map.gif")))
        {
            Assert(map.Width == 352 && map.Height == 400, "Unexpected fixed base-map dimensions.");
            foreach (var pixel in new[] { new Point(172, 60), new Point(172, 120), new Point(20, 208), new Point(141, 172) })
            {
                var color = map.GetPixel(pixel.X, pixel.Y);
                Assert(color.R < 120 && color.G < 120 && color.B < 120, "Inset divider no longer matches the fixed projection.");
            }
        }
        var fixtures = new List<object>();
        var original = Object(JsonRows(samplePath)[0]);
        var locations = new[] {
            new[] { 35.6812, 139.7671 }, new[] { 26.2124, 127.6809 }, new[] { 29.9, 130 },
            new[] { 43.0, 135 }, new[] { 26.0, 133 }, new[] { 35.6812, 139.7671 }, new[] { 29.8999, 130 }
        };
        var codes = new[] { "ESTTOKYO", "ESTNAHA", "ESTOUT", "ESTMAIN", "ESTINSET", "ESTKNOWN", "ESTEDGE" };
        for (var index = 0; index < codes.Length; index++)
        {
            var row = new Dictionary<string, object>(original) {
                ["code"] = codes[index], ["name"] = codes[index],
                ["location"] = new Dictionary<string, object> { ["latitude"] = locations[index][0], ["longitude"] = locations[index][1] },
                ["point"] = index == 5 ? new Dictionary<string, object> {
                    ["center_point"] = new Dictionary<string, object> { ["x"] = 200, ["y"] = 200 },
                    ["offset"] = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0 }
                } : null
            };
            fixtures.Add(row);
        }
        var serializer = new JavaScriptSerializer();
        var input = Path.Combine(tempDir, "image-estimates.json");
        File.WriteAllText(input, serializer.Serialize(fixtures));
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { input });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.ExecuteScriptAsync("window.editorTest.state.count === 7") == "true") break;
            await Task.Delay(20);
        }
        var evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new { expression = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageMarkerScenario.js")),
                awaitPromise = true, returnByValue = true }));
        Assert(!Object(serializer.DeserializeObject(evaluation)).ContainsKey("exceptionDetails"), "Image estimate scenario failed: " + evaluation);
        var position = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync(
            "(()=>{const r=document.getElementById('monitor').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()")));
        var x = Convert.ToDouble(position["x"]);
        var y = Convert.ToDouble(position["y"]);
        foreach (var step in new[] { "mousePressed", "mouseMoved", "mouseReleased" })
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                serializer.Serialize(new { type = step, x = step == "mousePressed" ? x : x + 37,
                    y = step == "mousePressed" ? y : y + 23, button = "right", buttons = step == "mouseReleased" ? 0 : 2, clickCount = 1 }));
        evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new { expression = "window.verifyImageMarkerPan(37,23)", awaitPromise = true, returnByValue = true }));
        Assert(!Object(serializer.DeserializeObject(evaluation)).ContainsKey("exceptionDetails"), "Panned marker is misplaced: " + evaluation);
        var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(form);
        var output = Path.Combine(tempDir, "image-estimates-unchanged.json");
        Invoke(model, "ModernSave", output);
        Assert(Equivalent(JsonRows(input), JsonRows(output)), "Displaying estimates wrote coordinates or changed station metadata.");
        Assert(!(bool)typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(form), "Displaying estimates marked the document dirty.");
        using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image-estimate.png")))
            await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { samplePath });
        await Task.Delay(50);
        Console.WriteLine("PASS Estimated markers: mainland/inset, 1x/3x/5x, panning, invalid regions, no safety square, unchanged JSON");
    }
}
