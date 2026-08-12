using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace XflImporter
{
	
	public struct XflVec2
	{
		public double X, Y;
		public XflVec2(double x, double y) { X = x; Y = y; }
	}

	
	
	
	
	
	
	public static class XflParser
	{
		
		public class ExtraData
		{
			public XflVec2 Origin;
			public readonly Dictionary<string, double[]> ImgSz = new Dictionary<string, double[]>();
			public readonly Dictionary<string, string> ImgMapper = new Dictionary<string, string>();
			public readonly Dictionary<string, string> AnimMapper = new Dictionary<string, string>();
		}

		public class MatrixData
		{
			public double A = 1.0, B = 0.0, C = 0.0, D = 1.0, Tx = 0.0, Ty = 0.0;
		}

		public class ColorData
		{
			public double R = 1.0, G = 1.0, B = 1.0, A = 1.0;
		}

		public class InstanceData
		{
			public string ItemName = "";
			public MatrixData Matrix = new MatrixData();
			public ColorData Color = new ColorData();
			public XflVec2 TransPoint;
			public bool HasTransPoint;
		}

		public class FrameData
		{
			public int Index;
			public int Duration = 1;
			public string Label = "";
			public List<InstanceData> Instances = new List<InstanceData>();
		}

		public class LayerData
		{
			public string Name = "";
			public List<FrameData> Frames = new List<FrameData>();
		}

		public class SymbolData
		{
			public string Name = "";
			public bool IsImage;
			public string BitmapName = "";
			public List<LayerData> Layers = new List<LayerData>();
		}

		public class LabelData
		{
			public string Name = "";
			public int Start;
			public int Duration;
		}

		public class DocData
		{
			public int FrameRate = 30;
			public Dictionary<string, string> Bitmaps = new Dictionary<string, string>();
			public List<LabelData> Labels = new List<LabelData>();
		}

		
		private static string Attr(XElement e, string name, string def = "")
		{
			if (e == null) return def;
			var a = e.Attribute(name);
			return a != null ? a.Value : def;
		}

		private static bool HasAttr(XElement e, string name) => e != null && e.Attribute(name) != null;

		private static double AttrF(XElement e, string name, double def)
		{
			var a = e != null ? e.Attribute(name) : null;
			if (a == null) return def;
			return double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
				? FixU32(v) : def;
		}

		private static int AttrI(XElement e, string name, int def)
		{
			var a = e != null ? e.Attribute(name) : null;
			if (a == null) return def;
			return int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
				? v : def;
		}

		
		
		
		
		public static double FixU32(double v)
		{
			return v > 2147483647.0 ? v - 4294967296.0 : v;
		}

		
		public static ExtraData ParseExtra(string path)
		{
			var data = new ExtraData();
			try
			{
				var root = MiniJson.Obj(MiniJson.Parse(File.ReadAllText(path)));
				if (root == null) return data;

				var origin = root.TryGetValue("origin", out var o) ? MiniJson.Arr(o) : null;
				data.Origin = new XflVec2(
					origin != null && origin.Count > 0 ? MiniJson.Num(origin[0]) : 0.0,
					origin != null && origin.Count > 1 ? MiniJson.Num(origin[1]) : 0.0);

				if (root.TryGetValue("imgSz", out var isz) && isz is Dictionary<string, object> sz)
				{
					foreach (var kv in sz)
					{
						var arr = MiniJson.Arr(kv.Value);
						if (arr != null && arr.Count >= 2)
						{
							data.ImgSz[kv.Key] = new[] { MiniJson.Num(arr[0]), MiniJson.Num(arr[1]) };
						}
					}
				}
				if (root.TryGetValue("imgMapper", out var im) && im is Dictionary<string, object> imMap)
				{
					foreach (var kv in imMap) data.ImgMapper[kv.Key] = kv.Value as string ?? "";
				}
				if (root.TryGetValue("animMapper", out var am) && am is Dictionary<string, object> amMap)
				{
					foreach (var kv in amMap) data.AnimMapper[kv.Key] = kv.Value as string ?? "";
				}
			}
			catch (Exception e)
			{
				LogError("extra.json 解析失败: " + path + "\n" + e);
			}
			return data;
		}

		
		public static DocData ParseDoc(string path)
		{
			var doc = new DocData();
			try
			{
				var xdoc = XDocument.Load(path);
				foreach (var e in xdoc.Descendants())
				{
					switch (e.Name.LocalName)
					{
						case "DOMDocument":
							doc.FrameRate = AttrI(e, "frameRate", doc.FrameRate);
							break;
						case "DOMBitmapItem":
							if (HasAttr(e, "name") && HasAttr(e, "href"))
							{
								doc.Bitmaps[Attr(e, "name")] = Attr(e, "href");
							}
							break;
						case "DOMFrame":
							
							if (HasAttr(e, "name"))
							{
								var dur = Math.Max(AttrI(e, "duration", 1), 1);
								doc.Labels.Add(new LabelData
								{
									Name = Attr(e, "name"),
									Start = AttrI(e, "index", 0),
									Duration = dur,
								});
							}
							break;
					}
				}
			}
			catch (Exception e)
			{
				LogError("DOMDocument.xml 解析失败: " + path + "\n" + e);
			}
			return doc;
		}

		
		public static SymbolData ParseSymbolFile(string path)
		{
			var sym = new SymbolData();
			try
			{
				var xdoc = XDocument.Load(path);
				LayerData curLayer = null;
				FrameData curFrame = null;
				InstanceData curInst = null;
				foreach (var e in xdoc.Descendants())
				{
					switch (e.Name.LocalName)
					{
						case "DOMSymbolItem":
							sym.Name = Attr(e, "name", sym.Name);
							break;
						case "DOMLayer":
							curLayer = new LayerData { Name = Attr(e, "name") };
							sym.Layers.Add(curLayer);
							break;
						case "DOMFrame":
							curFrame = new FrameData
							{
								Index = AttrI(e, "index", 0),
								Duration = Math.Max(AttrI(e, "duration", 1), 1),
								Label = Attr(e, "name"),
							};
							if (curLayer != null) curLayer.Frames.Add(curFrame);
							break;
						case "DOMSymbolInstance":
						case "DOMBitmapInstance":
							curInst = null;
							if (HasAttr(e, "libraryItemName"))
							{
								curInst = new InstanceData { ItemName = Attr(e, "libraryItemName") };
								if (curFrame != null) curFrame.Instances.Add(curInst);
								if (e.Name.LocalName == "DOMBitmapInstance")
								{
									sym.IsImage = true;
									sym.BitmapName = curInst.ItemName;
								}
							}
							break;
						case "Matrix":
							if (curInst != null)
							{
								curInst.Matrix = new MatrixData
								{
									A = AttrF(e, "a", 1.0),
									B = AttrF(e, "b", 0.0),
									C = AttrF(e, "c", 0.0),
									D = AttrF(e, "d", 1.0),
									Tx = AttrF(e, "tx", 0.0),
									Ty = AttrF(e, "ty", 0.0),
								};
							}
							break;
						case "Color":
							if (curInst != null)
							{
								curInst.Color = new ColorData
								{
									R = AttrF(e, "redMultiplier", 1.0),
									G = AttrF(e, "greenMultiplier", 1.0),
									B = AttrF(e, "blueMultiplier", 1.0),
									A = AttrF(e, "alphaMultiplier", 1.0),
								};
							}
							break;
						case "Point":
							if (curInst != null)
							{
								curInst.TransPoint = new XflVec2(AttrF(e, "x", 0.0), AttrF(e, "y", 0.0));
								curInst.HasTransPoint = true;
							}
							break;
					}
				}
			}
			catch (Exception e)
			{
				LogError("符号文件解析失败: " + path + "\n" + e);
			}
			return sym;
		}

		private static void LogError(string msg)
		{
			
			Console.Error.WriteLine("[XflParser] " + msg);
		}
	}
}
