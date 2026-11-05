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
		private void InitializePointMerge()
		{
			var merge = new ToolStripMenuItem("合并站点文件（补充图像坐标）...");
			merge.Click += (sender, args) =>
			{
				using (var dialog = new OpenFileDialog
				{
					Title = "选择要合并的站点文件（保留当前站点信息）",
					Filter = "点位文件|*.json;*.mpk;*.mpk.lz4;*.csv",
					RestoreDirectory = true
				})
				{
					if (dialog.ShowDialog(this) != DialogResult.OK)
						return;
					try
					{
						MessageBox.Show(this, MergePointFile(dialog.FileName), "合并完成",
							MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					catch (Exception ex)
					{
						MessageBox.Show(this, "合并失败，原数据保留。\n" + ex.GetBaseException().Message,
							"合并失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			};
			importToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
			importToolStripMenuItem.DropDownItems.Add(merge);
		}

		private string MergePointFile(string path)
		{
			ObservationPointJsonDocument incomingDocument;
			string report;
			var incoming = ReadPointFile(path, out incomingDocument, out report, true);
			_points = _points ?? new List<ObservationPoint>();
			var existing = _points.ToDictionary(point => point.Code, StringComparer.Ordinal);
			foreach (var source in incoming)
			{
				ObservationPoint target;
				if (existing.TryGetValue(source.Code, out target) && target.Type != source.Type)
					throw new InvalidDataException($"观测点 {source.Code} 的网络类型不一致。");
			}
			var restore = CapturePointState();
			var previousDocument = _jsonDocument;
			var mapping = new List<KeyValuePair<ObservationPoint, ObservationPoint>>();
			var added = 0;
			var updated = 0;
			try
			{
				foreach (var source in incoming)
				{
					ObservationPoint target;
					if (!existing.TryGetValue(source.Code, out target))
					{
						target = source;
						_points.Add(target);
						existing.Add(target.Code, target);
						added++;
					}
					else
					{
						if (source.Point.HasValue && target.Point != source.Point)
						{
							target.Point = source.Point;
							updated++;
						}
						target.ClassificationId = target.ClassificationId ?? source.ClassificationId;
						target.PrefectureClassificationId = target.PrefectureClassificationId ?? source.PrefectureClassificationId;
					}
					mapping.Add(new KeyValuePair<ObservationPoint, ObservationPoint>(source, target));
				}
				ObservationPointJsonDocument.ValidatePoints(_points);
				_jsonDocument = ObservationPointJsonDocument.MergeMetadata(previousDocument, incomingDocument, mapping, _points);
				UpdateListValue();
			}
			catch
			{
				restore();
				_jsonDocument = previousDocument;
				UpdateListValue();
				throw;
			}
			if (incoming.Count > 0)
				RecordUndo("合并站点文件", () => { restore(); _jsonDocument = previousDocument; });
			return $"新增：{added} 条\n补充或更新图像坐标：{updated} 条\n"
				+ "同代码站点保留当前名称、地区、经纬度和停用状态；传入的空坐标不会清除已有坐标。\n"
				+ "合并可撤销，请另存为 JSON 保存结果。";
		}
	}
}
