using KyoshinMonitorLib;
using KyoshinShindoPlaceEditor.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public partial class MainForm : Form
	{
		public MainForm()
		{
			InitializeComponent();
			InitializeEditingFeatures();
			InitializeSelectionHighlight();
			InitializeNiedDownloads();
		}

		private List<ObservationPoint> _points;
		private ObservationPointJsonDocument _jsonDocument;
		private Point2 _mapLeftUpMapPosition = new Point2(13 * 2 * MercatorProjection.TileSize, 5 * 2 * MercatorProjection.TileSize);

		private Point _prevPos;
		private Panel _mapPoint;

		private double _monitorZoom = 1;

		private MonitorSourceType _sourceType;

		private string _lastFilePath = null;

		private double MonitorZoom
		{
			get => _monitorZoom;
			set
			{
				if (value == _monitorZoom)
					return;

				interpolatedPictureBox4.Width = interpolatedPictureBox3.Width = interpolatedPictureBox2.Width = (int)(352 * value);
				interpolatedPictureBox4.Height = interpolatedPictureBox3.Height = interpolatedPictureBox2.Height = (int)(400 * value);
				interpolatedPictureBox3.Location = Point.Empty;

				_monitorZoom = value;
				RefreshSelectionHighlight();
			}
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			_sourceType = MonitorSourceType.SurfaceShindo;

			Text += " - " + Assembly.GetExecutingAssembly().GetName().Version + "（中文增强版）";

			importFromNiedToolStripMenuItem.Click += (s2, e2) => ImportNiedData();
			importFromEqWatchToolStripMenuItem.Click += (s2, e2) => ImportEqWatchData();

			sortToolStripMenuItem.Click += (s2, e2) => SortPoints();

			loadToolStripMenuItem.Click += (s2, e2) =>
			{
				var ofd = new OpenFileDialog()
				{
					FileName = _lastFilePath ?? Properties.Settings.Default.AutoLoadFilePath,
					Filter = "所有支持的文件|*.mpk.lz4;*.mpk;*.json;*.csv|MessagePack(LZ4压缩)(*.mpk.lz4)|*.mpk.lz4|MessagePack(*.mpk)|*.mpk|JSON(*.json)|*.json|CSV(*.csv)|*.csv",
					FilterIndex = 1,
					Title = "请选择要打开的文件",
					RestoreDirectory = true
				};

				if (ofd.ShowDialog() != DialogResult.OK)
					return;

				LoadFile(ofd.FileName);
			};

			saveToolStripMenuItem.Click += (s2, e2) =>
			{
				if (string.IsNullOrWhiteSpace(_lastFilePath) || !File.Exists(_lastFilePath))
					return;
				if (MessageBox.Show("确定要覆盖保存以下文件吗？\n" + _lastFilePath, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
					return;

				SaveFile(_lastFilePath);
			};
			saveAsToolStripMenuItem.Click += (s2, e2) =>
			{
				var sfd = new SaveFileDialog()
				{
					FileName = _lastFilePath ?? Properties.Settings.Default.AutoLoadFilePath,
					Filter = "MessagePack(LZ4压缩)(*.mpk.lz4)|*.mpk.lz4|MessagePack(*.mpk)|*.mpk|ProtocolBuffers(*.pbf)|*.pbf|JSON(*.json)|*.json|CSV(*.csv)|*.csv",
					FilterIndex = _jsonDocument != null ? 4 : 1,
					Title = "请选择保存位置和文件名",
					RestoreDirectory = true
				};
				if (sfd.ShowDialog() != DialogResult.OK)
					return;

				SaveFile(sfd.FileName);
			};

			aboutToolStripMenuItem.Click += (s2, e2) => new AboutForm().ShowDialog();

			surfaceShindoStripMenuItem.Click += (s2, e2) =>
			{
				_sourceType = MonitorSourceType.SurfaceShindo;
				surfaceShindoStripMenuItem.CheckState = CheckState.Indeterminate;
				boreholeShindoStripMenuItem.CheckState = CheckState.Unchecked;
				UpdateImage();
			};
			boreholeShindoStripMenuItem.Click += (s2, e2) =>
			{
				_sourceType = MonitorSourceType.BoreholeShindo;
				surfaceShindoStripMenuItem.CheckState = CheckState.Unchecked;
				boreholeShindoStripMenuItem.CheckState = CheckState.Indeterminate;
				UpdateImage();
			};

			interpolatedPictureBox3.ImageLocation = "http://www.kmoni.bosai.go.jp/data/map_img/CommonImg/base_map_w.gif";

			interpolatedPictureBox4.MouseDown += (s2, e2) =>
			{
				if (e2.Button != MouseButtons.Right)
					return;
				_prevPos = e2.Location;
			};
			interpolatedPictureBox4.MouseMove += (s2, e2) =>
			{
				if (e2.Button != MouseButtons.Right)
					return;
				interpolatedPictureBox3.Location = new Point(
					interpolatedPictureBox3.Location.X + (e2.Location.X - _prevPos.X),
					interpolatedPictureBox3.Location.Y + (e2.Location.Y - _prevPos.Y)
					);
			};
			interpolatedPictureBox1.MouseDown += (s2, e2) =>
			{
				if (e2.Button == MouseButtons.Right)
					_prevPos = e2.Location;
			};
			interpolatedPictureBox1.MouseMove += (s2, e2) =>
			{
				if (e2.Button != MouseButtons.Right)
					return;
				interpolatedPictureBox1.Location = new Point(
					interpolatedPictureBox1.Location.X + (e2.Location.X - _prevPos.X),
					interpolatedPictureBox1.Location.Y + (e2.Location.Y - _prevPos.Y)
					);

				if (_mapPoint == null)
					return;
				_mapPoint.Location = new Point(
					_mapPoint.Location.X + (e2.Location.X - _prevPos.X),
					_mapPoint.Location.Y + (e2.Location.Y - _prevPos.Y)
					);
			};

			interpolatedPictureBox1.Interpolation = InterpolationMode.NearestNeighbor;
			interpolatedPictureBox2.Interpolation = InterpolationMode.NearestNeighbor;
			interpolatedPictureBox3.Interpolation = InterpolationMode.NearestNeighbor;
			interpolatedPictureBox4.Interpolation = InterpolationMode.NearestNeighbor;
			interpolatedPictureBox3.Controls.Add(interpolatedPictureBox2);
			interpolatedPictureBox2.Controls.Add(interpolatedPictureBox4);

			UpdateImage();

			_points = new List<ObservationPoint>();

			if (File.Exists(Settings.Default.AutoLoadFilePath))
			{
				LoadFile(Settings.Default.AutoLoadFilePath);
				return;
			}

			UpdateListValue();
		}

		private void UpdateImage()
		{
			var startTime = DateTime.Now.AddSeconds(-10);
			if (_sourceType == MonitorSourceType.SurfaceShindo)
				interpolatedPictureBox2.ImageLocation = "http://www.kmoni.bosai.go.jp/data/map_img/RealTimeImg/jma_s/" + startTime.ToString("yyyyMMdd") + "/" + startTime.ToString("yyyyMMddHHmmss") + ".jma_s.gif";
			else if (_sourceType == MonitorSourceType.BoreholeShindo)
				interpolatedPictureBox2.ImageLocation = "http://www.kmoni.bosai.go.jp/data/map_img/RealTimeImg/jma_b/" + startTime.ToString("yyyyMMdd") + "/" + startTime.ToString("yyyyMMddHHmmss") + ".jma_b.gif";
		}

		private void UpdateListValue()
		{
			_points = _points ?? new List<ObservationPoint>();

			string oldText = Text;
			bool wasEnabled = Enabled;
			var oldCursor = Cursor;
			Text = "[正在更新数据] " + Text;
			Enabled = false;
			Cursor = Cursors.WaitCursor;

			var lastSelectedCode = SelectedPointCode();

			listView1.BeginUpdate();

			try
			{
				listView1.Items.Clear();
				listView1.Items.AddRange(_points.Select(p => new ListViewItem(new string[] {
					p.Code, p.Type == ObservationPointType.Unknown ? "未知" : p.Type.ToNaturalString(),
					p.IsSuspended ? "是" : "否", p.Name, p.Region,
					p.Location?.ToString() ?? "未设置", p.Point?.ToString() ?? "未设置"
				})).ToArray());

				Bitmap canvas = null;
				if (!checkBox2.Checked)
				{
					canvas = new Bitmap(352, 400);
					try
					{
						foreach (var point in _points.Where(p => p.Point.HasValue && ObservationPointJsonDocument.IsInsideImage(p.Point.Value)))
						{
							Color color = point.Type == ObservationPointType.KiK_net ? Color.Red
								: point.Type == ObservationPointType.K_NET ? Color.Orange : Color.Black;
							if (point.IsSuspended)
								color = Color.Gray;
							canvas.SetPixel(point.Point.Value.X, point.Point.Value.Y, color);
						}
					}
					catch
					{
						canvas.Dispose();
						throw;
					}
				}
				var oldImage = interpolatedPictureBox4.Image;
				interpolatedPictureBox4.Image = canvas;
				oldImage?.Dispose();

				var selectedItem = listView1.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Text == lastSelectedCode);
				if (selectedItem != null)
				{
					selectedItem.Selected = true;
					selectedItem.EnsureVisible();
				}
			}
			finally
			{
				listView1.EndUpdate();
				Enabled = wasEnabled;
				Cursor = oldCursor;
				Text = oldText;
				RefreshSelectionHighlight();
			}
		}

		private void ListViewSelectedIndexChanged(object sender, EventArgs e)
		{
			RefreshSelectionHighlight();
			if (listView1.SelectedItems.Count == 0)
				return;
			var selectedPoint = _points.FirstOrDefault(p => p.Code == listView1.SelectedItems[0].Text);
			if (selectedPoint?.Location == null)
				return;

			System.Diagnostics.Debug.WriteLine(listView1.SelectedItems[0].Text);

			if (_mapPoint != null && splitContainer2.Panel2.Controls.Contains(_mapPoint))
			{
				splitContainer2.Panel2.Controls.Remove(_mapPoint);
				_mapPoint.Dispose();
			}

			_mapPoint = new Panel()
			{
				BackColor = Color.Red,
				Size = new Size(3, 3), //3
				Location = (MercatorProjection.LatLngToPixel(selectedPoint.Location, 5) - _mapLeftUpMapPosition - 1 + interpolatedPictureBox1.Location),
			};

			splitContainer2.Panel2.Controls.Add(_mapPoint);
			splitContainer2.Panel2.Controls.SetChildIndex(_mapPoint, 0);
		}

		#region ZoomChange

		private void KyoshinZoomChange_1(object sender, EventArgs e) => MonitorZoom = 1;

		private void KyoshinZoomChange_2(object sender, EventArgs e) => MonitorZoom = 2;

		private void KyoshinZoomChange_3(object sender, EventArgs e) => MonitorZoom = 3;

		private void KyoshinZoomChange_4(object sender, EventArgs e) => MonitorZoom = 4;

		private void KyoshinZoomChange_5(object sender, EventArgs e) => MonitorZoom = 5;

		#endregion ZoomChange

		private void InterpolatedPictureBox2_MouseDown(object sender, MouseEventArgs e)
		{
			var clientpos = ((Point2)e.Location / (int)Math.Floor(MonitorZoom));
			if (_points == null || !ObservationPointJsonDocument.IsInsideImage(clientpos))
				return;
			System.Diagnostics.Debug.WriteLine(clientpos);

			if (e.Button == MouseButtons.Middle && _points.Any(p => p.Point == clientpos))
			{
				var code = _points.First(p => p.Point == clientpos).Code;
				System.Diagnostics.Debug.WriteLine(_points.First(p => p.Point == clientpos).Code);

				var item = listView1.Items.Cast<ListViewItem>().First(i => i.Text == code);

				foreach (ListViewItem i in listView1.Items)
					i.Selected = false;

				item.Selected = true;
				listView1.Focus();
				listView1.EnsureVisible(item.Index);
				return;
			}

			if (e.Button != MouseButtons.Left)
				return;

			if (listView1.SelectedItems.Count == 0)
				return;

			SetSelectedImagePoint(clientpos);
		}

		private void SaveFile(string path)
		{
			try
			{
				ObservationPointJsonDocument.ValidatePoints(_points);
				if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
					_points.SaveToCsv(path);
				else if (path.EndsWith(".mpk", StringComparison.OrdinalIgnoreCase))
					_points.SaveToMpk(path);
				else if (path.EndsWith(".mpk.lz4", StringComparison.OrdinalIgnoreCase))
					_points.SaveToMpk(path, true);
				else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					if (_jsonDocument != null)
						_jsonDocument.Save(path, _points);
					else
						_points.SaveToJson(path);
				}
				else
				{
					MessageBox.Show("无法识别要保存的文件类型。", "保存失败");
					return;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("保存文件失败。\n" + ex.GetBaseException().Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			_lastFilePath = path;
			saveToolStripMenuItem.Enabled = true;
			UpdateListValue();
		}

		private void LoadFile(string path)
		{
			if (_points != null && _points.Any() && MessageBox.Show("当前已加载的数据将被全部覆盖。确定要打开文件吗？", "确认", MessageBoxButtons.YesNo) == DialogResult.No)
				return;

			string error, report;
			if (!TryLoadFile(path, out error, out report))
				MessageBox.Show("读取文件失败。\n" + error, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			else if (report != null)
				MessageBox.Show(report, "处理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private bool TryLoadFile(string path, out string error, out string report)
		{
			error = report = null;
			var previousPoints = _points;
			var previousJsonDocument = _jsonDocument;
			try
			{
				if (!File.Exists(path))
					throw new FileNotFoundException("找不到文件。", path);

				List<ObservationPoint> loadedPoints;
				ObservationPointJsonDocument jsonDocument = null;
				if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
				{
					var result = ObservationPoint.LoadFromCsv(path);
					if (result.points.Length == 0 && result.error > 0)
						throw new InvalidDataException("CSV 没有成功读取任何观测点，请使用编辑器支持的 CSV 格式。");
					loadedPoints = result.points.ToList();
					report = $"处理报告\n成功：{result.success} 条\n失败：{result.error} 条";
				}
				else if (path.EndsWith(".mpk", StringComparison.OrdinalIgnoreCase))
					loadedPoints = ObservationPoint.LoadFromMpk(path).ToList();
				else if (path.EndsWith(".mpk.lz4", StringComparison.OrdinalIgnoreCase))
					loadedPoints = ObservationPoint.LoadFromMpk(path, true).ToList();
				else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					jsonDocument = ObservationPointJsonDocument.Load(path);
					loadedPoints = jsonDocument.Points;
				}
				else
					throw new InvalidDataException("无法识别要打开的文件类型。");

				ObservationPointJsonDocument.ValidatePoints(loadedPoints);
				_points = loadedPoints;
				_jsonDocument = jsonDocument;
				UpdateListValue();
			}
			catch (Exception ex)
			{
				_points = previousPoints;
				_jsonDocument = previousJsonDocument;
				error = ex.GetBaseException().Message;
				return false;
			}
			_lastFilePath = path;
			saveToolStripMenuItem.Enabled = true;
			_undoHistory.Clear();
			UpdateUndoMenu();
			return true;
		}

		#region Import

		private void ImportEqWatchData()
		{
			if (!File.Exists("Kansokuten.dat"))
			{
				MessageBox.Show("找不到 Kansokuten.dat 文件。", null);
				return;
			}

			var addedCount = 0;
			var addId = 0;
			var replaceCount = 0;
			var errorCount = 0;
			var completionMode = false;

			if (_points.Any() && MessageBox.Show("导入操作将覆盖当前已加载的数据。确定要导入吗？", "确认", MessageBoxButtons.YesNo) == DialogResult.No)
				return;

			if (_points.Any(p => p.Point != null) && MessageBox.Show("是否启用补全模式？\n仅导入尚未设置像素坐标的点位，其他点位只补全其余信息。", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
				completionMode = true;

			var undo = CapturePointState();
			try
			{
				using (var reader = new StreamReader("Kansokuten.dat", Encoding.GetEncoding("Shift-JIS")))
				{
					if (!reader.ReadLine().StartsWith("v5 Kyoshin Monitor Base Data for EqWatch"))
					{
						MessageBox.Show("不支持此版本的 EqWatch 数据文件。", null);
						return;
					}
					while (reader.Peek() >= 0)
					{
						try
						{
							var strings = reader.ReadLine().Split(',');

							var point = _points.FirstOrDefault(p => p.Type == strings[2].ToObservationPointType() && p.Name == strings[3] && (strings[4] == "その他" || p.Region.StartsWith(strings[4])));
							//あるとき!
							if (point != null)
							{
								point.IsSuspended = strings[0] == "0";
								if (!completionMode || point.Point == null)
									point.Point = new Point2(int.Parse(strings[9]) + int.Parse(strings[11]), int.Parse(strings[10]) + int.Parse(strings[12]));
								replaceCount++;
							}
							//ないとき…
							else
							{
								while (_points.Any(p => p.Code == $"_EQW{addId}"))
									addId++;

								point = new ObservationPoint
								{
									Type = strings[2].ToObservationPointType(),
									Code = $"_EQW{addId}",
									IsSuspended = strings[0] == "0",
									Name = strings[3],
									Region = strings[4],
									Location = new Location(float.Parse(strings[8]), float.Parse(strings[7])),
									Point = new Point2(int.Parse(strings[9]) + int.Parse(strings[11]), int.Parse(strings[10]) + int.Parse(strings[12])),
								};
								_points.Add(point);
								addedCount++;
								addId++;
							}
							point.ClassificationId = point.ClassificationId ?? int.Parse(strings[5]);
							point.PrefectureClassificationId = point.PrefectureClassificationId ?? int.Parse(strings[6]);
						}
						catch
						{
							errorCount++;
						}
					}
				}
				MessageBox.Show($"处理报告\n替换：{replaceCount} 条\n新增：{addedCount} 条\n失败：{errorCount} 条", "处理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show("导入 EqWatch 数据失败。\n" + ex, null);
			}
			finally
			{
				if (addedCount + replaceCount > 0)
					RecordUndo("导入 EqWatch 数据", undo);
				UpdateListValue();
			}
		}

		private void ImportNiedData()
		{
			var kikPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sitepub_kik_sj.csv");
			var knetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sitepub_knet_sj.csv");
			if (!File.Exists(kikPath))
			{
				MessageBox.Show("找不到 sitepub_kik_sj.csv 文件。", null);
				return;
			}
			if (!File.Exists(knetPath))
			{
				MessageBox.Show("找不到 sitepub_knet_sj.csv 文件。", null);
				return;
			}

			if (_points.Any() && MessageBox.Show("将添加尚未登记的点位信息。确定要导入吗？", "确认", MessageBoxButtons.YesNo) == DialogResult.No)
				return;

			try
			{
				var incoming = NiedCsvFile.Read(kikPath, ObservationPointType.KiK_net);
				incoming.AddRange(NiedCsvFile.Read(knetPath, ObservationPointType.K_NET));
				MessageBox.Show(ApplyNiedImport(incoming), "处理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show("导入 NIED 观测点数据失败。\n" + ex.GetBaseException().Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		#endregion Import

		private void RefleshKyoshinImage(object sender, EventArgs e) => UpdateImage();

		private void BackgroundMapChanged(object sender, EventArgs e)
		{
			if (checkBox1.Checked)
				interpolatedPictureBox3.ImageLocation = "";
			else
				interpolatedPictureBox3.ImageLocation = "http://www.kmoni.bosai.go.jp/data/map_img/CommonImg/base_map_w.gif";
		}

		private void PointMapChanged(object sender, EventArgs e)
		{
			UpdateListValue();
		}

		private void ContextMenuOpening(object sender, CancelEventArgs e)
		{
			if (listView1.SelectedItems.Count > 0)
				return;
			e.Cancel = true;
		}

		private void ToggleSuspendToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var point = SelectedPoint();
			if (point == null)
				return;
			var previous = point.IsSuspended;
			RecordUndo("切换停用状态", () => point.IsSuspended = previous);
			point.IsSuspended = !previous;
			UpdateListValue();
		}

		private void RemovePointToolStripMenuItem_Click(object sender, EventArgs e)
		{
			SetSelectedImagePoint(null);
		}

		private void ShowMapUsage(object sender, EventArgs e)
		{
			MessageBox.Show(@"操作说明
点击“刷新图像”可获取当前时刻附近的强震监测图像。
先选中右侧列表中的观测点，再在上方监测图像中单击鼠标左键，即可将该点的图像坐标设置为点击位置。
按住鼠标右键拖动可移动图像或地图，请注意不要将其移出可视区域。
在已有点位上单击鼠标中键（滚轮按钮），可在右侧列表中选中该观测点。
编辑后请通过“文件”菜单保存，修改不会自动保存。", "操作说明");
		}
	}
}
