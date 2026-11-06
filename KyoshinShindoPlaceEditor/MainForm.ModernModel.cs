using KyoshinMonitorLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KyoshinShindoPlaceEditor
{
	public partial class MainForm
	{
		private bool _headless;
		internal string ModernDocumentId { get; private set; } = Guid.NewGuid().ToString("N");
		private ObservationPoint _modernSelection;
		private sealed class DraftPoint
		{
			internal Point2 Center;
			internal Point2 Pixel;
		}
		private readonly Dictionary<string, DraftPoint> _draftPoints = new Dictionary<string, DraftPoint>();
		internal int ModernDraftCount => _points.Count(point => !point.Point.HasValue && _draftPoints.ContainsKey(point.Code));
		internal bool ModernHasDraft(string code)
			=> _draftPoints.ContainsKey(code) && _points.Any(point => point.Code == code && !point.Point.HasValue);
		internal Point2? ModernComparisonPixel(string code)
		{
			var point = _points.FirstOrDefault(item => item.Code == code);
			if (point == null) throw new InvalidDataException("找不到选中的站点。");
			if (point.Point.HasValue) return point.Point;
			DraftPoint draft;
			return _draftPoints.TryGetValue(code, out draft) ? draft.Pixel : (Point2?)null;
		}

		internal void ModernFillDrafts(Dictionary<string, Point2> candidates)
		{
			var additions = _points.Where(point => !point.Point.HasValue && !_draftPoints.ContainsKey(point.Code)
				&& candidates.ContainsKey(point.Code)).ToArray();
			if (additions.Any(point => !ObservationPointJsonDocument.IsInsideImage(candidates[point.Code])))
				throw new InvalidDataException("草稿坐标必须在 352×400 范围内。");
			if (additions.Length == 0) return;
			RecordUndo("批量填充草稿", () => { foreach (var point in additions) _draftPoints.Remove(point.Code); });
			foreach (var point in additions)
				_draftPoints.Add(point.Code, new DraftPoint { Center = candidates[point.Code], Pixel = candidates[point.Code] });
		}

		internal void ModernConfirmDraft(string code)
		{
			ModernSelect(code);
			if (!ModernHasDraft(code)) throw new InvalidDataException("当前站点没有待核对草稿。");
			var point = _modernSelection;
			var draft = _draftPoints[code];
			var restoreMetadata = _jsonDocument?.ConfirmImagePoint(point, draft.Center, draft.Pixel);
			RecordUndo("确认点位", () => {
				point.Point = null;
				restoreMetadata?.Invoke();
				_draftPoints[code] = draft;
			}, point);
			point.Point = draft.Pixel;
			_draftPoints.Remove(code);
		}

		internal void InitializeModernModel()
		{
			_headless = true;
			_points = new List<ObservationPoint>();
		}

		internal object ModernState(string changedCode = null)
		{
			var source = changedCode == null ? _points : _points.Where(point => point.Code == changedCode);
			var rows = source.Select(point =>
				{
					var geo = point.Location == null ? (Point2?)null
						: MercatorProjection.LatLngToPixel(point.Location, 5) - _mapLeftUpMapPosition;
					DraftPoint draft;
					if (point.Point.HasValue || !_draftPoints.TryGetValue(point.Code, out draft)) draft = null;
					return new
					{
						code = point.Code, name = point.Name, region = point.Region,
						type = point.Type == ObservationPointType.K_NET ? "K-NET"
							: point.Type == ObservationPointType.KiK_net ? "KiK-net" : "未知",
						suspended = point.IsSuspended,
						lat = point.Location?.Latitude, lon = point.Location?.Longitude,
						pixel = point.Point.HasValue ? new[] { point.Point.Value.X, point.Point.Value.Y } : null,
						draft = draft == null ? null : new[] { draft.Pixel.X, draft.Pixel.Y },
						draftCenter = draft == null ? null : new[] { draft.Center.X, draft.Center.Y },
						geo = geo.HasValue ? new[] { geo.Value.X, geo.Value.Y } : null
					};
				}).ToArray();
			return new
			{
				points = changedCode == null ? rows : null,
				patch = changedCode == null ? null : rows.FirstOrDefault(),
				selected = _modernSelection?.Code,
				path = _lastFilePath,
				documentId = ModernDocumentId,
				draftCount = ModernDraftCount,
				undoCount = _undoHistory.Count,
				undoLabel = _undoHistory.Count == 0 ? "" : _undoHistory.Last().Label
			};
		}

		internal string ModernLoad(string path)
		{
			string error, report;
			if (!TryLoadFile(path, out error, out report))
				throw new InvalidDataException(error);
			_draftPoints.Clear();
			ModernDocumentId = Guid.NewGuid().ToString("N");
			return report;
		}

		internal string ModernMerge(string path) => MergePointFile(path);

		internal string ModernUpdateNied(List<ObservationPoint> incoming, out bool changed)
		{
			var previous = _undoHistory.LastOrDefault();
			var report = ApplyNiedImport(incoming);
			changed = !ReferenceEquals(previous, _undoHistory.LastOrDefault());
			return report;
		}

		internal string ModernImportNied(string[] paths)
		{
			var incoming = new List<ObservationPoint>();
			foreach (var path in paths)
			{
				// Modern files declare the network; older files use their station codes.
				using (var reader = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, System.Text.Encoding.GetEncoding(932), true))
				{
					reader.SetDelimiters(",");
					reader.HasFieldsEnclosedInQuotes = true;
					var fields = reader.ReadFields();
					if (fields == null || fields.Length == 0) throw new InvalidDataException("NIED CSV 为空。");
					var type = fields[0].Equals("KiK-net", StringComparison.OrdinalIgnoreCase)
						|| System.Text.RegularExpressions.Regex.IsMatch(fields[0], "^[A-Z]{2,4}H\\d{2}$")
						? ObservationPointType.KiK_net : ObservationPointType.K_NET;
					incoming.AddRange(NiedCsvFile.Read(path, type));
				}
			}
			return ApplyNiedImport(incoming);
		}

		internal void ModernSelect(string code)
		{
			_modernSelection = _points.FirstOrDefault(point => point.Code == code);
			if (_modernSelection == null)
				throw new InvalidDataException("找不到选中的站点。");
		}

		internal void ModernEdit(string code, int? x, int? y)
		{
			ModernSelect(code);
			if (x.HasValue != y.HasValue)
				throw new InvalidDataException("需要完整的图像 X / Y 坐标。");
			var pixel = x.HasValue ? new Point2(x.Value, y.Value) : (Point2?)null;
			if (pixel.HasValue && !ObservationPointJsonDocument.IsInsideImage(pixel.Value))
				throw new InvalidDataException("图像坐标必须在 352×400 范围内。");
			if (ModernHasDraft(code) && pixel.HasValue)
			{
				var previous = _draftPoints[code];
				if (previous.Pixel == pixel.Value) return;
				RecordUndo("调整草稿", () => _draftPoints[code] = previous, _modernSelection);
				_draftPoints[code] = new DraftPoint { Center = previous.Center, Pixel = pixel.Value };
				return;
			}
			SetSelectedImagePoint(pixel);
		}

		internal void ModernSuspend(string code)
		{
			ModernSelect(code);
			ToggleSuspendToolStripMenuItem_Click(null, EventArgs.Empty);
		}

		internal bool ModernClear(string code, System.Windows.Forms.IWin32Window owner)
		{
			ModernSelect(code);
			if (ModernHasDraft(code))
			{
				if (System.Windows.Forms.MessageBox.Show(owner,
					$"确定清除以下站点的待核对草稿吗？\n\n{code}  {_modernSelection.Name}"
					+ "\n仅清除内存草稿，保留站点记录和经纬度。可使用 Ctrl+Z 撤销。",
					"清除图像坐标", System.Windows.Forms.MessageBoxButtons.YesNo,
					System.Windows.Forms.MessageBoxIcon.Warning, System.Windows.Forms.MessageBoxDefaultButton.Button2)
					!= System.Windows.Forms.DialogResult.Yes) return false;
				var draft = _draftPoints[code];
				RecordUndo("清除草稿", () => _draftPoints[code] = draft, _modernSelection);
				_draftPoints.Remove(code);
				return true;
			}
			return ClearSelectedImagePoint(owner);
		}

		internal void ModernUndo() => UndoLastEdit();
		internal string ModernUndoPointCode() => _undoHistory.LastOrDefault()?.ChangedPoint?.Code;
		internal void ModernSort() => SortPoints();

		internal string ModernOsmImage()
		{
			using (var buffer = new MemoryStream())
			{
				interpolatedPictureBox1.Image.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
				return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
			}
		}

		internal void ModernSave(string path)
		{
			ObservationPointJsonDocument.ValidatePoints(_points);
			if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				if (_jsonDocument != null) _jsonDocument.Save(path, _points);
				else _points.SaveToJson(path);
			}
			else if (path.EndsWith(".mpk.lz4", StringComparison.OrdinalIgnoreCase)) _points.SaveToMpk(path, true);
			else if (path.EndsWith(".mpk", StringComparison.OrdinalIgnoreCase)) _points.SaveToMpk(path);
			else if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) _points.SaveToCsv(path);
			else throw new InvalidDataException("不支持的保存格式。");
			_lastFilePath = path;
		}
	}
}
