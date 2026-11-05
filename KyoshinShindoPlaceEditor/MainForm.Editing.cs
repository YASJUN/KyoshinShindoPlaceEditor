using KyoshinMonitorLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public partial class MainForm
	{
		private sealed class UndoStep
		{
			public string Label;
			public string SelectionCode;
			public Action Restore;
		}

		private readonly List<UndoStep> _undoHistory = new List<UndoStep>();
		private ToolStripMenuItem _undoMenuItem;

		private void InitializeEditingFeatures()
		{
			var editMenu = new ToolStripMenuItem("编辑(&E)");
			_undoMenuItem = new ToolStripMenuItem("撤销(&U)")
			{
				Enabled = false,
				ShortcutKeys = Keys.Control | Keys.Z
			};
			_undoMenuItem.Click += (sender, args) => UndoLastEdit();
			editMenu.DropDownItems.Add(_undoMenuItem);
			menuStrip1.Items.Insert(1, editMenu);
		}

		private string SelectedPointCode()
			=> listView1.SelectedItems.Count == 0 ? null : listView1.SelectedItems[0].Text;

		private ObservationPoint SelectedPoint()
		{
			var code = SelectedPointCode();
			return _points?.FirstOrDefault(point => point.Code == code);
		}

		private void RecordUndo(string label, Action restore)
		{
			_undoHistory.Add(new UndoStep { Label = label, Restore = restore, SelectionCode = SelectedPointCode() });
			if (_undoHistory.Count > 50)
				_undoHistory.RemoveAt(0);
			UpdateUndoMenu();
		}

		private void UpdateUndoMenu()
		{
			_undoMenuItem.Enabled = _undoHistory.Count > 0;
			_undoMenuItem.Text = _undoHistory.Count == 0 ? "撤销(&U)" : "撤销 " + _undoHistory.Last().Label + "(&U)";
		}

		private void UndoLastEdit()
		{
			if (_undoHistory.Count == 0)
				return;
			var step = _undoHistory.Last();
			step.Restore();
			_undoHistory.RemoveAt(_undoHistory.Count - 1);
			UpdateListValue();
			if (step.SelectionCode != null)
			{
				var item = listView1.Items.Cast<ListViewItem>().FirstOrDefault(value => value.Text == step.SelectionCode);
				if (item != null)
				{
					foreach (ListViewItem selected in listView1.SelectedItems)
						selected.Selected = false;
					item.Selected = true;
					item.EnsureVisible();
				}
			}
			UpdateUndoMenu();
		}

		private void SetSelectedImagePoint(Point2? value)
		{
			var point = SelectedPoint();
			if (point == null || point.Point == value)
				return;
			var previous = point.Point;
			RecordUndo(value.HasValue ? "移动点位" : "清除图像坐标", () => point.Point = previous);
			point.Point = value;
			UpdateListValue();
		}

		private void SortPoints()
		{
			if (_points == null || _points.Count == 0)
				return;
			var previous = _points.ToList();
			_points.Sort();
			if (!previous.SequenceEqual(_points))
				RecordUndo("排序", () => _points = previous);
			UpdateListValue();
		}

		private Action CapturePointState()
		{
			var originals = (_points ?? new List<ObservationPoint>()).ToList();
			var copies = originals.Select(point =>
			{
				var copy = new ObservationPoint();
				CopyPoint(point, copy);
				return copy;
			}).ToArray();
			// 保留原对象身份，撤销后仍能关联新版 JSON 中的附加字段。
			return () =>
			{
				for (int index = 0; index < originals.Count; index++)
					CopyPoint(copies[index], originals[index]);
				_points = originals;
			};
		}

		private static void CopyPoint(ObservationPoint source, ObservationPoint target)
		{
			target.Type = source.Type;
			target.Code = source.Code;
			target.Name = source.Name;
			target.Region = source.Region;
			target.IsSuspended = source.IsSuspended;
			target.Point = source.Point;
			target.Location = source.Location == null ? null : new Location(source.Location.Latitude, source.Location.Longitude);
			target.OldLocation = source.OldLocation == null ? null : new Location(source.OldLocation.Latitude, source.OldLocation.Longitude);
			target.ClassificationId = source.ClassificationId;
			target.PrefectureClassificationId = source.PrefectureClassificationId;
		}

		private string ApplyNiedImport(List<ObservationPoint> incoming)
		{
			ObservationPointJsonDocument.ValidatePoints(incoming);
			var undo = CapturePointState();
			var existing = _points.ToDictionary(point => point.Code, StringComparer.Ordinal);
			var added = 0;
			var updated = 0;
			try
			{
				foreach (var source in incoming)
				{
					ObservationPoint target;
					if (!existing.TryGetValue(source.Code, out target))
					{
						_points.Add(source);
						existing.Add(source.Code, source);
						added++;
						continue;
					}
					if (target.Type != source.Type)
						throw new InvalidDataException($"观测点 {source.Code} 的网络类型不一致。");
					if (target.Name == source.Name && target.Region == source.Region && target.IsSuspended == source.IsSuspended
						&& LocationsEqual(target.Location, source.Location)
						&& (source.OldLocation == null || LocationsEqual(target.OldLocation, source.OldLocation)))
						continue;
					target.Name = source.Name;
					target.Region = source.Region;
					target.Location = source.Location;
					if (source.OldLocation != null)
						target.OldLocation = source.OldLocation;
					target.IsSuspended = source.IsSuspended;
					updated++;
				}
				ObservationPointJsonDocument.ValidatePoints(_points);
			}
			catch
			{
				undo();
				throw;
			}
			if (added + updated > 0)
				RecordUndo("导入 NIED 观测点", undo);
			UpdateListValue();
			return $"新增：{added} 条\n更新：{updated} 条\n已有图像坐标保留不变。";
		}

		private static bool LocationsEqual(Location left, Location right)
			=> left == null || right == null ? left == right
				: left.Latitude == right.Latitude && left.Longitude == right.Longitude;
	}
}
