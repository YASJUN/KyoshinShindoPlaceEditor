using KyoshinMonitorLib;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public sealed partial class ModernEditorForm : Form
	{
		private readonly MainForm _model = new MainForm();
		private readonly WebView2 _browser = new WebView2 { Dock = DockStyle.Fill };
		private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
		private readonly SemaphoreSlim _commands = new SemaphoreSlim(1, 1);
		private Func<ObservationPointType, byte[]> _downloadNied = NiedCsvFile.Download;
		private bool _dirty;
		private bool _testMode;
		private const string PointFilter = "点位文件|*.json;*.mpk;*.mpk.lz4;*.csv";
		public TaskCompletionSource<bool> Ready { get; } = new TaskCompletionSource<bool>();

		public ModernEditorForm(bool testMode = false)
		{
			_testMode = testMode;
			Text = "强震监测点位编辑器 · WebView2 + Canvas";
			ClientSize = new System.Drawing.Size(1280, 860);
			MinimumSize = new System.Drawing.Size(900, 650);
			Icon = _model.Icon;
			_model.InitializeModernModel();
			Controls.Add(_browser);
			Shown += async (sender, args) => await InitializeBrowserAsync();
			FormClosing += (sender, args) =>
			{
				if (!_testMode && !ConfirmDiscard()) args.Cancel = true;
			};
		}

		private bool ConfirmDiscard()
			=> !(_dirty || _model.ModernDraftCount > 0) || MessageBox.Show(this,
				"有尚未保存的修改或待核对草稿。\n待核对草稿仅保留在内存中，保存不会写入草稿坐标。"
				+ $"\n当前还有 {_model.ModernDraftCount} 个待核对草稿。确定放弃并继续吗？", "未保存的修改 / 草稿",
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

		private async Task InitializeBrowserAsync()
		{
			try
			{
				var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"KyoshinShindoPlaceEditor", _testMode ? "WebView2-Tests" : "WebView2");
				var environment = await CoreWebView2Environment.CreateAsync(null, profile);
				await _browser.EnsureCoreWebView2Async(environment);
				var core = _browser.CoreWebView2;
				core.SetVirtualHostNameToFolderMapping("editor.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modern"),
					CoreWebView2HostResourceAccessKind.DenyCors);
				core.NavigationStarting += (sender, args) =>
				{
					Uri uri;
					if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "editor.local")
						args.Cancel = true;
				};
				core.NewWindowRequested += (sender, args) => args.Handled = true;
				core.PermissionRequested += (sender, args) => args.State = CoreWebView2PermissionState.Deny;
				core.WebMessageReceived += ReceiveCommand;
				core.Navigate("https://editor.local/index.html");
			}
			catch (Exception ex)
			{
				Ready.TrySetException(ex);
				if (!_testMode)
					MessageBox.Show(this, "现代界面初始化失败。\n" + ex.GetBaseException().Message
						+ "\n\n需要 Microsoft Edge WebView2 Runtime。可用 --legacy 参数启动经典界面。",
						"启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void Post(object message)
		{
			if (!IsDisposed && !Disposing && _browser.CoreWebView2 != null)
				_browser.CoreWebView2.PostWebMessageAsJson(_json.Serialize(message));
		}

		private async void ReceiveCommand(object sender, CoreWebView2WebMessageReceivedEventArgs args)
		{
			if (!args.Source.StartsWith("https://editor.local/", StringComparison.Ordinal)) return;
			Dictionary<string, object> command;
			int id = 0;
			try
			{
				var raw = args.WebMessageAsJson;
				if (raw.Length > 512 * 1024) throw new InvalidDataException("请求过长。");
				command = _json.Deserialize<Dictionary<string, object>>(raw);
				if (raw.Length > 8192 && (string)command["action"] != "fillDrafts")
					throw new InvalidDataException("请求过长。");
				id = Convert.ToInt32(command["id"]);
			}
			catch { return; }
			await _commands.WaitAsync();
			try
			{
				var action = (string)command["action"];
				var changedCode = action == "undo" ? _model.ModernUndoPointCode()
					: action == "edit" || action == "confirmDraft" || action == "clear" || action == "suspend" ? (string)command["code"] : null;
				var notice = await ExecuteCommandAsync(action, command);
				Post(new { id, state = _model.ModernState(changedCode), dirty = _dirty, notice, testing = _testMode });
				if (action == "ready")
				{
					PostInitialImages();
					Ready.TrySetResult(true);
					if (!_testMode) _ = RefreshMonitorAsync("surface");
				}
			}
			catch (Exception ex)
			{
				Post(new { id, error = ex.GetBaseException().Message });
			}
			finally { _commands.Release(); }
		}

		private async Task<string> ExecuteCommandAsync(string action, Dictionary<string, object> command)
		{
			if (action == "ready")
			{
				var automatic = Properties.Settings.Default.AutoLoadFilePath;
				if (!_testMode && File.Exists(automatic))
				{
					_model.ModernLoad(automatic);
					_currentSavePath = automatic;
				}
				return null;
			}
			if (action == "compare")
			{
				if (_comparison == null || _comparison.IsDisposed)
				{
					_comparison = new ImageComparisonForm(() => _model.ModernState(), NavigateFromComparisonAsync);
					_comparison.FormClosed += (sender, args) => Post(new { type = "comparisonAvailability", open = false });
					_comparison.Show();
				}
				else { _comparison.WindowState = FormWindowState.Normal; _comparison.Activate(); }
				Post(new { type = "comparisonAvailability", open = true });
				return null;
			}
			if (action == "sendComparison")
			{
				if (_comparison == null || _comparison.IsDisposed) throw new InvalidOperationException("请先打开图像对比窗口。");
				var code = (string)command["code"];
				var pixel = _model.ModernComparisonPixel(code) ?? new Point2(Integer(command, "x"), Integer(command, "y"));
				var scale = Integer(command, "scale");
				if (!ObservationPointJsonDocument.IsInsideImage(pixel) || scale < 1 || scale > 10)
					throw new InvalidOperationException("当前站点没有可用于对比的有效图像坐标。");
				_comparison.FocusCoordinate(_model.ModernState(), code, pixel.X, pixel.Y, scale);
				return "已将当前坐标发送到对比窗口，仅定位，不修改标注。";
			}
			if (action == "open" || action == "merge")
			{
				if (action == "open" && !ConfirmDiscard()) return null;
				using (var dialog = new OpenFileDialog { Title = action == "open" ? "打开点位文件" : "合并站点文件（保留当前信息）",
					Filter = PointFilter, RestoreDirectory = true })
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return null;
					var result = action == "open" ? _model.ModernLoad(dialog.FileName) : _model.ModernMerge(dialog.FileName);
					if (action == "open") _currentSavePath = dialog.FileName;
					_dirty = action == "merge";
					return result ?? "已打开 " + Path.GetFileName(dialog.FileName);
				}
			}
			if (action == "importNied")
			{
				using (var dialog = new OpenFileDialog { Title = "选择 NIED K-NET / KiK-net 原始 CSV（可多选）",
					Filter = "NIED CSV|*.csv", Multiselect = true, RestoreDirectory = true,
					InitialDirectory = AppDomain.CurrentDomain.BaseDirectory })
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return null;
					var result = _model.ModernImportNied(dialog.FileNames);
					_dirty = true;
					return result;
				}
			}
			if (action == "updateNied")
			{
				var incoming = await Task.Run(() => NiedCsvFile.DownloadLatest(_downloadNied));
				if (IsDisposed) return null;
				bool changed;
				var report = _model.ModernUpdateNied(incoming, out changed);
				_dirty |= changed;
				return "NIED 最新数据增量更新完成\n" + report;
			}
			if (action == "save" || action == "saveAs")
			{
				var path = _currentSavePath;
				if (action == "saveAs" || string.IsNullOrEmpty(path))
					using (var dialog = new SaveFileDialog { Title = "保存点位文件", FileName = path ?? "intensity-points.json",
						Filter = "JSON|*.json|MessagePack|*.mpk|LZ4 MessagePack|*.mpk.lz4|编辑器 CSV|*.csv",
						RestoreDirectory = true, OverwritePrompt = true })
					{
						if (dialog.ShowDialog(this) != DialogResult.OK) return null;
						path = dialog.FileName;
					}
				_model.ModernSave(path);
				_currentSavePath = path;
				_dirty = false;
				return "已保存 " + Path.GetFileName(path) + (_model.ModernDraftCount > 0
					? $"（{_model.ModernDraftCount} 个待核对草稿未写入，仍保留在内存中）" : "");
			}
			if (action == "fillDrafts")
			{
				var candidates = new Dictionary<string, Point2>();
				var rows = command["candidates"] as System.Collections.IList;
				if (rows == null || rows.Count > 20000) throw new InvalidDataException("草稿列表无效。");
				foreach (var value in rows)
				{
					var row = value as Dictionary<string, object>;
					if (row == null) throw new InvalidDataException("草稿记录无效。");
					candidates.Add((string)row["code"], new Point2(Integer(row, "x"), Integer(row, "y")));
				}
				_model.ModernFillDrafts(candidates);
				return $"当前有 {_model.ModernDraftCount} 个待核对草稿，确认前不会写入文件。"
					+ "\n无法换算或超出底图范围的站点保持未设置；已有标注和草稿保持不变。";
			}
			if (action == "edit" && _model.ModernHasDraft((string)command["code"]))
			{
				_model.ModernEdit((string)command["code"], Integer(command, "x"), Integer(command, "y"));
				return null;
			}
			if (action == "clear")
			{
				var draft = _model.ModernHasDraft((string)command["code"]);
				if (_model.ModernClear((string)command["code"], this) && !draft) _dirty = true;
				return null;
			}
			if (action == "edit")
				_model.ModernEdit((string)command["code"], Integer(command, "x"), Integer(command, "y"));
			else if (action == "confirmDraft") _model.ModernConfirmDraft((string)command["code"]);
			else if (action == "suspend") _model.ModernSuspend((string)command["code"]);
			else if (action == "undo") _model.ModernUndo();
			else if (action == "sort") _model.ModernSort();
			else if (action == "refresh") { await RefreshMonitorAsync((string)command["source"]); return null; }
			else if (action == "download") { await DownloadNiedAsync((string)command["network"]); return null; }
			else if (action == "official") { Process.Start(new ProcessStartInfo(NiedCsvFile.StationListUrl) { UseShellExecute = true }); return null; }
			else throw new InvalidDataException("不支持的操作。");
			_dirty = true;
			return null;
		}

		private string _currentSavePath;
		private ImageComparisonForm _comparison;

		private async Task NavigateFromComparisonAsync(string documentId, string code, double x, double y, double scale)
		{
			await _commands.WaitAsync();
			try
			{
				if (IsDisposed) throw new InvalidOperationException("主窗口已关闭。");
				if (documentId != _model.ModernDocumentId)
					throw new InvalidOperationException("主窗口已打开其他文件，请先同步编辑器点位。");
				if (code != null) _model.ModernSelect(code);
				if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
				Post(new { type = "comparisonNavigate", state = _model.ModernState(), dirty = _dirty, code, x, y, scale });
				Activate();
			}
			finally { _commands.Release(); }
		}

		private static int Integer(Dictionary<string, object> command, string name)
		{
			object value;
			if (!command.TryGetValue(name, out value) || !(value is int))
				throw new InvalidDataException("图像坐标必须是整数。");
			return (int)value;
		}

		internal void LoadForTest(string path)
		{
			_model.ModernLoad(path);
			_currentSavePath = path;
			_dirty = false;
			Post(new { type = "state", state = _model.ModernState(), dirty = false });
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_comparison?.Dispose();
				_browser.Dispose();
				_model.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
