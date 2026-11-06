using KyoshinMonitorLib;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KyoshinShindoPlaceEditor
{
	public sealed partial class ModernEditorForm
	{
		private int _imageSequence;

		private void PostInitialImages()
		{
			var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modern", "base-map.gif");
			Post(new { type = "images", osm = _model.ModernOsmImage(),
				baseMap = File.Exists(path) ? "data:image/gif;base64," + Convert.ToBase64String(File.ReadAllBytes(path)) : null });
		}

		private async Task RefreshMonitorAsync(string source)
		{
			if (source != "surface" && source != "borehole") throw new InvalidDataException("未知图像来源。");
			var sequence = ++_imageSequence;
			try
			{
				var frame = await MonitorImageClient.DownloadAsync(source);
				if (sequence == _imageSequence)
					Post(new { type = "monitorImage", image = frame.image, stamp = frame.stamp });
			}
			catch (Exception ex)
			{
				if (sequence == _imageSequence)
					Post(new { type = "imageError", error = "监测图像暂时不可用：" + ex.GetBaseException().Message });
			}
		}

		private async Task DownloadNiedAsync(string network)
		{
			if (network != "knet" && network != "kiknet") throw new InvalidDataException("未知观测网络。");
			using (var dialog = new SaveFileDialog
			{
				Title = "保存 NIED 官方原始 CSV", Filter = "CSV|*.csv", RestoreDirectory = true,
				FileName = network == "knet" ? "sitepub_knet_sj.csv" : "sitepub_kik_sj.csv",
				InitialDirectory = AppDomain.CurrentDomain.BaseDirectory, OverwritePrompt = true
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				var bytes = await Task.Run(() => _downloadNied(network == "knet"
					? ObservationPointType.K_NET : ObservationPointType.KiK_net));
				var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
				try
				{
					File.WriteAllBytes(temporary, bytes);
					if (File.Exists(dialog.FileName)) File.Replace(temporary, dialog.FileName, null);
					else File.Move(temporary, dialog.FileName);
				}
				finally { if (File.Exists(temporary)) File.Delete(temporary); }
				Post(new { type = "notice", text = "已保存 NIED 原始 CSV：" + dialog.FileName });
			}
		}
	}
}
