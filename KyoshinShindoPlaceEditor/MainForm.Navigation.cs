using KyoshinMonitorLib;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public partial class MainForm
	{
		private ToolStripMenuItem _syncMapMenu;

		private void InitializeMapNavigation()
		{
			var view = new ToolStripMenuItem("视图(&V)");
			_syncMapMenu = new ToolStripMenuItem("选点时同步定位上下地图") { CheckOnClick = true, Checked = true };
			_syncMapMenu.CheckedChanged += (sender, args) => { if (_syncMapMenu.Checked) CenterSelectedStation(); };
			var locate = new ToolStripMenuItem("定位当前站点") { ShortcutKeys = Keys.Control | Keys.L };
			locate.Click += (sender, args) => CenterSelectedStation();
			view.DropDownItems.AddRange(new ToolStripItem[] { _syncMapMenu, locate });
			menuStrip1.Items.Insert(2, view);
			interpolatedPictureBox1.Paint += DrawGeographicStations;
			interpolatedPictureBox1.MouseClick += GeographicMapMouseClick;
			splitContainer2.Panel1.Resize += (sender, args) => { if (_syncMapMenu.Checked) CenterSelectedStation(); };
			splitContainer2.Panel2.Resize += (sender, args) => { if (_syncMapMenu.Checked) CenterSelectedStation(); };
		}

		private void CenterSelectedStation()
		{
			var selected = SelectedPoint();
			if (selected == null)
				return;
			if (selected.Point.HasValue)
			{
				var pixel = selected.Point.Value;
				var zoom = (int)MonitorZoom;
				interpolatedPictureBox3.Location = new Point(
					splitContainer2.Panel1.ClientSize.Width / 2 - pixel.X * zoom - zoom / 2,
					splitContainer2.Panel1.ClientSize.Height / 2 - pixel.Y * zoom - zoom / 2);
			}
			UpdateGeographicMarker(selected, true);
			RefreshSelectionHighlight();
		}

		private void UpdateGeographicMarker(ObservationPoint selected, bool center)
		{
			if (selected?.Location == null)
			{
				if (_mapPoint != null) _mapPoint.Visible = false;
				return;
			}
			var pixel = MercatorProjection.LatLngToPixel(selected.Location, 5) - _mapLeftUpMapPosition;
			if (center)
				interpolatedPictureBox1.Location = new Point(
					splitContainer2.Panel2.ClientSize.Width / 2 - pixel.X,
					splitContainer2.Panel2.ClientSize.Height / 2 - pixel.Y);
			if (_mapPoint == null)
			{
				_mapPoint = new Panel { BackColor = Color.Red, Size = new Size(7, 7), BorderStyle = BorderStyle.FixedSingle };
				splitContainer2.Panel2.Controls.Add(_mapPoint);
			}
			_mapPoint.Location = (Point)(pixel + interpolatedPictureBox1.Location) - new Size(3, 3);
			_mapPoint.Visible = true;
			_mapPoint.BringToFront();
		}

		private void DrawGeographicStations(object sender, PaintEventArgs args)
		{
			if (_points == null)
				return;
			foreach (var point in _points)
			{
				if (point.Location == null)
					continue;
				var pixel = MercatorProjection.LatLngToPixel(point.Location, 5) - _mapLeftUpMapPosition;
				var brush = point.IsSuspended ? Brushes.Gray : point.Point.HasValue ? Brushes.SeaGreen : Brushes.RoyalBlue;
				args.Graphics.FillRectangle(brush, pixel.X - 1, pixel.Y - 1, 3, 3);
			}
		}

		private void GeographicMapMouseClick(object sender, MouseEventArgs args)
		{
			if (_points == null || args.Button != MouseButtons.Left && args.Button != MouseButtons.Middle)
				return;
			ObservationPoint nearest = null;
			long distance = 8 * 8 + 1;
			foreach (var point in _points)
			{
				if (point.Location == null)
					continue;
				var pixel = MercatorProjection.LatLngToPixel(point.Location, 5) - _mapLeftUpMapPosition;
				var dx = (long)pixel.X - args.X;
				var dy = (long)pixel.Y - args.Y;
				var squared = dx * dx + dy * dy;
				if (squared < distance)
				{
					nearest = point;
					distance = squared;
				}
			}
			if (nearest != null)
				SelectStation(nearest);
		}

		private void SelectStation(ObservationPoint point)
		{
			var item = listView1.Items.Cast<ListViewItem>().FirstOrDefault(value => ReferenceEquals(value.Tag, point));
			if (item == null)
				return;
			_updatingList = true;
			try
			{
				foreach (var selected in listView1.SelectedItems.Cast<ListViewItem>().ToArray())
					selected.Selected = false;
				item.Selected = true;
				item.Focused = true;
				item.EnsureVisible();
			}
			finally { _updatingList = false; }
			listView1.Focus();
			ListViewSelectedIndexChanged(listView1, EventArgs.Empty);
		}
	}
}
