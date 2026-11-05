using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using KyoshinMonitorLib;
using KyoshinShindoPlaceEditor;

internal static class EditorRegressionTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static string samplePath;
    private static string tempDir;
    private static int failures;
    private static int tests;

    [STAThread]
    private static int Main(string[] args)
    {
        samplePath = args[0];
        tempDir = Path.Combine(Path.GetTempPath(), "KyoshinEditorTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            Run("Invalid render data never leaves the window disabled", RenderRestoresUi);
            Run("Real modern JSON loads all station fields", LoadsModernJson);
            Run("Unedited modern JSON round trip preserves all fields", RoundTripModernJson);
            Run("Edits retain centers, bounded offsets and extra fields", SavesModernEdits);
            Run("Legacy JSON remains readable and writable", LegacyJson);
            Run("Failed load preserves previous document and usable UI", FailedLoadIsTransactional);
            Run("Smaller subsequent files and out-of-image clicks are safe", SmallerFileAndClick);
            Run("Point edits support multiple undo steps without losing JSON metadata", UndoEdits);
            Run("Modern and legacy NIED CSV records are readable", NiedCsvFormats);
            Run("NIED import can be undone and failed imports preserve data", UndoNiedImport);
            Run("Selection highlight blinks and safety square never blocks editing", SelectionHighlight);
            Run("Official NIED downloads are exposed inside the editor", DownloadMenu);
            Run("Large NIED edits keep selection stable without rebuilding every row", LargeNiedEdits);
            Run("Station files merge coordinates with NIED metadata and support undo", MergeStationFiles);
            if (args.Length > 1 && args[1] == "--network")
                Run("Official NIED downloads return valid original CSV bytes", LiveNiedDownloads);
            Console.WriteLine("RESULT: " + (tests - failures) + " passed, " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static void Run(string name, Action test)
    {
        tests++;
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static object Field(MainForm form, string name)
        => typeof(MainForm).GetField(name, PrivateInstance).GetValue(form);

    private static void SetField(MainForm form, string name, object value)
        => typeof(MainForm).GetField(name, PrivateInstance).SetValue(form, value);

    private static object Invoke(MainForm form, string name, params object[] args)
        => typeof(MainForm).GetMethod(name, PrivateInstance).Invoke(form, args);

    private static object ReadDocument(string path)
    {
        var type = typeof(MainForm).Assembly.GetType("KyoshinShindoPlaceEditor.ObservationPointJsonDocument");
        Assert(type != null, "Modern JSON document support is missing.");
        return type.GetMethod("Load").Invoke(null, new object[] { path });
    }

    private static List<ObservationPoint> Points(object document)
        => (List<ObservationPoint>)document.GetType().GetProperty("Points").GetValue(document);

    private static void Save(object document, string path)
        => document.GetType().GetMethod("Save").Invoke(document, new object[] { path, Points(document) });

    private static object[] JsonRows(string path)
        => (object[])new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }
            .DeserializeObject(File.ReadAllText(path));

    private static Dictionary<string, object> Object(object value)
        => (Dictionary<string, object>)value;

    private static bool Equivalent(object left, object right)
    {
        var a = left as Dictionary<string, object>;
        var b = right as Dictionary<string, object>;
        if (a != null || b != null)
            return a != null && b != null && a.Count == b.Count &&
                a.All(pair => b.ContainsKey(pair.Key) && Equivalent(pair.Value, b[pair.Key]));
        var aa = left as object[];
        var bb = right as object[];
        if (aa != null || bb != null)
            return aa != null && bb != null && aa.Length == bb.Length &&
                aa.Select((item, i) => Equivalent(item, bb[i])).All(value => value);
        if (left == null || right == null) return left == right;
        return left.Equals(right);
    }

    private static void RenderRestoresUi()
    {
        using (var form = new MainForm())
        {
            var originalTitle = form.Text;
            SetField(form, "_points", new List<ObservationPoint> { null });
            var failed = false;
            try { Invoke(form, "UpdateListValue"); } catch (TargetInvocationException) { failed = true; }
            Assert(failed, "Render failure was not exercised.");
            Assert(form.Enabled, "Window stays disabled after a render error.");
            Assert(form.Text == originalTitle, "Busy title was not restored.");
            SetField(form, "_points", new List<ObservationPoint>());
            Invoke(form, "UpdateListValue");
            Assert(form.Enabled, "Cannot refresh again after an error.");
        }
    }

    private static void LoadsModernJson()
    {
        var points = Points(ReadDocument(samplePath));
        Assert(points.Count == 1749, "Expected 1749 stations.");
        Assert(points.All(p => !string.IsNullOrWhiteSpace(p.Code) && p.Location != null), "Empty station fields.");
        Assert(points[0].Code == "ABSH01" && points[0].Type == ObservationPointType.KiK_net, "First station mismatch.");
        Assert(points[0].Point.Value.X == 289 && points[0].Point.Value.Y == 42, "Image coordinates mismatch.");
        Assert(points.Count(p => p.Point != null) == 1634, "Null image coordinates were not preserved.");
        Assert(points.Count(p => p.Type == ObservationPointType.K_NET) == 1047, "Network type mismatch.");
    }

    private static void RoundTripModernJson()
    {
        var path = Path.Combine(tempDir, "roundtrip.json");
        Save(ReadDocument(samplePath), path);
        Assert(Equivalent(JsonRows(samplePath), JsonRows(path)), "Unedited fields changed during save.");
        Assert(Points(ReadDocument(path)).Count == 1749, "Saved file cannot be reopened.");
        var optionalPath = Path.Combine(tempDir, "optional.json");
        foreach (var suffix in new[] { "", ",\"point\":{\"center_point\":{\"x\":100,\"y\":100},\"offset\":{\"x\":10,\"y\":0}}" })
        {
            File.WriteAllText(optionalPath, "[{\"type\":\"k_NET\",\"code\":\"OPTIONAL\",\"name\":\"station\",\"region\":\"region\",\"is_suspended\":false,\"location\":{\"latitude\":35,\"longitude\":139}" + suffix + "}]");
            Save(ReadDocument(optionalPath), path);
            Assert(Equivalent(JsonRows(optionalPath), JsonRows(path)), "Unedited optional image fields changed.");
        }
    }

    private static void SavesModernEdits()
    {
        var input = Path.Combine(tempDir, "extras.json");
        File.WriteAllText(input, @"[{""type"":""kiK_net"",""code"":""TEST1"",""name"":""station"",""region"":""region"",
            ""sub_region"":""detail"",""is_suspended"":false,""location"":{""latitude"":35.123456789,""longitude"":139.123456789},
            ""custom"":{""keep"":true},""point"":{""center_point"":{""x"":100,""y"":100},""offset"":{""x"":2,""y"":-1},""extra"":7}}]");
        var document = ReadDocument(input);
        var point = Points(document)[0];
        point.Point = new Point2(103, 101);
        point.IsSuspended = true;
        var output = Path.Combine(tempDir, "edited.json");
        Save(document, output);
        var row = Object(JsonRows(output)[0]);
        var image = Object(row["point"]);
        Assert((int)Object(image["center_point"])["x"] == 100, "Small edit moved the center.");
        Assert((int)Object(image["offset"])["x"] == 3 && (int)Object(image["offset"])["y"] == 1, "Wrong offset.");
        Assert((bool)row["is_suspended"] && (string)row["sub_region"] == "detail", "Metadata lost.");
        Assert((int)image["extra"] == 7 && Equivalent(Object(JsonRows(input)[0])["custom"], row["custom"]), "Unknown fields lost.");
        Assert(Equivalent(Object(JsonRows(input)[0])["location"], row["location"]), "Coordinate precision lost.");
        point.Point = new Point2(300, 200);
        Save(document, output);
        image = Object(Object(JsonRows(output)[0])["point"]);
        Assert((int)Object(image["center_point"])["x"] == 300 && (int)Object(image["offset"])["x"] == 0, "Large edit produced an invalid offset.");
        point.Point = null;
        Save(document, output);
        Assert(Object(JsonRows(output)[0])["point"] == null, "Cleared image coordinate was not saved.");
    }

    private static void LegacyJson()
    {
        var input = Path.Combine(tempDir, "legacy.json");
        File.WriteAllText(input, @"[{""Type"":""K_NET"",""Code"":""OLD1"",""Name"":""legacy"",""Region"":""region"",
            ""IsSuspended"":false,""Location"":{""Latitude"":35,""Longitude"":139},""Point"":{""X"":30,""Y"":40}}]");
        var document = ReadDocument(input);
        Assert(Points(document)[0].Code == "OLD1", "Legacy data was not read.");
        var output = Path.Combine(tempDir, "legacy-save.json");
        Save(document, output);
        Assert(Object(JsonRows(output)[0]).ContainsKey("Code"), "Legacy schema unexpectedly changed.");
        Assert(Points(ReadDocument(output))[0].Point.Value.X == 30, "Legacy round trip failed.");
    }

    private static bool TryLoad(MainForm form, string path, out string error)
    {
        var method = typeof(MainForm).GetMethod("TryLoadFile", PrivateInstance);
        Assert(method != null, "Transactional file loading is missing.");
        var args = new object[] { path, null, null };
        var success = (bool)method.Invoke(form, args);
        error = (string)args[1];
        return success;
    }

    private static void FailedLoadIsTransactional()
    {
        using (var form = new MainForm())
        {
            var list = (ListView)Field(form, "listView1");
            var handle = list.Handle;
            string error;
            Assert(TryLoad(form, samplePath, out error), error);
            var previous = Field(form, "_points");
            var previousPath = Field(form, "_lastFilePath");
            var invalids = new[] {
                "[{\"code\":\"BROKEN\"}]",
                "[{\"type\":\"kiK_net\",\"code\":\"BAD\",\"name\":\"bad\",\"region\":\"r\",\"is_suspended\":false,\"location\":{\"latitude\":35,\"longitude\":139},\"point\":{\"center_point\":{\"x\":400,\"y\":100},\"offset\":{\"x\":0,\"y\":0}}}]",
                "{not-json"
            };
            foreach (var contents in invalids)
            {
                var path = Path.Combine(tempDir, "invalid.json");
                File.WriteAllText(path, contents);
                Assert(!TryLoad(form, path, out error) && !string.IsNullOrWhiteSpace(error), "Invalid file was accepted.");
                Assert(ReferenceEquals(previous, Field(form, "_points")), "Failed load replaced previous data.");
                Assert(Equals(previousPath, Field(form, "_lastFilePath")), "Failed load changed save target.");
                Assert(form.Enabled && !form.Text.Contains("["), "UI remains busy after failure.");
            }
            Assert(TryLoad(form, samplePath, out error), "Cannot open a valid file after failure: " + error);
        }
    }

    private static void SmallerFileAndClick()
    {
        using (var form = new MainForm())
        {
            string error;
            Assert(TryLoad(form, samplePath, out error), error);
            var list = (ListView)Field(form, "listView1");
            var handle = list.Handle;
            list.Items[0].Selected = true;
            Invoke(form, "ListViewSelectedIndexChanged", list, EventArgs.Empty);
            Assert(Field(form, "_mapPoint") != null, "Selection did not update the map marker.");
            list.Items[list.Items.Count - 1].Selected = true;
            var single = Path.Combine(tempDir, "single.json");
            File.WriteAllText(single, new JavaScriptSerializer().Serialize(new[] { JsonRows(samplePath)[0] }));
            Assert(TryLoad(form, single, out error), error);
            Assert(list.Items.Count == 1 && form.Enabled, "Loading a smaller file broke selection.");
            list.Items[0].Selected = true;
            var before = ((List<ObservationPoint>)Field(form, "_points"))[0].Point;
            Invoke(form, "InterpolatedPictureBox2_MouseDown", null, new MouseEventArgs(MouseButtons.Left, 1, 500, 500, 0));
            Assert(((List<ObservationPoint>)Field(form, "_points"))[0].Point == before, "Out-of-image click changed a point.");
        }
    }

    private static MainForm LoadedForm()
    {
        var form = new MainForm();
        var list = (ListView)Field(form, "listView1");
        var handle = list.Handle;
        string error;
        Assert(TryLoad(form, samplePath, out error), error);
        list.Items[0].Selected = true;
        return form;
    }

    private static void UndoEdits()
    {
        using (var form = LoadedForm())
        {
            Assert(typeof(MainForm).GetMethod("UndoLastEdit", PrivateInstance) != null, "Undo is missing.");
            var point = ((List<ObservationPoint>)Field(form, "_points"))[0];
            var originalPixel = point.Point;
            var originalSuspended = point.IsSuspended;
            Invoke(form, "InterpolatedPictureBox2_MouseDown", null, new MouseEventArgs(MouseButtons.Left, 1, 200, 150, 0));
            Invoke(form, "ToggleSuspendToolStripMenuItem_Click", null, EventArgs.Empty);
            Invoke(form, "RemovePointToolStripMenuItem_Click", null, EventArgs.Empty);
            Invoke(form, "UndoLastEdit");
            Assert(point.Point == new Point2(200, 150), "Undo did not restore cleared pixel.");
            Invoke(form, "UndoLastEdit");
            Assert(point.IsSuspended == originalSuspended, "Undo did not restore suspension.");
            Invoke(form, "UndoLastEdit");
            Assert(point.Point == originalPixel, "Undo did not restore original pixel.");
            Invoke(form, "InterpolatedPictureBox2_MouseDown", null,
                new MouseEventArgs(MouseButtons.Left, 1, originalPixel.Value.X, originalPixel.Value.Y, 0));
            Assert(!((ToolStripMenuItem)Field(form, "_undoMenuItem")).Enabled, "An unchanged click created an undo step.");
            Invoke(form, "SortPoints");
            if (((ToolStripMenuItem)Field(form, "_undoMenuItem")).Enabled)
                Invoke(form, "UndoLastEdit");
            var output = Path.Combine(tempDir, "undo-save.json");
            Invoke(form, "SaveFile", output);
            Assert(Equivalent(JsonRows(samplePath), JsonRows(output)), "Undo lost modern JSON fields.");
            var menu = (ToolStripMenuItem)Field(form, "_undoMenuItem");
            Assert(!menu.Enabled && menu.ShortcutKeys == (Keys.Control | Keys.Z), "Undo shortcut or empty state is wrong.");
            Invoke(form, "ToggleSuspendToolStripMenuItem_Click", null, EventArgs.Empty);
            string error;
            Assert(TryLoad(form, samplePath, out error), error);
            Assert(!menu.Enabled, "Opening a different file retained old undo steps.");
        }
    }

    private static object ReadNied(string path, ObservationPointType type)
    {
        var reader = typeof(MainForm).Assembly.GetType("KyoshinShindoPlaceEditor.NiedCsvFile");
        Assert(reader != null, "NIED CSV format support is missing.");
        return reader.GetMethod("Read").Invoke(null, new object[] { path, type });
    }

    private static void NiedCsvFormats()
    {
        var modern = Path.Combine(tempDir, "nied-modern.csv");
        File.WriteAllText(modern, "K-NET,AIC001,\"尾西,試験\",BISAI,35.2974,136.7505,5,-,愛知県,K-NET11A,休止\r\n", System.Text.Encoding.GetEncoding(932));
        var records = (List<ObservationPoint>)ReadNied(modern, ObservationPointType.K_NET);
        Assert(records.Count == 1 && records[0].Name == "尾西,試験", "Quoted CSV field was not read.");
        Assert(records[0].IsSuspended && records[0].OldLocation == null && records[0].Point == null, "Modern NIED metadata is wrong.");
        var legacy = Path.Combine(tempDir, "nied-legacy.csv");
        File.WriteAllText(legacy, "OLD1,station,STATION,35.1,139.1,5,NA,region,REGION,,,K-NET11A,,suspension\r\n", System.Text.Encoding.GetEncoding(932));
        records = (List<ObservationPoint>)ReadNied(legacy, ObservationPointType.K_NET);
        Assert(records[0].Code == "OLD1" && records[0].OldLocation == null && records[0].IsSuspended, "Legacy NIED fields were not preserved.");
        var actualDir = @"D:\KyoshinShindoPlaceEditor.0.0.6.0\NIED-original-20261105";
        if (Directory.Exists(actualDir))
        {
            Assert(((List<ObservationPoint>)ReadNied(Path.Combine(actualDir, "stationlist_knet.csv"), ObservationPointType.K_NET)).Count == 1047, "Official K-NET CSV mismatch.");
            Assert(((List<ObservationPoint>)ReadNied(Path.Combine(actualDir, "stationlist_kiknet.csv"), ObservationPointType.KiK_net)).Count == 702, "Official KiK-net CSV mismatch.");
        }
    }

    private static void UndoNiedImport()
    {
        using (var form = LoadedForm())
        {
            Assert(typeof(MainForm).GetMethod("ApplyNiedImport", PrivateInstance) != null, "Undoable NIED import is missing.");
            var before = (List<ObservationPoint>)Field(form, "_points");
            var first = before[0];
            var oldName = first.Name;
            var oldPoint = first.Point;
            var incoming = new List<ObservationPoint> {
                new ObservationPoint { Code = first.Code, Type = first.Type, Name = "updated", Region = first.Region, Location = new Location(35, 139) },
                new ObservationPoint { Code = "NEW1", Type = ObservationPointType.K_NET, Name = "new", Region = "region", Location = new Location(35, 139) }
            };
            Invoke(form, "ApplyNiedImport", incoming);
            Assert(((List<ObservationPoint>)Field(form, "_points")).Count == 1750 && first.Point == oldPoint, "Import replaced image coordinates.");
            Invoke(form, "UndoLastEdit");
            var restored = (List<ObservationPoint>)Field(form, "_points");
            Assert(restored.Count == 1749 && ReferenceEquals(restored[0], first) && first.Name == oldName, "Undo import lost original station objects.");
            var output = Path.Combine(tempDir, "undo-import.json");
            Invoke(form, "SaveFile", output);
            Assert(Equivalent(JsonRows(samplePath), JsonRows(output)), "Undo import lost JSON precision or unknown fields.");
            incoming[0].Location = null;
            try { Invoke(form, "ApplyNiedImport", incoming); throw new Exception("Invalid import succeeded."); }
            catch (TargetInvocationException) { }
            Assert(((List<ObservationPoint>)Field(form, "_points")).Count == 1749 && first.Name == oldName, "Invalid import partially changed the document.");
            incoming[0].Location = new Location(35, 139);
            incoming[1].Code = restored[1].Code;
            incoming[1].Type = restored[1].Type == ObservationPointType.K_NET ? ObservationPointType.KiK_net : ObservationPointType.K_NET;
            try { Invoke(form, "ApplyNiedImport", incoming); throw new Exception("Conflicting network import succeeded."); }
            catch (TargetInvocationException) { }
            Assert(first.Name == oldName && ((List<ObservationPoint>)Field(form, "_points")).Count == 1749, "Partial import was not rolled back.");
        }
    }

    private static void SelectionHighlight()
    {
        using (var form = LoadedForm())
        {
            Assert(typeof(MainForm).GetMethod("RefreshSelectionHighlight", PrivateInstance) != null, "Selection highlighting is missing.");
            Invoke(form, "RefreshSelectionHighlight");
            var timer = (Timer)Field(form, "_selectionTimer");
            Assert(timer.Enabled, "Selected point does not blink.");
            var beforeBlink = (bool)Field(form, "_selectionBlinkVisible");
            Invoke(form, "SelectionTimerTick", null, EventArgs.Empty);
            Assert((bool)Field(form, "_selectionBlinkVisible") != beforeBlink, "Blink timer does not change the highlight.");
            var first = ((List<ObservationPoint>)Field(form, "_points"))[0];
            var other = ((List<ObservationPoint>)Field(form, "_points"))[1];
            Invoke(form, "InterpolatedPictureBox2_MouseDown", null,
                new MouseEventArgs(MouseButtons.Left, 1, other.Point.Value.X, other.Point.Value.Y, 0));
            Assert(first.Point == other.Point, "Safety square incorrectly blocked an overlapping point.");
            Invoke(form, "RefreshSelectionHighlight");
            var status = (ToolStripStatusLabel)Field(form, "_selectionStatus");
            Assert(status.Text.Contains("冲突"), "Safety square does not report neighboring points.");
            Invoke(form, "KyoshinZoomChange_3", null, EventArgs.Empty);
            var bounds = (System.Drawing.Rectangle)Invoke(form, "SelectionSafetyBounds");
            Assert(bounds.Width == 9 && bounds.Height == 9, "Safety square does not follow image zoom.");
            SetField(form, "_selectionBlinkVisible", true);
            using (var bitmap = new System.Drawing.Bitmap(1056, 1200))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Black);
                Invoke(form, "DrawSelectedPointHighlight", null, new PaintEventArgs(graphics, new System.Drawing.Rectangle(0, 0, 1056, 1200)));
                Assert(bitmap.GetPixel(other.Point.Value.X * 3 + 1, other.Point.Value.Y * 3 + 1).ToArgb() == System.Drawing.Color.Yellow.ToArgb(), "Blink marker is not painted.");
                Assert(bitmap.GetPixel(bounds.X, bounds.Y).ToArgb() == System.Drawing.Color.Red.ToArgb(), "Conflicting safety frame is not painted red.");
                Invoke(form, "SelectionTimerTick", null, EventArgs.Empty);
                graphics.Clear(System.Drawing.Color.Black);
                Invoke(form, "DrawSelectedPointHighlight", null, new PaintEventArgs(graphics, new System.Drawing.Rectangle(0, 0, 1056, 1200)));
                Assert(bitmap.GetPixel(other.Point.Value.X * 3 + 1, other.Point.Value.Y * 3 + 1).ToArgb() != System.Drawing.Color.Yellow.ToArgb(), "Blink marker did not switch off.");
            }
            Invoke(form, "UndoLastEdit");
            Assert(first.Point != other.Point, "Overlapping edit cannot be undone.");
        }
    }

    private static void DownloadMenu()
    {
        var downloader = typeof(MainForm).Assembly.GetType("KyoshinShindoPlaceEditor.NiedCsvFile");
        Assert(downloader.GetMethod("Download") != null, "Official CSV downloads are missing.");
        using (var form = new MainForm())
        {
            var menu = (ToolStripMenuItem)Field(form, "_downloadNiedMenu");
            Assert(menu.DropDownItems.Count == 3 && menu.Enabled, "Both download entries and the official page are required.");
        }
    }

    private static void LiveNiedDownloads()
    {
        var downloader = typeof(MainForm).Assembly.GetType("KyoshinShindoPlaceEditor.NiedCsvFile");
        foreach (var type in new[] { ObservationPointType.K_NET, ObservationPointType.KiK_net })
        {
            var bytes = (byte[])downloader.GetMethod("Download").Invoke(null, new object[] { type });
            var output = Path.Combine(tempDir, type + ".csv");
            File.WriteAllBytes(output, bytes);
            var records = (List<ObservationPoint>)ReadNied(output, type);
            Assert(records.Count == (type == ObservationPointType.K_NET ? 1047 : 702), "Official downloaded station count mismatch.");
        }
    }

    private static void LargeNiedEdits()
    {
        using (var form = new MainForm())
        {
            var incoming = Points(ReadDocument(samplePath));
            foreach (var point in incoming) point.Point = null;
            SetField(form, "_points", new List<ObservationPoint>());
            var list = (ListView)Field(form, "listView1");
            var handle = list.Handle;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Invoke(form, "ApplyNiedImport", incoming);
            var importMs = watch.ElapsedMilliseconds;
            watch.Restart();
            list.Items[list.Items.Count - 1].Selected = true;
            for (var i = 0; i < 3; i++)
                Invoke(form, "ListViewSelectedIndexChanged", list, EventArgs.Empty);
            var selectMs = watch.ElapsedMilliseconds;
            var changes = 0;
            list.SelectedIndexChanged += (sender, args) => changes++;
            watch.Restart();
            for (var i = 0; i < 5; i++)
                Invoke(form, "SetSelectedImagePoint", new Point2(100 + i, 100));
            var editMs = watch.ElapsedMilliseconds;
            Console.WriteLine("PERF 1749 NIED stations: import=" + importMs + "ms select=" + selectMs + "ms edits(5)=" + editMs + "ms selectionChanges=" + changes);
            Assert(list.SelectedItems.Count == 1 && list.SelectedItems[0].Text == incoming.Last().Code, "Editing lost selection.");
            Assert(changes == 0, "A single-coordinate edit unnecessarily resets the full list selection.");
            Assert(importMs < 5000 && selectMs < 3000 && editMs < 3000, "Large-file interaction exceeded the response budget.");
        }
    }

    private static void MergeStationFiles()
    {
        using (var form = new MainForm())
        {
            Assert(typeof(MainForm).GetMethod("MergePointFile", PrivateInstance) != null, "Station-file merge is missing.");
            var rows = JsonRows(samplePath);
            var firstCode = (string)Object(rows[0])["code"];
            var incoming = Points(ReadDocument(samplePath));
            var official = new ObservationPoint { Code = firstCode, Type = incoming[0].Type, Name = "NIED official name",
                Region = "official region", Location = incoming[0].Location, IsSuspended = true };
            var exclusive = new ObservationPoint { Code = "NIED_ONLY", Type = ObservationPointType.K_NET, Name = "official only",
                Region = "region", Location = new Location(35, 139) };
            SetField(form, "_points", new List<ObservationPoint>());
            var list = (ListView)Field(form, "listView1");
            var handle = list.Handle;
            Invoke(form, "ApplyNiedImport", new List<ObservationPoint> { official, exclusive });
            var previousPath = Field(form, "_lastFilePath");
            Invoke(form, "MergePointFile", samplePath);
            var merged = (List<ObservationPoint>)Field(form, "_points");
            Assert(merged.Count == 1750 && ReferenceEquals(merged[0], official) && merged.Contains(exclusive), "Merge replaced NIED stations.");
            Assert(official.Point == incoming[0].Point && official.Name == "NIED official name" && official.IsSuspended,
                "Merge did not preserve official metadata while adding image coordinates.");
            Assert(Equals(previousPath, Field(form, "_lastFilePath")), "Import changed the current save target.");
            var output = Path.Combine(tempDir, "merged.json");
            Invoke(form, "SaveFile", output);
            var savedRows = JsonRows(output);
            Assert(Equivalent(Object(savedRows[1 + 1]), Object(rows[1])), "Merge lost an unmatched station's JSON fields.");
            var savedFirst = Object(savedRows[0]);
            Assert(Equivalent(savedFirst["point"], Object(rows[0])["point"]), "Merge lost original center and offsets.");
            Assert(Equivalent(savedFirst["location"], Object(rows[0])["location"]), "Merge lost coordinate precision.");
            Invoke(form, "UndoLastEdit");
            Assert(((List<ObservationPoint>)Field(form, "_points")).Count == 2 && official.Point == null,
                "Cannot undo the complete merge.");
            var noPixel = Path.Combine(tempDir, "merge-no-pixel.json");
            var firstRow = new Dictionary<string, object>(Object(rows[0]));
            firstRow["point"] = null;
            File.WriteAllText(noPixel, new JavaScriptSerializer().Serialize(new[] { firstRow }));
            official.Point = new Point2(10, 20);
            Invoke(form, "MergePointFile", noPixel);
            Assert(official.Point == new Point2(10, 20), "An absent incoming coordinate cleared an existing one.");
            var bad = Path.Combine(tempDir, "merge-conflict.json");
            firstRow["type"] = official.Type == ObservationPointType.K_NET ? "kiK_net" : "k_NET";
            File.WriteAllText(bad, new JavaScriptSerializer().Serialize(new[] { firstRow }));
            try { Invoke(form, "MergePointFile", bad); throw new Exception("Conflicting merge succeeded."); }
            catch (TargetInvocationException) { }
            Assert(((List<ObservationPoint>)Field(form, "_points")).Count == 2 && official.Point == new Point2(10, 20),
                "Invalid merge partially changed existing data.");
        }
    }
}
