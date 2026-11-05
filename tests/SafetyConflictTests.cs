using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using KyoshinShindoPlaceEditor;
using Microsoft.Web.WebView2.WinForms;

internal static partial class EditorRegressionTests
{
    private static async Task VerifySafetyConflictColors(ModernEditorForm form, WebView2 browser)
    {
        var original = Object(JsonRows(samplePath)[0]);
        var fixtures = new List<object>();
        Action<string, int?, int?> add = (code, x, y) => fixtures.Add(new Dictionary<string, object>(original) {
            ["code"] = code, ["name"] = "安全区测试站 " + code,
            ["location"] = new Dictionary<string, object> { ["latitude"] = 35.6812, ["longitude"] = 139.7671 },
            ["point"] = x.HasValue ? new Dictionary<string, object> {
                ["center_point"] = new Dictionary<string, object> { ["x"] = x.Value, ["y"] = y.Value },
                ["offset"] = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0 }
            } : null
        });
        add("ROOT", 200, 200);
        add("A", 200, 200);
        add("B", 201, 200);
        add("C", 199, 199);
        add("OUTSIDE", 202, 200);
        add("EMPTY", 50, 350);
        add("UNSET", null, null);
        for (var index = 0; index < 11; index++) add("DUP" + index.ToString("00"), 199, 201);
        for (var index = 0; index < 60; index++) add("FAR" + index.ToString("00"), 20 + index, 300);
        var serializer = new JavaScriptSerializer();
        var input = Path.Combine(tempDir, "safety-conflicts.json");
        File.WriteAllText(input, serializer.Serialize(fixtures));
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { input });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.ExecuteScriptAsync("window.editorTest.state.count === 78") == "true") break;
            await Task.Delay(20);
        }
        var evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new {
                expression = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SafetyConflictScenario.js")),
                awaitPromise = true, returnByValue = true
            }));
        Assert(!Object(serializer.DeserializeObject(evaluation)).ContainsKey("exceptionDetails"), "Safety conflict scenario failed: " + evaluation);
        var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(form);
        Assert(!(bool)typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(form),
            "Selection or filtering the conflict legend marked the document dirty.");
        using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "safety-conflicts.png")))
            await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        var originalSize = form.ClientSize;
        form.ClientSize = new System.Drawing.Size(900, 650);
        evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new {
                expression = "window.verifySafetyConflictLayout()", awaitPromise = true, returnByValue = true
            }));
        Assert(!Object(serializer.DeserializeObject(evaluation)).ContainsKey("exceptionDetails"), "Narrow conflict status bar overflowed: " + evaluation);
        using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "safety-conflicts-narrow.png")))
            await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        form.ClientSize = originalSize;
        evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new {
                expression = "window.verifySafetyConflictEdits()", awaitPromise = true, returnByValue = true
            }));
        Assert(!Object(serializer.DeserializeObject(evaluation)).ContainsKey("exceptionDetails"), "Conflict colors did not follow edits/undo: " + evaluation);
        var output = Path.Combine(tempDir, "safety-conflicts-unchanged.json");
        Invoke(model, "ModernSave", output);
        Assert(Equivalent(JsonRows(input), JsonRows(output)), "Conflict colors changed JSON metadata or survived undo.");
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { samplePath });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.ExecuteScriptAsync("window.editorTest.state.count === 1749") == "true") break;
            await Task.Delay(20);
        }
        Console.WriteLine("PASS Safety conflict names/colors: both maps, overlapping centers, palette overflow, contrast, narrow layout, virtual rows, filtering, sorting, edit/undo, unchanged JSON");
    }
}
