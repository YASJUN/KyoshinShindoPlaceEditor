using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
    internal sealed class ImageComparisonForm : Form
    {
        private readonly WebView2 _browser = new WebView2 { Dock = DockStyle.Fill };
        private readonly Func<object> _snapshot;
        private readonly Func<string, string, double, double, double, Task> _navigate;
        private Func<string, Task<MonitorFrame>> _download = MonitorImageClient.DownloadAsync;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private bool _ready;
        private object _pendingFocus;

        internal void FocusCoordinate(object state, string code, int x, int y, int scale)
        {
            var message = new { type = "focusFromEditor", state, code, x, y, scale };
            if (_ready) Post(message);
            else _pendingFocus = message;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        internal ImageComparisonForm(Func<object> snapshot, Func<string, string, double, double, double, Task> navigate)
        {
            _snapshot = snapshot;
            _navigate = navigate;
            Text = "点位与监测图像对比";
            ClientSize = new System.Drawing.Size(1240, 820);
            MinimumSize = new System.Drawing.Size(900, 650);
            Controls.Add(_browser);
            Shown += async (sender, args) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KyoshinShindoPlaceEditor", "WebView2-Comparison");
                await _browser.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, profile));
                if (IsDisposed) return;
                var core = _browser.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("editor.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modern"),
                    CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (sender, args) => {
                    if (args.Uri != "https://editor.local/comparison.html") args.Cancel = true;
                };
                core.NewWindowRequested += (sender, args) => args.Handled = true;
                core.PermissionRequested += (sender, args) => args.State = CoreWebView2PermissionState.Deny;
                core.WebMessageReceived += Receive;
                core.Navigate("https://editor.local/comparison.html");
            }
            catch (Exception ex)
            {
                if (!IsDisposed) MessageBox.Show(this, "对比窗口初始化失败：" + ex.GetBaseException().Message,
                    "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void Receive(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (args.Source != "https://editor.local/comparison.html") return;
            int id = 0;
            try
            {
                if (args.WebMessageAsJson.Length > 4096) return;
                var command = _json.Deserialize<Dictionary<string, object>>(args.WebMessageAsJson);
                id = Convert.ToInt32(command["id"]);
                var action = (string)command["action"];
                MonitorFrame frame = null;
                if (action == "navigate")
                {
                    var x = Convert.ToDouble(command["x"]);
                    var y = Convert.ToDouble(command["y"]);
                    var scale = Convert.ToDouble(command["scale"]);
                    if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)
                        || double.IsNaN(scale) || scale < 1 || scale > 10 || Math.Abs(x) > 100000 || Math.Abs(y) > 100000)
                        throw new InvalidDataException("视图位置无效。");
                    await _navigate((string)command["documentId"], command["code"] as string, x, y, scale);
                    Post(new { id, navigated = true });
                    return;
                }
                if (action == "refresh") frame = await _download((string)command["source"]);
                else if (action == "loadImage")
                {
                    using (var dialog = new OpenFileDialog { Title = "打开监测图层（透明背景，352×400）", Filter = "监测图像|*.gif;*.png", RestoreDirectory = true })
                    {
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                        {
                            if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new InvalidDataException("图像超出 2 MB 限制。");
                            frame = new MonitorFrame { image = MonitorImageClient.Encode(File.ReadAllBytes(dialog.FileName)),
                                stamp = "本地文件：" + Path.GetFileName(dialog.FileName), source = (string)command["source"] };
                        }
                    }
                }
                else if (action != "ready" && action != "snapshot") throw new InvalidDataException("不支持的对比操作。");
                if (!IsDisposed) Post(new { id, state = _snapshot(), frame });
                if (action == "ready")
                {
                    _ready = true;
                    if (_pendingFocus != null) { Post(_pendingFocus); _pendingFocus = null; }
                }
            }
            catch (Exception ex) { Post(new { id, error = ex.GetBaseException().Message }); }
        }

        private void Post(object message)
        {
            if (!IsDisposed && _browser.CoreWebView2 != null) _browser.CoreWebView2.PostWebMessageAsJson(_json.Serialize(message));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _browser.Dispose();
            base.Dispose(disposing);
        }
    }
}
