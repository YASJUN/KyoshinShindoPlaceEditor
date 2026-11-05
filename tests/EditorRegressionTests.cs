using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using KyoshinMonitorLib;
using KyoshinShindoPlaceEditor;

internal static partial class EditorRegressionTests
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
            if (args.Length > 1 && args[1] == "--visible")
            {
                Run("Visible full-data window sustains selection, zoom and edits", VisibleWindowResponsiveness);
                return failures == 0 ? 0 : 1;
            }
            if (args.Length > 1 && args[1] == "--nied-update")
            {
                Run("Latest NIED data incrementally updates an open JSON and rolls back safely", LatestNiedUpdate);
                return failures == 0 ? 0 : 1;
            }
            if (args.Length > 1 && args[1] == "--clear")
            {
                Run("Clearing image coordinates requires confirmation and supports undo", ClearImageCoordinates);
                return failures == 0 ? 0 : 1;
            }
            Run("Invalid render data never leaves the window disabled", RenderRestoresUi);
            Run("Real modern JSON loads all station fields", LoadsModernJson);
            Run("Unedited modern JSON round trip preserves all fields", RoundTripModernJson);
            Run("Edits retain centers, bounded offsets and extra fields", SavesModernEdits);
            Run("Legacy JSON remains readable and writable", LegacyJson);
            Run("Failed load preserves previous document and usable UI", FailedLoadIsTransactional);
            Run("Smaller subsequent files and out-of-image clicks are safe", SmallerFileAndClick);
            Run("Point edits support multiple undo steps without losing JSON metadata", UndoEdits);
            Run("Clearing image coordinates requires confirmation and supports undo", ClearImageCoordinates);
            Run("Modern and legacy NIED CSV records are readable", NiedCsvFormats);
            Run("NIED import can be undone and failed imports preserve data", UndoNiedImport);
            Run("Selection highlight blinks and safety square never blocks editing", SelectionHighlight);
            Run("Official NIED downloads are exposed inside the editor", DownloadMenu);
            Run("Large NIED edits keep selection stable without rebuilding every row", LargeNiedEdits);
            Run("Station files merge coordinates with NIED metadata and support undo", MergeStationFiles);
            Run("Station selection centers both maps and OSM can select stations", SynchronizedMaps);
            Run("Modern model imports and merges without creating native station rows", ModernHeadlessModel);
            Run("Latest NIED data incrementally updates an open JSON and rolls back safely", LatestNiedUpdate);
            Run("Visible full-data window sustains selection, zoom and edits", VisibleWindowResponsiveness);
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
            using (var reply = ReplyToClearDialog(DialogResult.Yes, (text, defaultNo) => { }))
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static Timer ReplyToClearDialog(DialogResult response, Action<string, bool> observed)
    {
        var timer = new Timer { Interval = 50 };
        timer.Tick += (sender, args) =>
        {
            var window = GetActiveWindow();
            var title = new System.Text.StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (title.ToString() != "清除图像坐标") return;
            var message = new System.Text.StringBuilder();
            var child = IntPtr.Zero;
            while ((child = FindWindowEx(window, child, "Static", null)) != IntPtr.Zero)
            {
                var text = new System.Text.StringBuilder(2048);
                GetWindowText(child, text, text.Capacity);
                message.Append(text);
            }
            var defaultId = SendMessage(window, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64() & 0xffff;
            observed(message.ToString(), defaultId == (int)DialogResult.No);
            timer.Stop();
            SendMessage(window, 0x0111, new IntPtr((int)response), IntPtr.Zero);
        };
        timer.Start();
        return timer;
    }

    private static void ClearImageCoordinates()
    {
        using (var form = LoadedForm())
        {
            var points = (List<ObservationPoint>)Field(form, "_points");
            var first = points[0];
            var original = first.Point;
            var observed = false;
            var safeDefault = false;
            string message = null;
            using (var reply = ReplyToClearDialog(DialogResult.No, (text, defaultNo) =>
                { observed = true; message = text; safeDefault = defaultNo; }))
                Invoke(form, "RemovePointToolStripMenuItem_Click", null, EventArgs.Empty);
            Assert(observed, "Coordinates were cleared without a confirmation dialog.");
            Assert(safeDefault && message.Contains(first.Code) && message.Contains(first.Name)
                && message.Contains("站点记录"), "Confirmation lacks station details or defaults to deletion.");
            Assert(first.Point == original && !((ToolStripMenuItem)Field(form, "_undoMenuItem")).Enabled,
                "Cancelling deletion changed coordinates or undo history.");
            using (var reply = ReplyToClearDialog(DialogResult.Yes, (text, defaultNo) => { }))
                Invoke(form, "RemovePointToolStripMenuItem_Click", null, EventArgs.Empty);
            Assert(first.Point == null && points.Count == 1749, "Clear deleted a station or kept its pixel.");
            Invoke(form, "UndoLastEdit");
            var output = Path.Combine(tempDir, "clear-confirmation-undo.json");
            Invoke(form, "SaveFile", output);
            Assert(Equivalent(JsonRows(output), JsonRows(samplePath)), "Undo clear changed JSON fields.");
        }
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
        Func<ObservationPointType, byte[]> download = type =>
        {
            var bytes = (byte[])downloader.GetMethod("Download").Invoke(null, new object[] { type });
            var output = Path.Combine(tempDir, type + ".csv");
            File.WriteAllBytes(output, bytes);
            var records = (List<ObservationPoint>)ReadNied(output, type);
            Assert(records.Count > 0 && records.All(point => point.Type == type), "Official download returned the wrong network.");
            Console.WriteLine("NIED latest " + type + ": " + records.Count + " stations");
            return bytes;
        };
        var incoming = (List<ObservationPoint>)downloader.GetMethod("DownloadLatest").Invoke(null, new object[] { download });
        using (var model = new MainForm())
        {
            Invoke(model, "InitializeModernModel");
            Invoke(model, "ModernLoad", samplePath);
            var previous = ((List<ObservationPoint>)Field(model, "_points")).ToDictionary(point => point.Code, point => point.Point);
            var arguments = new object[] { incoming, false };
            var report = (string)typeof(MainForm).GetMethod("ModernUpdateNied", PrivateInstance).Invoke(model, arguments);
            var current = ((List<ObservationPoint>)Field(model, "_points")).ToDictionary(point => point.Code);
            Assert(previous.All(point => current.ContainsKey(point.Key) && current[point.Key].Point == point.Value),
                "Live incremental update lost a station or image coordinate.");
            Console.WriteLine("NIED live incremental update: " + report.Replace("\n", " | "));
            if ((bool)arguments[1]) Invoke(model, "ModernUndo");
            var output = Path.Combine(tempDir, "live-nied-undo.json");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(output), JsonRows(samplePath)), "Live NIED undo changed original JSON data.");
        }
    }

    private static void LargeNiedEdits()
    {
        using (var form = new MainForm())
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var actualDir = @"D:\KyoshinShindoPlaceEditor.0.0.6.0\NIED-original-20261105";
            List<ObservationPoint> incoming;
            if (Directory.Exists(actualDir))
            {
                incoming = (List<ObservationPoint>)ReadNied(Path.Combine(actualDir, "stationlist_knet.csv"), ObservationPointType.K_NET);
                incoming.AddRange((List<ObservationPoint>)ReadNied(Path.Combine(actualDir, "stationlist_kiknet.csv"), ObservationPointType.KiK_net));
            }
            else
            {
                incoming = Points(ReadDocument(samplePath));
                foreach (var point in incoming) point.Point = null;
            }
            var parseMs = watch.ElapsedMilliseconds;
            SetField(form, "_points", new List<ObservationPoint>());
            var list = (ListView)Field(form, "listView1");
            var handle = list.Handle;
            watch.Restart();
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
            Console.WriteLine("PERF " + incoming.Count + " NIED stations: parse=" + parseMs + "ms import=" + importMs + "ms select=" + selectMs + "ms edits(5)=" + editMs + "ms selectionChanges=" + changes);
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
        using (var form = LoadedForm())
        {
            var row = Object(JsonRows(samplePath)[0]);
            var pixel = Object(row["point"]);
            pixel["center_point"] = new Dictionary<string, object> { ["x"] = 100, ["y"] = 100 };
            pixel["offset"] = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0 };
            row["merge_only"] = "keep incoming metadata";
            var path = Path.Combine(tempDir, "merge-into-modern.json");
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(new[] { row }));
            Invoke(form, "MergePointFile", path);
            var output = Path.Combine(tempDir, "merged-modern-save.json");
            Invoke(form, "SaveFile", output);
            var saved = Object(JsonRows(output)[0]);
            Assert((string)saved["merge_only"] == "keep incoming metadata" && Equivalent(saved["point"], pixel),
                "Merging into an existing JSON document lost incoming image metadata.");
            Assert(Equivalent(saved["location"], Object(JsonRows(samplePath)[0])["location"]), "Merge rounded current JSON coordinates.");
            Invoke(form, "UndoLastEdit");
            Invoke(form, "SaveFile", output);
            Assert(Equivalent(JsonRows(output), JsonRows(samplePath)), "Undo merge did not restore the original JSON metadata.");
        }
    }

    private static void ModernHeadlessModel()
    {
        using (var model = new MainForm())
        {
            Invoke(model, "InitializeModernModel");
            var officialDir = @"D:\KyoshinShindoPlaceEditor.0.0.6.0\NIED-original-20261105";
            var files = new[] { Path.Combine(officialDir, "stationlist_knet.csv"), Path.Combine(officialDir, "stationlist_kiknet.csv") };
            if (!files.All(File.Exists))
            {
                var kik = Path.Combine(tempDir, "kik.csv");
                var knet = Path.Combine(tempDir, "knet.csv");
                File.WriteAllText(kik, "KiK-net,ABSH01,Example,Official one,44.5276,142.8444,0,0,北海道,Hokkaido,operating\n", System.Text.Encoding.GetEncoding(932));
                File.WriteAllText(knet, "K-NET,AIC001,Example,Official two,35,137,0,0,愛知県,Aichi,operating\n", System.Text.Encoding.GetEncoding(932));
                files = new[] { kik, knet };
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Invoke(model, "ModernImportNied", new object[] { files });
            var imported = ((List<ObservationPoint>)Field(model, "_points")).Count;
            Invoke(model, "ModernMerge", samplePath);
            var merged = (List<ObservationPoint>)Field(model, "_points");
            Assert(merged.Count == 1749 && merged.Count(point => point.Point.HasValue) == 1634, "Modern NIED/JSON merge lost stations or pixels.");
            Assert(((ListView)Field(model, "listView1")).Items.Count == 0, "Modern mode still populates the native ListView.");
            Assert(!((ListView)Field(model, "listView1")).IsHandleCreated, "Modern mode created a native station window.");
            Invoke(model, "ModernUndo");
            Assert(((List<ObservationPoint>)Field(model, "_points")).Count == imported
                && ((List<ObservationPoint>)Field(model, "_points")).All(point => !point.Point.HasValue), "Modern merge undo failed.");
            Console.WriteLine("PERF modern NIED import + merge + undo: " + watch.ElapsedMilliseconds + "ms; nativeRows=0");
        }
    }

    private static void LatestNiedUpdate()
    {
        var downloader = typeof(MainForm).Assembly.GetType("KyoshinShindoPlaceEditor.NiedCsvFile");
        var latest = downloader.GetMethod("DownloadLatest");
        Assert(latest != null, "One-step latest NIED download and validation is missing.");
        var encoding = System.Text.Encoding.GetEncoding(932);
        Func<ObservationPointType, byte[]> download = type => encoding.GetBytes(type == ObservationPointType.KiK_net
            ? "KiK-net,ABSH01,Official updated,Example,44.6,142.9,0,0,北海道,Hokkaido,suspension\n"
            : "K-NET,NEW001,New station,Example,35,139,0,0,東京都,Tokyo,operating\n");
        using (var model = new MainForm())
        {
            Invoke(model, "InitializeModernModel");
            Invoke(model, "ModernLoad", samplePath);
            var originalRows = JsonRows(samplePath);
            var original = (List<ObservationPoint>)Field(model, "_points");
            var first = original.Single(point => point.Code == "ABSH01");
            var oldPixel = first.Point;
            var incoming = (List<ObservationPoint>)latest.Invoke(null, new object[] { download });
            Assert(incoming.Count == 2 && incoming.All(point => !point.Point.HasValue), "Downloads fabricated image coordinates.");
            var arguments = new object[] { incoming, false };
            var report = (string)typeof(MainForm).GetMethod("ModernUpdateNied", PrivateInstance).Invoke(model, arguments);
            var updated = (List<ObservationPoint>)Field(model, "_points");
            Assert((bool)arguments[1], "Changed NIED data was reported as unchanged.");
            Assert(updated.Count == 1750 && ReferenceEquals(first, updated.Single(point => point.Code == "ABSH01")), "Incremental update replaced stations.");
            Assert(first.Point == oldPixel && first.IsSuspended && first.Name == "Official updated", "Official metadata or original pixels were lost.");
            Assert(updated.Single(point => point.Code == "NEW001").Point == null, "New stations need manual pixel placement.");
            Assert(report.Contains("新增：1") && report.Contains("更新：1") && report.Contains("保留：1748"), "Update summary does not explain retained stations.");
            var output = Path.Combine(tempDir, "latest-nied.json");
            Invoke(model, "ModernSave", output);
            var updatedRows = JsonRows(output);
            var oldRow = Object(originalRows.Single(row => (string)Object(row)["code"] == "ABSH01"));
            var changedRow = Object(updatedRows.Single(row => (string)Object(row)["code"] == "ABSH01"));
            Assert(Equivalent(oldRow["point"], changedRow["point"]), "Update changed JSON center/offset data.");
            foreach (var key in oldRow.Keys.Except(new[] { "name", "region", "location", "is_suspended" }))
                Assert(Equivalent(oldRow[key], changedRow[key]), "Update lost JSON field: " + key);
            Assert(Equivalent(originalRows[1], updatedRows[1]), "An absent official station was changed.");
            arguments = new object[] { incoming, true };
            typeof(MainForm).GetMethod("ModernUpdateNied", PrivateInstance).Invoke(model, arguments);
            Assert(!(bool)arguments[1], "Identical official data created another change.");
            Invoke(model, "ModernUndo");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(originalRows, JsonRows(output)), "Single undo did not restore the complete original JSON.");

            Func<ObservationPointType, byte[]> failing = type => type == ObservationPointType.K_NET
                ? download(type) : throw new IOException("Second download failed.");
            try { latest.Invoke(null, new object[] { failing }); throw new Exception("Partial download succeeded."); }
            catch (TargetInvocationException) { }
            Func<ObservationPointType, byte[]> invalid = type => type == ObservationPointType.K_NET
                ? download(type) : encoding.GetBytes("not,a,valid,csv\n");
            try { latest.Invoke(null, new object[] { invalid }); throw new Exception("Invalid second network succeeded."); }
            catch (TargetInvocationException) { }
            var conflicting = (List<ObservationPoint>)latest.Invoke(null, new object[] { download });
            conflicting.Add(new ObservationPoint { Code = original[1].Code, Type = ObservationPointType.K_NET,
                Name = "conflicting", Region = "region", Location = new Location(35, 139) });
            try { Invoke(model, "ModernUpdateNied", conflicting, false); throw new Exception("Network conflict succeeded."); }
            catch (TargetInvocationException) { }
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(originalRows, JsonRows(output)), "Failed update partially changed existing data.");
        }
    }

    private static void VisibleWindowResponsiveness()
    {
        Application.EnableVisualStyles();
        using (var form = new ModernEditorForm(true))
        using (var watchdog = new Timer { Interval = 60000 })
        {
            Exception failure = null;
            var completed = false;
            watchdog.Tick += (sender, args) => { failure = new TimeoutException("Modern UI did not finish within 60 seconds."); form.Close(); };
            form.Shown += async (sender, args) =>
            {
                try
                {
                    watchdog.Start();
                    await form.Ready.Task;
                    typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { samplePath });
                    var browser = (Microsoft.Web.WebView2.WinForms.WebView2)typeof(ModernEditorForm)
                        .GetField("_browser", PrivateInstance).GetValue(form);
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        if (await browser.ExecuteScriptAsync("window.editorTest.state.count === 1749") == "true") break;
                        await System.Threading.Tasks.Task.Delay(50);
                    }
                    Assert(await browser.ExecuteScriptAsync("!!document.getElementById('updateNied')") == "true",
                        "The open JSON has no one-step latest NIED update button.");
                    await VerifyImageEstimates(form, browser);
                    await VerifySafetyConflictColors(form, browser);
                    await VerifyImageClickSync(form, browser);
                    var serializer = new JavaScriptSerializer();
                    var evaluation = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                        serializer.Serialize(new { expression = "window.editorTest.benchmark(100)", awaitPromise = true, returnByValue = true }));
                    var result = Object(serializer.DeserializeObject(evaluation));
                    Assert(!result.ContainsKey("exceptionDetails"), "Browser runtime workload threw: " + evaluation);
                    var metrics = Object(Object(result["result"])["value"]);
                    Console.WriteLine("PERF modern visible window: " + serializer.Serialize(metrics));
                    Assert(Convert.ToInt32(metrics["count"]) == 1749, "The browser did not load the full dataset.");
                    Assert(Convert.ToInt32(metrics["renderedRows"]) < 80, "The UI created every station row instead of virtualizing.");
                    Assert(Convert.ToInt32(metrics["colors"]) > 4, "The monitor canvas is blank.");
                    Assert(Convert.ToDouble(metrics["maximum"]) < 200, "Visible interactions exceed the 200ms response budget.");

                    var bridgeResult = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                        serializer.Serialize(new { expression = "(async()=>{await window.editorTest.command('edit',{code:'ABSH01',x:100,y:100});return window.editorTest.state;})()",
                            awaitPromise = true, returnByValue = true }));
                    Assert(!Object(serializer.DeserializeObject(bridgeResult)).ContainsKey("exceptionDetails"), "Edit bridge failed.");
                    var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(form);
                    Assert(((List<ObservationPoint>)Field(model, "_points"))[0].Point == new Point2(100, 100), "Browser edit did not update the model.");
                    await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                        serializer.Serialize(new { expression = "window.editorTest.command('undo')", awaitPromise = true, returnByValue = true }));
                    var output = Path.Combine(tempDir, "modern-ui-undo.json");
                    typeof(MainForm).GetMethod("ModernSave", PrivateInstance).Invoke(model, new object[] { output });
                    Assert(Equivalent(JsonRows(output), JsonRows(samplePath)), "Modern UI edit/undo changed JSON metadata.");
                    await VerifyModernNiedUpdate(form, browser);
                    await VerifyModernClear(form, browser);
                    await browser.ExecuteScriptAsync("window.editorTest.select('ABSH01'); window.editorTest.setZoom(3);");
                    await System.Threading.Tasks.Task.Delay(200);
                    using (var image = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modern-editor.png")))
                        await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                    completed = true;
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    watchdog.Stop();
                    if (!form.IsDisposed) form.Close();
                }
            };
            Application.Run(form);
            if (failure != null) throw failure;
            Assert(completed, "The modern UI scenario did not complete.");
        }
    }

    private static async System.Threading.Tasks.Task VerifyModernNiedUpdate(ModernEditorForm form,
        Microsoft.Web.WebView2.WinForms.WebView2 browser)
    {
        var input = Path.Combine(tempDir, "online-update-input.json");
        File.Copy(samplePath, input);
        typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { input });
        var original = JsonRows(input);
        var calls = new List<ObservationPointType>();
        using (var started = new System.Threading.ManualResetEventSlim())
        using (var release = new System.Threading.ManualResetEventSlim())
        {
            Func<ObservationPointType, byte[]> download = type =>
            {
                calls.Add(type);
                started.Set();
                if (!release.Wait(5000)) throw new TimeoutException("Test download was not released.");
                return System.Text.Encoding.GetEncoding(932).GetBytes(type == ObservationPointType.KiK_net
                    ? "KiK-net,ABSH01,Updated online,Example,44.6,142.9,0,0,北海道,Hokkaido,suspension\n"
                    : "K-NET,NEW001,New online station,Example,35,139,0,0,東京都,Tokyo,operating\n");
            };
            typeof(ModernEditorForm).GetField("_downloadNied", PrivateInstance).SetValue(form, download);
            try
            {
                await browser.ExecuteScriptAsync("window.niedUpdateTest = window.editorTest.command('updateNied');");
                for (var attempt = 0; attempt < 100 && !started.IsSet; attempt++)
                    await System.Threading.Tasks.Task.Delay(20);
                Assert(started.IsSet, "The update button did not start the NIED download.");
                Assert(await browser.ExecuteScriptAsync(
                    "window.editorTest.select('ABSH02'); document.getElementById('updateNied').disabled && window.editorTest.state.selected === 'ABSH02' && window.editorTest.state.count === 1749") == "true",
                    "Selection froze or the document changed before both downloads completed.");
            }
            finally { release.Set(); }
            var serializer = new JavaScriptSerializer();
            var result = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression = "window.niedUpdateTest", awaitPromise = true, returnByValue = true }));
            Assert(!Object(serializer.DeserializeObject(result)).ContainsKey("exceptionDetails"), "Online update bridge failed: " + result);
            var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(form);
            var points = (List<ObservationPoint>)Field(model, "_points");
            Assert(calls.Count == 2 && points.Count == 1750 && points.Single(point => point.Code == "ABSH01").Name == "Updated online",
                "One-step update did not apply both networks.");
            Assert((bool)typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(form), "An update was not marked unsaved.");
            Assert(Equivalent(JsonRows(input), original), "Online update wrote the open JSON without Save.");
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression = "window.editorTest.command('undo')", awaitPromise = true, returnByValue = true }));
            var output = Path.Combine(tempDir, "online-update-undo.json");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(output), original), "Online update undo lost station fields.");
            Func<ObservationPointType, byte[]> failing = type => type == ObservationPointType.K_NET
                ? download(type) : throw new IOException("Second network unavailable.");
            typeof(ModernEditorForm).GetField("_downloadNied", PrivateInstance).SetValue(form, failing);
            result = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression = "window.editorTest.command('updateNied').then(()=>false,()=>true)",
                    awaitPromise = true, returnByValue = true }));
            Assert((bool)Object(Object(serializer.DeserializeObject(result))["result"])["value"], "Failed download was not reported.");
            Invoke(model, "ModernSave", output);
            Assert(Equivalent(JsonRows(output), original), "A failed second download partially updated the document.");
            Assert(await browser.ExecuteScriptAsync("!document.getElementById('updateNied').disabled") == "true",
                "Update button remained disabled after a download failure.");
            typeof(ModernEditorForm).GetMethod("LoadForTest", PrivateInstance).Invoke(form, new object[] { samplePath });
        }
    }

    private static async System.Threading.Tasks.Task VerifyModernClear(ModernEditorForm form,
        Microsoft.Web.WebView2.WinForms.WebView2 browser)
    {
        var model = (MainForm)typeof(ModernEditorForm).GetField("_model", PrivateInstance).GetValue(form);
        var first = ((List<ObservationPoint>)Field(model, "_points"))[0];
        var pixel = first.Point;
        var serializer = new JavaScriptSerializer();
        var observed = false;
        using (var reply = ReplyToClearDialog(DialogResult.No, (text, defaultNo) => { observed = true; }))
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression = "window.editorTest.command('clear',{code:'ABSH01'})",
                    awaitPromise = true, returnByValue = true }));
        Assert(observed && first.Point == pixel, "Browser clear did not honor cancellation.");
        Assert(!(bool)typeof(ModernEditorForm).GetField("_dirty", PrivateInstance).GetValue(form),
            "Cancelled clear marked the document as edited.");
        using (var reply = ReplyToClearDialog(DialogResult.Yes, (text, defaultNo) => { }))
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                serializer.Serialize(new { expression = "window.editorTest.command('clear',{code:'ABSH01'})",
                    awaitPromise = true, returnByValue = true }));
        Assert(first.Point == null && ((List<ObservationPoint>)Field(model, "_points")).Count == 1749,
            "Browser clear removed more than image coordinates.");
        var cleared = Path.Combine(tempDir, "browser-cleared.json");
        Invoke(model, "ModernSave", cleared);
        Assert(Object(JsonRows(cleared)[0])["point"] == null, "Clear did not save as null image coordinates.");
        await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            serializer.Serialize(new { expression = "window.editorTest.command('undo')", awaitPromise = true, returnByValue = true }));
        Invoke(model, "ModernSave", cleared);
        Assert(Equivalent(JsonRows(cleared), JsonRows(samplePath)), "Browser clear undo lost original metadata.");
    }

    private static void SynchronizedMaps()
    {
        using (var form = LoadedForm())
        {
            Assert(typeof(MainForm).GetMethod("CenterSelectedStation", PrivateInstance) != null, "Map synchronization is missing.");
            var list = (ListView)Field(form, "listView1");
            var points = (List<ObservationPoint>)Field(form, "_points");
            var split = (SplitContainer)Field(form, "splitContainer2");
            var image = (PictureBox)Field(form, "interpolatedPictureBox3");
            var map = (PictureBox)Field(form, "interpolatedPictureBox1");
            var sync = (ToolStripMenuItem)Field(form, "_syncMapMenu");
            Assert(sync.Checked, "Synchronized navigation is not enabled by default.");
            Invoke(form, "KyoshinZoomChange_3", null, EventArgs.Empty);
            Invoke(form, "CenterSelectedStation");
            var first = points[0];
            Assert(Math.Abs(image.Left + first.Point.Value.X * 3 + 1 - split.Panel1.ClientSize.Width / 2) <= 1,
                "Selected image point is not centered at the current zoom.");
            var marker = (Panel)Field(form, "_mapPoint");
            Assert(marker.Parent == split.Panel2 && Math.Abs(marker.Left + marker.Width / 2 - split.Panel2.ClientSize.Width / 2) <= 1,
                "Geographic marker is not centered.");
            var other = points[1];
            var origin = (Point2)Field(form, "_mapLeftUpMapPosition");
            var pixel = MercatorProjection.LatLngToPixel(other.Location, 5) - origin;
            Invoke(form, "GeographicMapMouseClick", null, new MouseEventArgs(MouseButtons.Left, 1, pixel.X, pixel.Y, 0));
            Assert(list.SelectedItems.Count == 1 && list.SelectedItems[0].Text == other.Code,
                "Clicking the OSM station did not select its image station.");
            sync.Checked = false;
            var previous = image.Location;
            list.Items[2].Selected = true;
            Invoke(form, "ListViewSelectedIndexChanged", list, EventArgs.Empty);
            Assert(image.Location == previous, "Disabling synchronization still moved the image.");
            points[2].Point = null;
            Invoke(form, "CenterSelectedStation");
            Assert(((ToolStripStatusLabel)Field(form, "_selectionStatus")).Text.Contains("未设置"),
                "A NIED-only station does not explain its missing image coordinates.");
            using (var bitmap = new System.Drawing.Bitmap(1024, 1024))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.White);
                Invoke(form, "DrawGeographicStations", null, new PaintEventArgs(graphics, new System.Drawing.Rectangle(0, 0, 1024, 1024)));
                Assert(bitmap.GetPixel(pixel.X, pixel.Y).ToArgb() != System.Drawing.Color.White.ToArgb(),
                    "OSM station markers are not painted.");
            }
        }
    }
}
