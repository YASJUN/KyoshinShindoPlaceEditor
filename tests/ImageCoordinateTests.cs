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
    private static void DraftAnnotations()
    {
        var serializer = new JavaScriptSerializer();
        var original = Object(JsonRows(samplePath)[0]);
        var input = Path.Combine(tempDir, "draft-input.json");
        File.WriteAllText(input, serializer.Serialize(new[] {
            new Dictionary<string, object>(original) { ["code"] = "DRAFT", ["point"] = null },
            new Dictionary<string, object>(original) { ["code"] = "KNOWN" }
        }));
        using (var model = new MainForm())
        {
            Invoke(model, "InitializeModernModel");
            Invoke(model, "ModernLoad", input);
            try {
                Invoke(model, "ModernFillDrafts", new Dictionary<string, KyoshinMonitorLib.Point2> {
                    ["DRAFT"] = new KyoshinMonitorLib.Point2(352, 400)
                });
                throw new Exception("Out-of-image batch draft accepted.");
            } catch (System.Reflection.TargetInvocationException) { }
            Invoke(model, "ModernFillDrafts", new Dictionary<string, KyoshinMonitorLib.Point2> {
                ["DRAFT"] = new KyoshinMonitorLib.Point2(227, 260),
                ["KNOWN"] = new KyoshinMonitorLib.Point2(100, 100)
            });
            Assert((int)typeof(MainForm).GetProperty("ModernDraftCount", PrivateInstance).GetValue(model) == 1,
                "Batch fill changed an existing annotation.");
            Invoke(model, "ModernEdit", "DRAFT", 229, 259);
            Invoke(model, "ModernFillDrafts", new Dictionary<string, KyoshinMonitorLib.Point2> {
                ["DRAFT"] = new KyoshinMonitorLib.Point2(100, 100)
            });
            try { Invoke(model, "ModernLoad", Path.Combine(tempDir, "missing-draft.json")); }
            catch (System.Reflection.TargetInvocationException) { }
            var output = Path.Combine(tempDir, "draft-save.json");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(input), JsonRows(output)), "Unconfirmed coordinates leaked into JSON.");
            Assert(((List<KyoshinMonitorLib.ObservationPoint>)Field(model, "_points"))[0].Point == null,
                "Draft was counted as marked.");
            Invoke(model, "ModernConfirmDraft", "DRAFT");
            Invoke(model, "ModernSave", output);
            var image = Object(Object(JsonRows(output)[0])["point"]);
            Assert(Convert.ToInt32(Object(image["center_point"])["x"]) == 227
                && Convert.ToInt32(Object(image["offset"])["x"]) == 2
                && Convert.ToInt32(Object(image["offset"])["y"]) == -1, "Confirmation lost draft center/offset.");
            Assert(!Object(JsonRows(output)[0]).ContainsKey("needs_review"), "Unexpected review metadata.");
            Invoke(model, "ModernUndo");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(input), JsonRows(output)), "Undo confirmation did not restore a draft.");
            Assert((int)typeof(MainForm).GetProperty("ModernDraftCount", PrivateInstance).GetValue(model) == 1,
                "Undo confirmation lost the draft.");
            Invoke(model, "ModernUndo");
            Invoke(model, "ModernUndo");
            Assert((int)typeof(MainForm).GetProperty("ModernDraftCount", PrivateInstance).GetValue(model) == 0,
                "Batch fill was not a single undo step.");
            Invoke(model, "ModernFillDrafts", new Dictionary<string, KyoshinMonitorLib.Point2> {
                ["DRAFT"] = new KyoshinMonitorLib.Point2(227, 260)
            });
            Invoke(model, "ModernEdit", "DRAFT", 240, 260);
            Invoke(model, "ModernConfirmDraft", "DRAFT");
            Invoke(model, "ModernSave", output);
            image = Object(Object(JsonRows(output)[0])["point"]);
            Assert(Convert.ToInt32(Object(image["center_point"])["x"]) == 240
                && Convert.ToInt32(Object(image["offset"])["x"]) == 0, "Large draft offset was not rebased.");
            Invoke(model, "ModernUndo");
            Invoke(model, "ModernLoad", input);
            Assert((int)typeof(MainForm).GetProperty("ModernDraftCount", PrivateInstance).GetValue(model) == 0,
                "Opening another file retained stale drafts.");
        }
    }

    private static async Task VerifyImageClickSync(ModernEditorForm form, WebView2 browser)
    {
        var serializer = new JavaScriptSerializer();
        var original = Object(JsonRows(samplePath)[0]);
        var fixtures = new List<object>();
        foreach (var code in new[] { "CLICKROOT", "CLICKOTHER" })
            fixtures.Add(new Dictionary<string, object>(original) {
                ["code"] = code, ["name"] = code,
                ["location"] = new Dictionary<string, object> {
                    ["latitude"] = code == "CLICKROOT" ? 35.6812 : 26.2124,
                    ["longitude"] = code == "CLICKROOT" ? 139.7671 : 127.6809
                },
                ["point"] = new Dictionary<string, object> {
                    ["center_point"] = new Dictionary<string, object> { ["x"] = code == "CLICKROOT" ? 227 : 228, ["y"] = code == "CLICKROOT" ? 260 : 261 },
                    ["offset"] = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0 }
                }
            });
        var input = Path.Combine(tempDir, "image-clicks.json");
        File.WriteAllText(input, serializer.Serialize(fixtures));
        var originalRows = JsonRows(input);
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { input });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.ExecuteScriptAsync("window.editorTest.state.count === 2") == "true") break;
            await Task.Delay(20);
        }
        Func<string, Task<string>> evaluate = async expression => {
            var result = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }));
            if (Object(serializer.DeserializeObject(result)).ContainsKey("exceptionDetails"))
                using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image-click-failure.png")))
                    await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
            Assert(!Object(serializer.DeserializeObject(result)).ContainsKey("exceptionDetails"), "Image click scenario failed: " + result);
            return result;
        };
        Func<string, double, double, string, int, Task> mouse = async (type, x, y, button, buttons) => {
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                serializer.Serialize(new { type, x, y, button, buttons, clickCount = 1 }));
        };
        var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(form);
        await evaluate(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageClickScenario.js")));
        var origin = new KyoshinMonitorLib.Point2(26 * 256, 10 * 256);
        var official = MercatorProjection.LatLngToPixel(new KyoshinMonitorLib.Location(35.6812f, 139.7671f), 5) - origin;
        foreach (var target in new[] { new[] { 228, 261, 1 }, new[] { 228, 261, 3 }, new[] { 228, 261, 10 },
            new[] { 107, 147, 1 }, new[] { 154, 30, 1 } })
        {
            var x = target[0]; var y = target[1];
            await evaluate("window.prepareImageClick(" + target[2] + ")");
            var position = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("window.imageClickPosition(" + x + "," + y + ")")));
            if (!(bool)position["inside"])
            {
                var cx = Convert.ToDouble(position["centerX"]); var cy = Convert.ToDouble(position["centerY"]);
                var dx = cx - Convert.ToDouble(position["x"]); var dy = cy - Convert.ToDouble(position["y"]);
                await mouse("mousePressed", cx, cy, "right", 2);
                await mouse("mouseMoved", cx + dx, cy + dy, "right", 2);
                await mouse("mouseReleased", cx + dx, cy + dy, "right", 0);
                await Task.Delay(30);
                position = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("window.imageClickPosition(" + x + "," + y + ")")));
            }
            var screenX = Convert.ToDouble(position["x"]); var screenY = Convert.ToDouble(position["y"]);
            await mouse("mousePressed", screenX, screenY, "right", 2);
            await mouse("mouseMoved", screenX + 17, screenY + 13, "right", 2);
            await mouse("mouseReleased", screenX + 17, screenY + 13, "right", 0);
            await Task.Delay(30);
            await mouse("mousePressed", screenX + 17, screenY + 13, "left", 1);
            await mouse("mouseReleased", screenX + 17, screenY + 13, "left", 0);
            var location = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("ImageCoordinates.imageToLatLon(" + x + "," + y + ")")));
            var geo = location == null ? origin : MercatorProjection.LatLngToPixel(new KyoshinMonitorLib.Location(
                Convert.ToSingle(location["latitude"]), Convert.ToSingle(location["longitude"])), 5) - origin;
            await evaluate("window.verifyImageClick(" + x + "," + y + "," + geo.X + "," + geo.Y + "," + official.X + "," + official.Y + ")");
            var outputDuringEdit = Path.Combine(tempDir, "image-click-during-edit.json");
            Invoke(model, "ModernSave", outputDuringEdit);
            var duringRows = JsonRows(outputDuringEdit);
            Assert(Equivalent(Object(duringRows[0])["location"], Object(originalRows[0])["location"])
                && Equivalent(duringRows[1], originalRows[1]), "Image click changed official geography or edited the overlapped station.");
            if (target[2] == 10)
            {
                await evaluate("window.verifyPreviewZoom()");
                using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image-click-sync.png")))
                    await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
            }
            if (target[2] == 3)
                await evaluate("window.editorTest.select('CLICKOTHER'); window.verifyPreviewCleared()");
            await evaluate("window.editorTest.command('undo')");
            await evaluate("window.verifyPreviewCleared()");
        }
        await evaluate("window.prepareImageClick(10)");
        var lowerCenter = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync(
            "(()=>{const r=document.getElementById('geographic').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()")));
        for (var index = 0; index < 20; index++)
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", serializer.Serialize(new {
                type = "mouseWheel", x = lowerCenter["x"], y = lowerCenter["y"], deltaX = 0, deltaY = -120
            }));
        await Task.Delay(50);
        await evaluate("(()=>{if(document.getElementById('geographic').getContext('2d').getTransform().a/devicePixelRatio!==10)throw new Error('Lower zoom does not reach/cap at 10x');return true;})()");
        var highZoomClick = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("window.imageClickPosition(228,261)")));
        await mouse("mousePressed", Convert.ToDouble(highZoomClick["x"]), Convert.ToDouble(highZoomClick["y"]), "left", 1);
        await mouse("mouseReleased", Convert.ToDouble(highZoomClick["x"]), Convert.ToDouble(highZoomClick["y"]), "left", 0);
        var highLocation = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("ImageCoordinates.imageToLatLon(228,261)")));
        var highGeo = MercatorProjection.LatLngToPixel(new KyoshinMonitorLib.Location(
            Convert.ToSingle(highLocation["latitude"]), Convert.ToSingle(highLocation["longitude"])), 5) - origin;
        await evaluate("window.verifyImageClick(228,261," + highGeo.X + "," + highGeo.Y + "," + official.X + "," + official.Y + ")");
        var beforeTransform = await browser.ExecuteScriptAsync("document.getElementById('geographic').getContext('2d').getTransform().toString()");
        await evaluate("document.getElementById('sync').checked = false; true");
        highZoomClick = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("window.imageClickPosition(229,262)")));
        await mouse("mousePressed", Convert.ToDouble(highZoomClick["x"]), Convert.ToDouble(highZoomClick["y"]), "left", 1);
        await mouse("mouseReleased", Convert.ToDouble(highZoomClick["x"]), Convert.ToDouble(highZoomClick["y"]), "left", 0);
        var nextLocation = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync("ImageCoordinates.imageToLatLon(229,262)")));
        var nextGeo = MercatorProjection.LatLngToPixel(new KyoshinMonitorLib.Location(
            Convert.ToSingle(nextLocation["latitude"]), Convert.ToSingle(nextLocation["longitude"])), 5) - origin;
        await evaluate("window.verifyImageClick(229,262," + nextGeo.X + "," + nextGeo.Y + "," + official.X + "," + official.Y + ",false)");
        Assert(beforeTransform == await browser.ExecuteScriptAsync("document.getElementById('geographic').getContext('2d').getTransform().toString()"),
            "Disabling synchronization still moved the lower map.");
        var locateButton = Object(serializer.DeserializeObject(await browser.ExecuteScriptAsync(
            "(()=>{const r=document.getElementById('locate').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()")));
        await mouse("mousePressed", Convert.ToDouble(locateButton["x"]), Convert.ToDouble(locateButton["y"]), "left", 1);
        await mouse("mouseReleased", Convert.ToDouble(locateButton["x"]), Convert.ToDouble(locateButton["y"]), "left", 0);
        await evaluate("window.verifyPreviewCleared()");
        await evaluate("window.editorTest.command('undo')");
        await evaluate("window.editorTest.command('undo')");
        await evaluate("document.getElementById('sync').checked = true; true");
        await evaluate("window.finishImageClicks()");
        var output = Path.Combine(tempDir, "image-clicks-unchanged.json");
        Invoke(model, "ModernSave", output);
        Assert(Equivalent(JsonRows(input), JsonRows(output)), "Click preview changed official geography or failed to undo pixel edits.");
        // Isolate the following benchmark from this scenario's 10x OSM view and canvas probes.
        var navigation = new TaskCompletionSource<bool>();
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs> navigated =
            (sender, args) => navigation.TrySetResult(args.IsSuccess);
        browser.CoreWebView2.NavigationCompleted += navigated;
        browser.CoreWebView2.Reload();
        Assert(await navigation.Task, "Could not restore the initial browser view after click tests.");
        browser.CoreWebView2.NavigationCompleted -= navigated;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.ExecuteScriptAsync("!!window.editorTest && window.editorTest.state.count === 2 && window.editorTest.state.pendingCount === 0") == "true") break;
            await Task.Delay(20);
        }
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { samplePath });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await browser.ExecuteScriptAsync("window.editorTest.state.count === 1749") == "true") break;
            await Task.Delay(20);
        }
        Console.WriteLine("PASS Image clicks: mainland/inset inverse preview, overlap selection, 1x/3x/10x, panning, invalid regions, undo, both zoom caps, unchanged JSON");
    }

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
                ["is_suspended"] = index == 1 || index == 5,
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
        evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new { expression = "window.verifyUnmarkedFilterEdits()", awaitPromise = true, returnByValue = true }));
        Assert(!Object(serializer.DeserializeObject(evaluation)).ContainsKey("exceptionDetails"), "Unmarked filter did not follow edits/undo: " + evaluation);
        Invoke(model, "ModernSave", output);
        Assert(Equivalent(JsonRows(input), JsonRows(output)), "Unmarked filtering changed JSON or did not undo annotation.");
        Func<string, Task> draftEvaluate = async expression => {
            var result = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }));
            Assert(!Object(serializer.DeserializeObject(result)).ContainsKey("exceptionDetails"), "Draft UI scenario failed: " + result);
        };
        typeof(ModernEditorForm).GetField("_currentSavePath", PrivateInstance).SetValue(form, output);
        await draftEvaluate(@"(async()=>{
            const $=id=>document.getElementById(id);
            window.assertDraft=(condition,message)=>{if(!condition)throw new Error(message);};
            window.waitDraft=async()=>{for(let i=0;i<100;i++){
                if(window.editorTest.state.pendingCount===0)return;
                await new Promise(r=>setTimeout(r,20));
            }throw new Error('Draft command did not complete');};
            $('only-unmarked').checked=true; $('only-unmarked').dispatchEvent(new Event('change'));
            window.editorTest.select('ESTTOKYO');
            $('fillDrafts').click(); await window.waitDraft();
            assertDraft(!$('confirmDraft').disabled,'Missing explicit confirmation: '+$('notice').textContent);
            assertDraft(document.querySelector('[data-code=ESTTOKYO]').textContent.includes('待核对 227, 259'),'Draft left unmarked list');
            assertDraft($('counts').textContent.includes('1 已标注') && $('counts').textContent.includes('3 待核对'),'Draft count is incorrect');
            assertDraft($('selection').textContent.includes('尚未写入'),'Missing draft state');
            await window.editorTest.command('edit',{code:'ESTNAHA',x:228,y:260});
            window.editorTest.select('ESTTOKYO');
            assertDraft($('conflicts').textContent.includes('ESTNAHA'),'Draft conflicts were ignored');
            await new Promise(requestAnimationFrame);
            const canvas=$('monitor'), rect=canvas.getBoundingClientRect(), matrix=canvas.getContext('2d').getTransform();
            canvas.dispatchEvent(new PointerEvent('pointerdown',{button:0,
                clientX:rect.x+(229.5*matrix.a+matrix.e)/devicePixelRatio,
                clientY:rect.y+(259.5*matrix.d+matrix.f)/devicePixelRatio}));
            await window.waitDraft();
            assertDraft($('selection').textContent.includes('偏移：2, 0'),'Draft offset not displayed');
            assertDraft(!$('geo-preview').hidden && $('geo-preview').textContent.includes('临时'),'Draft click lost temporary geographic preview');
            await window.editorTest.command('save');
            assertDraft(!$('confirmDraft').disabled,'Save discarded the draft');
        })()");
        Assert(Equivalent(JsonRows(input), JsonRows(output)), "UI save exported unconfirmed drafts.");
        Assert(!(bool)typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(form), "Saving drafts marked official data dirty.");
        var discardObserved = false;
        using (var reply = ReplyToClearDialog(System.Windows.Forms.DialogResult.No, (text, defaultNo) => {
            discardObserved = true;
            Assert(defaultNo && text.Contains("草稿") && text.Contains("内存"), "Draft loss warning is incomplete.");
        }, "未保存的修改 / 草稿"))
            Assert(!(bool)typeof(ModernEditorForm).GetMethod("ConfirmDiscard", PrivateInstance).Invoke(form, null),
                "Discard ignored cancellation.");
        Assert(discardObserved, "Saving official data suppressed the draft loss warning.");
        var clearedDraft = false;
        using (var reply = ReplyToClearDialog(System.Windows.Forms.DialogResult.No, (text, defaultNo) => {
            Assert(defaultNo && text.Contains("草稿"), "Draft clear has no safe confirmation.");
            clearedDraft = true;
        }))
            await draftEvaluate("window.editorTest.command('clear',{code:'ESTTOKYO'})");
        Assert(clearedDraft, "Draft clear confirmation was not shown.");
        await draftEvaluate("assertDraft(!document.getElementById('confirmDraft').disabled,'Cancelled clear removed the draft')");
        using (var reply = ReplyToClearDialog(System.Windows.Forms.DialogResult.Yes, (text, defaultNo) => { }))
            await draftEvaluate("window.editorTest.command('clear',{code:'ESTTOKYO'})");
        await draftEvaluate(@"(async()=>{
            assertDraft(document.getElementById('confirmDraft').disabled,'Clear did not remove the draft');
            await window.editorTest.command('undo');
            assertDraft(!document.getElementById('confirmDraft').disabled,'Undo did not restore cleared draft');
            document.getElementById('dismiss').click();
        })()");
        using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "annotation-drafts.png")))
            await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        await draftEvaluate(@"(async()=>{
            document.getElementById('confirmDraft').click(); await window.waitDraft();
            assertDraft(!document.querySelector('[data-code=ESTTOKYO]'),'Confirmed point remained unmarked');
            await window.editorTest.command('save');
        })()");
        var confirmed = Object(Object(JsonRows(output)[0])["point"]);
        Assert(Convert.ToInt32(Object(confirmed["center_point"])["x"]) == 227
            && Convert.ToInt32(Object(confirmed["offset"])["x"]) == 2, "UI confirmation did not preserve center and offset.");
        Assert(Object(JsonRows(output)[1])["point"] == null, "Confirming one station committed another draft.");
        await draftEvaluate(@"(async()=>{
            await window.editorTest.command('undo');
            assertDraft(document.querySelector('[data-code=ESTTOKYO]').textContent.includes('待核对'),'Undo did not restore review state');
            await window.editorTest.command('undo');
            await window.editorTest.command('undo');
            await window.editorTest.command('undo');
            await window.editorTest.command('save');
            document.getElementById('only-unmarked').checked=false;
            document.getElementById('only-unmarked').dispatchEvent(new Event('change'));
        })()");
        Assert(Equivalent(JsonRows(input), JsonRows(output)), "Draft undo did not restore the original JSON.");
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { samplePath });
        await Task.Delay(50);
        Console.WriteLine("PASS Estimated markers: mainland/inset, 1x/3x/5x/10x, panning, invalid regions, no safety square, unchanged JSON");
        Console.WriteLine("PASS List filters: unmarked, hide/only suspended, combined search, empty results, estimates remain unmarked, edit/status/undo, unchanged JSON");
        Console.WriteLine("PASS Draft UI: batch fill, bounds/region exclusions, conflicts, adjustment, unmarked retention, save isolation, explicit confirmation/offset, undo");
    }
}
