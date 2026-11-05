using KyoshinMonitorLib;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public partial class MainForm
	{
		private ToolStripMenuItem _downloadNiedMenu;

		private void InitializeNiedDownloads()
		{
			_downloadNiedMenu = new ToolStripMenuItem("NIED 官方下载");
			var knet = new ToolStripMenuItem("下载 K-NET 原始 CSV...");
			var kik = new ToolStripMenuItem("下载 KiK-net 原始 CSV...");
			var website = new ToolStripMenuItem("打开 NIED 官方页面");
			knet.Click += async (sender, args) => await DownloadNiedCsvAsync(ObservationPointType.K_NET);
			kik.Click += async (sender, args) => await DownloadNiedCsvAsync(ObservationPointType.KiK_net);
			website.Click += (sender, args) =>
			{
				try { Process.Start(new ProcessStartInfo(NiedCsvFile.StationListUrl) { UseShellExecute = true }); }
				catch (Exception ex) { MessageBox.Show("无法打开浏览器。\n" + ex.Message, "打开失败"); }
			};
			_downloadNiedMenu.DropDownItems.AddRange(new ToolStripItem[] { knet, kik, website });
			importToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
			importToolStripMenuItem.DropDownItems.Add(_downloadNiedMenu);
		}

		private async Task DownloadNiedCsvAsync(ObservationPointType type)
		{
			using (var dialog = new SaveFileDialog
			{
				Title = "保存 NIED 原始 CSV",
				InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
				FileName = type == ObservationPointType.K_NET ? "sitepub_knet_sj.csv" : "sitepub_kik_sj.csv",
				Filter = "CSV 文件 (*.csv)|*.csv",
				RestoreDirectory = true,
				OverwritePrompt = true
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return;
				_downloadNiedMenu.Enabled = false;
				try
				{
					var bytes = await Task.Run(() => NiedCsvFile.Download(type));
					if (IsDisposed)
						return;
					var path = Path.GetFullPath(dialog.FileName);
					var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
					try
					{
						File.WriteAllBytes(temporary, bytes);
						if (File.Exists(path))
							File.Replace(temporary, path, null);
						else
							File.Move(temporary, path);
					}
					finally
					{
						if (File.Exists(temporary))
							File.Delete(temporary);
					}
					MessageBox.Show(this, "已下载 NIED 官方原始 CSV，未转换文件内容。\n" + path
						+ "\n\n导入时需将两份 CSV 保存在程序目录中。", "下载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				catch (Exception ex)
				{
					if (!IsDisposed)
						MessageBox.Show(this, "下载失败，原有文件未被替换。\n" + ex.GetBaseException().Message, "下载失败",
							MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				finally
				{
					if (!IsDisposed)
						_downloadNiedMenu.Enabled = true;
				}
			}
		}
	}
}
