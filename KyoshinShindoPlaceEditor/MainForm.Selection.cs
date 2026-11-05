using KyoshinMonitorLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public partial class MainForm
	{
		private Timer _selectionTimer;
		private ToolStripStatusLabel _selectionStatus;
		private bool _selectionBlinkVisible = true;
		private int _selectionConflictCount;
		private ObservationPoint _highlightedPoint;

		private void InitializeSelectionHighlight()
		{
			var status = new StatusStrip();
			_selectionStatus = new ToolStripStatusLabel("未选择观测点")
			{
				Spring = true,
				TextAlign = ContentAlignment.MiddleLeft,
				AutoToolTip = false
			};
			status.Items.Add(_selectionStatus);
			Controls.Add(status);
			status.SendToBack();
			_selectionTimer = new Timer(components) { Interval = 500 };
			_selectionTimer.Tick += SelectionTimerTick;
			interpolatedPictureBox4.Paint += DrawSelectedPointHighlight;
		}

		private void RefreshSelectionHighlight()
		{
			var selected = SelectedPoint();
			_highlightedPoint = selected;
			_selectionBlinkVisible = true;
			_selectionConflictCount = 0;
			if (selected?.Point == null)
			{
				_selectionTimer.Stop();
				_selectionStatus.Text = selected == null ? "未选择观测点" : "选中：" + selected.Code + " | 图像坐标未设置";
				_selectionStatus.ToolTipText = "";
			}
			else
			{
				var pixel = selected.Point.Value;
				var conflicts = _points.Where(point => !ReferenceEquals(point, selected) && point.Point.HasValue
					&& Math.Abs((long)point.Point.Value.X - pixel.X) <= 1
					&& Math.Abs((long)point.Point.Value.Y - pixel.Y) <= 1).ToArray();
				_selectionConflictCount = conflicts.Length;
				_selectionStatus.Text = $"选中：{selected.Code} {selected.Name} | 图像坐标：({pixel.X}, {pixel.Y}) | 3×3 安全圈："
					+ (conflicts.Length == 0 ? "无其他点位" : $"冲突 {conflicts.Length} 个（仅提示）");
				_selectionStatus.ToolTipText = string.Join("\n", conflicts.Select(point => point.Code + " " + point.Name));
				_selectionTimer.Start();
			}
			interpolatedPictureBox4.Invalidate();
		}

		private Rectangle SelectionSafetyBounds()
		{
			var point = _highlightedPoint?.Point;
			if (!point.HasValue)
				return Rectangle.Empty;
			var zoom = (int)MonitorZoom;
			return new Rectangle((point.Value.X - 1) * zoom, (point.Value.Y - 1) * zoom, 3 * zoom, 3 * zoom);
		}

		private void SelectionTimerTick(object sender, EventArgs args)
		{
			_selectionBlinkVisible = !_selectionBlinkVisible;
			interpolatedPictureBox4.Invalidate(SelectionSafetyBounds());
		}

		private void DrawSelectedPointHighlight(object sender, PaintEventArgs args)
		{
			var pixel = _highlightedPoint?.Point;
			if (!pixel.HasValue)
				return;
			var state = args.Graphics.Save();
			try
			{
				args.Graphics.SmoothingMode = SmoothingMode.None;
				args.Graphics.PixelOffsetMode = PixelOffsetMode.None;
				var square = SelectionSafetyBounds();
				using (var pen = new Pen(_selectionConflictCount > 0 ? Color.Red : Color.Cyan))
					args.Graphics.DrawRectangle(pen, square.X, square.Y, square.Width - 1, square.Height - 1);
				if (_selectionBlinkVisible)
				{
					var zoom = (int)MonitorZoom;
					using (var brush = new SolidBrush(Color.Yellow))
						args.Graphics.FillRectangle(brush, pixel.Value.X * zoom, pixel.Value.Y * zoom, zoom, zoom);
				}
			}
			finally
			{
				args.Graphics.Restore(state);
			}
		}
	}
}
