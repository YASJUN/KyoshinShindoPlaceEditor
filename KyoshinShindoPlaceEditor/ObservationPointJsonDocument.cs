using KyoshinMonitorLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace KyoshinShindoPlaceEditor
{
	internal sealed class ObservationPointJsonDocument
	{
		private readonly Dictionary<ObservationPoint, Dictionary<string, object>> _originalRows
			= new Dictionary<ObservationPoint, Dictionary<string, object>>();

		public List<ObservationPoint> Points { get; private set; }
		public bool IsModern { get; private set; }

		public static ObservationPointJsonDocument MergeMetadata(ObservationPointJsonDocument current,
			ObservationPointJsonDocument incoming, IEnumerable<KeyValuePair<ObservationPoint, ObservationPoint>> mapping,
			List<ObservationPoint> points)
		{
			if (incoming == null || !incoming.IsModern)
				return current;
			var merged = new ObservationPointJsonDocument { IsModern = true, Points = points };
			if (current != null && current.IsModern)
				foreach (var row in current._originalRows)
					merged._originalRows.Add(row.Key, row.Value);
			foreach (var pair in mapping)
			{
				Dictionary<string, object> source, previous;
				if (!incoming._originalRows.TryGetValue(pair.Key, out source))
					continue;
				var row = new Dictionary<string, object>(source);
				if (merged._originalRows.TryGetValue(pair.Value, out previous))
					foreach (var field in previous)
						row[field.Key] = field.Value;
				if (pair.Key.Point.HasValue && source.ContainsKey("point"))
					row["point"] = source["point"];
				merged._originalRows[pair.Value] = row;
			}
			return merged;
		}

		private static JavaScriptSerializer CreateSerializer()
			=> new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

		public static ObservationPointJsonDocument Load(string path)
		{
			var rows = CreateSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as object[];
			if (rows == null)
				throw new InvalidDataException("JSON 顶层必须是观测点数组。");

			var document = new ObservationPointJsonDocument
			{
				IsModern = rows.OfType<Dictionary<string, object>>().Any(row => row.ContainsKey("code"))
			};
			if (!document.IsModern)
			{
				document.Points = ObservationPoint.LoadFromJson(path).ToList();
			}
			else
			{
				document.Points = new List<ObservationPoint>();
				foreach (var value in rows)
				{
					var row = RequireObject(value, "观测点");
					var point = new ObservationPoint
					{
						Type = ReadType(Require(row, "type")),
						Code = ReadString(row, "code"),
						Name = ReadString(row, "name"),
						Region = ReadString(row, "region"),
						IsSuspended = ReadBoolean(Require(row, "is_suspended")),
						Location = ReadLocation(Require(row, "location")),
						OldLocation = row.ContainsKey("japanese_coordinate_system_location") && row["japanese_coordinate_system_location"] != null
							? ReadLocation(row["japanese_coordinate_system_location"]) : null,
						Point = ReadImagePoint(row.ContainsKey("point") ? row["point"] : null)
					};
					document.Points.Add(point);
					document._originalRows.Add(point, row);
				}
			}
			ValidatePoints(document.Points);
			return document;
		}

		public static void ValidatePoints(IEnumerable<ObservationPoint> points)
		{
			if (points == null)
				throw new InvalidDataException("文件不包含观测点数组。");
			var codes = new HashSet<string>(StringComparer.Ordinal);
			var index = 0;
			foreach (var point in points)
			{
				index++;
				if (point == null || string.IsNullOrWhiteSpace(point.Code) || string.IsNullOrWhiteSpace(point.Name))
					throw new InvalidDataException($"第 {index} 条观测点缺少代码或名称，请检查文件格式。");
				if (!codes.Add(point.Code))
					throw new InvalidDataException($"观测点代码重复：{point.Code}。");
				if (!Enum.IsDefined(typeof(ObservationPointType), point.Type))
					throw new InvalidDataException($"观测点 {point.Code} 的类型无效。");
				if (!ValidLocation(point.Location) || point.OldLocation != null && !ValidLocation(point.OldLocation))
					throw new InvalidDataException($"观测点 {point.Code} 缺少有效的经纬度。");
				if (point.Point.HasValue && !IsInsideImage(point.Point.Value))
					throw new InvalidDataException($"观测点 {point.Code} 的图像坐标超出范围，X 应为 0～351，Y 应为 0～399。");
			}
		}

		public static bool IsInsideImage(Point2 point)
			=> point.X >= 0 && point.X < 352 && point.Y >= 0 && point.Y < 400;

		private static bool ValidLocation(Location location)
			=> location != null && !float.IsNaN(location.Latitude) && !float.IsNaN(location.Longitude)
				&& location.Latitude >= -90 && location.Latitude <= 90
				&& location.Longitude >= -180 && location.Longitude <= 180;

		public void Save(string path, IEnumerable<ObservationPoint> points)
		{
			var list = points.ToList();
			ValidatePoints(list);
			var fullPath = Path.GetFullPath(path);
			var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				if (IsModern)
					File.WriteAllText(temporaryPath, CreateSerializer().Serialize(list.Select(ToModernRow).ToArray()), new UTF8Encoding(false));
				else
					ObservationPoint.SaveToJson(temporaryPath, list);

				if (File.Exists(fullPath))
					File.Replace(temporaryPath, fullPath, null);
				else
					File.Move(temporaryPath, fullPath);
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		private Dictionary<string, object> ToModernRow(ObservationPoint point)
		{
			Dictionary<string, object> original;
			var row = _originalRows.TryGetValue(point, out original)
				? new Dictionary<string, object>(original)
				: new Dictionary<string, object>();
			if (original == null || ReadType(original["type"]) != point.Type)
				row["type"] = point.Type == ObservationPointType.KiK_net ? "kiK_net"
					: point.Type == ObservationPointType.K_NET ? "k_NET" : "unknown";
			row["code"] = point.Code;
			row["name"] = point.Name;
			row["region"] = point.Region;
			row["is_suspended"] = point.IsSuspended;
			if (!row.ContainsKey("location") || !SameLocation(ReadLocation(row["location"]), point.Location))
				row["location"] = LocationObject(point.Location);
			if (point.OldLocation != null)
			{
				if (!row.ContainsKey("japanese_coordinate_system_location") || row["japanese_coordinate_system_location"] == null
					|| !SameLocation(ReadLocation(row["japanese_coordinate_system_location"]), point.OldLocation))
					row["japanese_coordinate_system_location"] = LocationObject(point.OldLocation);
			}
			else if (row.ContainsKey("japanese_coordinate_system_location"))
				row["japanese_coordinate_system_location"] = null;

			if (row.ContainsKey("point") || point.Point.HasValue)
				row["point"] = UpdatedImagePoint(point.Point, row.ContainsKey("point") ? row["point"] : null);
			return row;
		}

		internal Action ConfirmImagePoint(ObservationPoint point, Point2 center, Point2 pixel)
		{
			if (!IsModern) return null;
			Dictionary<string, object> previous;
			var existed = _originalRows.TryGetValue(point, out previous);
			var row = ToModernRow(point);
			row["point"] = UpdatedImagePoint(pixel, new Dictionary<string, object> {
				["center_point"] = PixelObject(null, center.X, center.Y),
				["offset"] = PixelObject(null, 0, 0)
			});
			_originalRows[point] = row;
			return () => {
				if (existed) _originalRows[point] = previous;
				else _originalRows.Remove(point);
			};
		}

		private static object UpdatedImagePoint(Point2? point, object original)
		{
			if (point == ReadImagePoint(original))
				return original;
			if (!point.HasValue)
				return null;
			var image = original == null ? new Dictionary<string, object>()
				: new Dictionary<string, object>(RequireObject(original, "point"));
			var center = original == null ? point.Value : ReadPixel(Require(image, "center_point"));
			var offsetX = (long)point.Value.X - center.X;
			var offsetY = (long)point.Value.Y - center.Y;
			// オフセットの格納範囲を超えた場合は中心を移し、元の形式を保つ。
			if (offsetX < -8 || offsetX > 7 || offsetY < -8 || offsetY > 7)
			{
				center = point.Value;
				offsetX = offsetY = 0;
			}
			image["center_point"] = PixelObject(image.ContainsKey("center_point") ? image["center_point"] : null, center.X, center.Y);
			image["offset"] = PixelObject(image.ContainsKey("offset") ? image["offset"] : null, (int)offsetX, (int)offsetY);
			return image;
		}

		private static Dictionary<string, object> PixelObject(object original, int x, int y)
		{
			var result = original == null ? new Dictionary<string, object>()
				: new Dictionary<string, object>(RequireObject(original, "像素坐标"));
			result["x"] = x;
			result["y"] = y;
			return result;
		}

		private static bool SameLocation(Location a, Location b)
			=> a.Latitude == b.Latitude && a.Longitude == b.Longitude;

		private static object LocationObject(Location location)
			=> new Dictionary<string, object> { ["latitude"] = location.Latitude, ["longitude"] = location.Longitude };

		private static Dictionary<string, object> RequireObject(object value, string label)
			=> value as Dictionary<string, object> ?? throw new InvalidDataException($"{label} 必须是 JSON 对象。");

		private static object Require(Dictionary<string, object> row, string key)
		{
			object value;
			if (!row.TryGetValue(key, out value) || value == null)
				throw new InvalidDataException($"JSON 缺少必填字段：{key}。");
			return value;
		}

		private static string ReadString(Dictionary<string, object> row, string key)
			=> Require(row, key) as string ?? throw new InvalidDataException($"字段 {key} 必须是字符串。");

		private static bool ReadBoolean(object value)
			=> value is bool ? (bool)value : throw new InvalidDataException("is_suspended 必须是布尔值。");

		private static ObservationPointType ReadType(object value)
		{
			ObservationPointType type;
			if (!Enum.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), true, out type)
				|| !Enum.IsDefined(typeof(ObservationPointType), type))
				throw new InvalidDataException("观测点 type 字段无效。");
			return type;
		}

		private static double ReadNumber(object value)
		{
			if (!(value is int || value is long || value is double || value is decimal))
				throw new InvalidDataException("坐标必须是数字。");
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}

		private static Location ReadLocation(object value)
		{
			var row = RequireObject(value, "经纬度");
			return new Location((float)ReadNumber(Require(row, "latitude")), (float)ReadNumber(Require(row, "longitude")));
		}

		private static Point2 ReadPixel(object value)
		{
			var row = RequireObject(value, "像素坐标");
			var x = ReadNumber(Require(row, "x"));
			var y = ReadNumber(Require(row, "y"));
			if (x != Math.Truncate(x) || y != Math.Truncate(y) || x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
				throw new InvalidDataException("像素坐标必须是有效的整数。");
			return new Point2((int)x, (int)y);
		}

		private static Point2? ReadImagePoint(object value)
		{
			if (value == null)
				return null;
			var row = RequireObject(value, "point");
			var center = ReadPixel(Require(row, "center_point"));
			var offset = ReadPixel(Require(row, "offset"));
			return new Point2(checked(center.X + offset.X), checked(center.Y + offset.Y));
		}
	}
}
