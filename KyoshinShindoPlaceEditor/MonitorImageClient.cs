using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace KyoshinShindoPlaceEditor
{
    internal sealed class MonitorFrame
    {
        public string image { get; set; }
        public string stamp { get; set; }
        public string source { get; set; }
    }

    internal static class MonitorImageClient
    {
        internal static string Encode(byte[] bytes)
        {
            if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("图像超出 2 MB 限制。");
            using (var input = new MemoryStream(bytes))
            using (var image = Image.FromStream(input))
            {
                if (image.Width != 352 || image.Height != 400)
                    throw new InvalidDataException("监测图像必须为 352×400 像素。");
                using (var output = new MemoryStream())
                {
                    image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
                }
            }
        }

        internal static Task<MonitorFrame> DownloadAsync(string source)
        {
            if (source != "surface" && source != "borehole") throw new InvalidDataException("未知图像来源。");
            return Task.Run(() =>
            {
                var stamp = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).AddSeconds(-15).ToString("yyyyMMddHHmmss");
                var kind = source == "surface" ? "jma_s" : "jma_b";
                var request = (HttpWebRequest)WebRequest.Create("http://www.kmoni.bosai.go.jp/data/map_img/RealTimeImg/"
                    + kind + "/" + stamp.Substring(0, 8) + "/" + stamp + "." + kind + ".gif");
                request.Timeout = request.ReadWriteTimeout = 8000;
                using (var response = request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    var block = new byte[8192];
                    int count;
                    while ((count = input.Read(block, 0, block.Length)) > 0)
                    {
                        if (output.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("图像超出预期大小。");
                        output.Write(block, 0, count);
                    }
                    return new MonitorFrame { image = Encode(output.ToArray()), stamp = stamp, source = source };
                }
            });
        }
    }
}
