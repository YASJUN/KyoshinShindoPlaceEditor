using KyoshinMonitorLib;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace KyoshinShindoPlaceEditor
{
	internal static class NiedCsvFile
	{
		public const string StationListUrl = "https://www.kyoshin.bosai.go.jp/ja/stationlist/";
		private const string DownloadUrl = StationListUrl + "download/stationlist/";

		public static byte[] Download(ObservationPointType type)
		{
			if (type != ObservationPointType.K_NET && type != ObservationPointType.KiK_net)
				throw new ArgumentException("只支持 K-NET 或 KiK-net。", nameof(type));
			// 发行版配置兼容旧框架，HTTPS 下载需要显式启用 TLS 1.2。
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			var cookies = new CookieContainer();
			string html;
			var pageRequest = CreateRequest(StationListUrl, cookies);
			using (var response = pageRequest.GetResponse())
			using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
				html = reader.ReadToEnd();
			var token = Regex.Match(html, "name=\"csrfmiddlewaretoken\"\\s+value=\"([^\"]+)\"").Groups[1].Value;
			if (string.IsNullOrEmpty(token))
				throw new InvalidDataException("NIED 下载页面结构已改变，请通过菜单打开官方页面下载。");

			var request = CreateRequest(DownloadUrl, cookies);
			request.Method = "POST";
			request.Referer = StationListUrl;
			request.ContentType = "application/x-www-form-urlencoded";
			var body = Encoding.UTF8.GetBytes("csrfmiddlewaretoken=" + Uri.EscapeDataString(token)
				+ "&datakind=" + (type == ObservationPointType.K_NET ? "1" : "2"));
			request.ContentLength = body.Length;
			using (var stream = request.GetRequestStream())
				stream.Write(body, 0, body.Length);
			using (var response = request.GetResponse())
			using (var stream = response.GetResponseStream())
			using (var buffer = new MemoryStream())
			{
				if (!response.ContentType.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("NIED 未返回 CSV 文件，请稍后重试或使用官方页面。");
				var block = new byte[8192];
				int count;
				while ((count = stream.Read(block, 0, block.Length)) > 0)
				{
					if (buffer.Length + count > 4 * 1024 * 1024)
						throw new InvalidDataException("下载文件超出预期大小。");
					buffer.Write(block, 0, count);
				}
				var bytes = buffer.ToArray();
				using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.GetEncoding(932), true))
					ReadRecords(reader, type);
				return bytes;
			}
		}

		private static HttpWebRequest CreateRequest(string url, CookieContainer cookies)
		{
			var request = (HttpWebRequest)WebRequest.Create(url);
			request.CookieContainer = cookies;
			request.Timeout = 20000;
			request.ReadWriteTimeout = 20000;
			return request;
		}

		public static List<ObservationPoint> Read(string path, ObservationPointType expectedType)
		{
			using (var reader = new StreamReader(path, Encoding.GetEncoding(932), true))
				return ReadRecords(reader, expectedType);
		}

		private static List<ObservationPoint> ReadRecords(TextReader reader, ObservationPointType expectedType)
		{
			var points = new List<ObservationPoint>();
			using (var parser = new TextFieldParser(reader))
			{
				parser.SetDelimiters(",");
				parser.HasFieldsEnclosedInQuotes = true;
				while (!parser.EndOfData)
				{
					var lineNumber = parser.LineNumber;
					try
					{
						var fields = parser.ReadFields();
						var modern = fields[0] == "K-NET" || fields[0] == "KiK-net";
						if (modern && fields.Length < 11 || !modern && fields.Length < 14)
							throw new InvalidDataException("需要 NIED 新版 11 列或旧版 14 列 CSV。");
						var type = modern ? (fields[0] == "K-NET" ? ObservationPointType.K_NET : ObservationPointType.KiK_net) : expectedType;
						if (type != expectedType)
							throw new InvalidDataException("CSV 中的观测网络与所选文件不一致。");
						var note = fields[modern ? 10 : 13];
						var point = new ObservationPoint
						{
							Type = type,
							Code = fields[modern ? 1 : 0],
							Name = fields[modern ? 2 : 1],
							Region = fields[modern ? 8 : 7],
							IsSuspended = note.Contains("休止") || note.Equals("suspension", StringComparison.OrdinalIgnoreCase),
							Location = new Location(ParseCoordinate(fields[modern ? 4 : 3]), ParseCoordinate(fields[modern ? 5 : 4]))
						};
						if (!modern && !string.IsNullOrWhiteSpace(fields[9]) && !string.IsNullOrWhiteSpace(fields[10]))
							point.OldLocation = new Location(ParseCoordinate(fields[9]), ParseCoordinate(fields[10]));
						points.Add(point);
					}
					catch (Exception ex)
					{
						throw new InvalidDataException($"NIED CSV 第 {lineNumber} 行无效：{ex.Message}", ex);
					}
				}
			}
			if (points.Count == 0)
				throw new InvalidDataException("NIED CSV 不包含观测点。");
			ObservationPointJsonDocument.ValidatePoints(points);
			return points;
		}

		private static float ParseCoordinate(string value)
			=> float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
	}
}
