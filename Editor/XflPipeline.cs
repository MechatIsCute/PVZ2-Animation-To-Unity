using System;
using System.Collections.Generic;
using System.IO;

namespace XflImporter
{
	
	[Serializable]
	public class XflOptions
	{
		
		public bool MergeSlots = true;
		
		public int MergeMode = 1;
		
		public bool NormalizeNames = true;
		
		public int MergeBuffer = 0;
		
		public bool GroupSlots = true;
	}

	
	public class SegmentData
	{
		public string Name = "";
		public int Start;
		public int Duration;
	}

	public class LeafData
	{
		public string SlotKey = "";
		public string Path = "";
		public string Pose = "";
		public string Image = "";       
		public XflMatrix Matrix;
		public XflColor Color;
	}

	public class MemberRef
	{
		public int LayerIndex;
		public string Key = "";
		public int Z;
	}

	public class SlotData
	{
		public string Key = "";
		public int LayerIndex;
		public string Name = "";
		public string BaseName = "";
		public string GroupName = "";
		public List<MemberRef> Members = new List<MemberRef>();
		public int Z;

		public SlotData Clone()
		{
			return new SlotData
			{
				Key = Key,
				LayerIndex = LayerIndex,
				Name = Name,
				BaseName = BaseName,
				GroupName = GroupName,
				Members = new List<MemberRef>(Members),
				Z = Z,
			};
		}
	}

	public class FrameState
	{
		public XflVec2 Pos;      
		public double Rot;       
		public XflVec2 Scale;    
		public double ChildRot;  
		public XflVec2 ChildScale = new XflVec2(1, 1);
		public double SkewErr;   
		public XflColor Color = XflColor.White;
		public string ImageName = "";  
		public bool Visible;
		public int Z;
	}

	public class SlotAnalysis
	{
		public readonly List<int[]> Visses = new List<int[]>();
		public readonly List<string> TexKeys = new List<string>();
		public readonly List<List<int>> FrameSlots = new List<List<int>>();
		public readonly List<long> SegMasks = new List<long>();
	}

	
	
	
	
	
	
	public class XflPipeline
	{
		public const int MaxPoseDepth = 24;

		public XflOptions Options = new XflOptions();
		public readonly List<string> Warnings = new List<string>();

		public string SrcAbs = "";
		public string LibDirAbs = "";
		public XflParser.ExtraData Extra;
		public XflParser.DocData Doc;
		public XflParser.SymbolData MainSym;
		public string CharName = "";
		public int TotalFrames = 1;
		public List<SegmentData> Segments = new List<SegmentData>();
		public List<List<List<LeafData>>> FrameLeaves = new List<List<List<LeafData>>>();

		private readonly Dictionary<string, XflParser.SymbolData> _symbols = new Dictionary<string, XflParser.SymbolData>();
		private readonly HashSet<string> _usedSpriteNames = new HashSet<string>();
		private readonly Dictionary<string, string> _imagePartNames = new Dictionary<string, string>();
		private readonly HashSet<string> _skewWarned = new HashSet<string>();

		
		public bool SetupSource(string srcPath, out string error)
		{
			error = "";
			Warnings.Clear();
			_skewWarned.Clear();
			SrcAbs = srcPath.Replace('\\', '/').TrimEnd('/');
			if (SrcAbs == "") { error = "源文件夹为空"; return false; }
			CharName = SanitizeName(Path.GetFileName(SrcAbs));
			if (CharName == "") CharName = SanitizeName(Path.GetFileName(Path.GetDirectoryName(SrcAbs)));
			if (CharName == "") { error = "无法从源文件夹名推导角色名（源路径：" + srcPath + "）"; return false; }
			if (!File.Exists(Path.Combine(SrcAbs, "DOMDocument.xml"))) { error = "未找到 DOMDocument.xml（不是有效的 XFL 源文件夹）"; return false; }
			if (!File.Exists(Path.Combine(SrcAbs, "extra.json"))) { error = "未找到 extra.json"; return false; }
			LibDirAbs = SrcAbs + "/LIBRARY";
			if (!File.Exists(Path.Combine(LibDirAbs, "main.xml"))) { error = "未找到 LIBRARY/main.xml"; return false; }

			Extra = XflParser.ParseExtra(Path.Combine(SrcAbs, "extra.json"));
			Doc = XflParser.ParseDoc(Path.Combine(SrcAbs, "DOMDocument.xml"));
			MainSym = XflParser.ParseSymbolFile(Path.Combine(LibDirAbs, "main.xml"));
			if (MainSym == null || MainSym.Layers.Count == 0) { error = "main.xml 解析失败或无图层"; return false; }

			BuildSegments();
			TotalFrames = ComputeTotalFrames();
			return true;
		}

		
		public void BuildSegments()
		{
			Segments.Clear();
			foreach (var label in Doc.Labels)
			{
				Segments.Add(new SegmentData { Name = SanitizeName(label.Name), Start = label.Start, Duration = label.Duration });
			}
			Segments.Sort((a, b) => a.Start.CompareTo(b.Start));
			if (Segments.Count == 0)
			{
				Segments.Add(new SegmentData { Name = "anim", Start = 0, Duration = 1 });
			}
		}

		public int ComputeTotalFrames()
		{
			var maxF = 0;
			foreach (var seg in Segments) maxF = Math.Max(maxF, seg.Start + seg.Duration);
			foreach (var layer in MainSym.Layers)
			{
				foreach (var fr in layer.Frames) maxF = Math.Max(maxF, fr.Index + fr.Duration);
			}
			TotalFrames = Math.Max(maxF, 1);
			return TotalFrames;
		}

		
		public XflParser.SymbolData GetSymbol(string name)
		{
			if (_symbols.TryGetValue(name, out var sym)) return sym;
			var path = Path.Combine(LibDirAbs, name + ".xml");
			if (!File.Exists(path)) return null;
			sym = XflParser.ParseSymbolFile(path);
			_symbols[name] = sym;
			return sym;
		}

		public static XflParser.InstanceData ImageInstance(XflParser.SymbolData pose)
		{
			if (pose.Layers.Count == 0 || pose.Layers[0].Frames.Count == 0) return null;
			var layer = pose.Layers[0];
			var fr = layer.Frames[0];
			if (fr.Instances.Count == 0) return null;
			return fr.Instances[0];
		}

		public static XflMatrix ToXflMatrix(XflParser.MatrixData md)
		{
			return new XflMatrix { A = md.A, B = md.B, C = md.C, D = md.D, Tx = md.Tx, Ty = md.Ty };
		}

		public static XflColor ToXflColor(XflParser.ColorData cd)
		{
			return new XflColor { R = cd.R, G = cd.G, B = cd.B, A = cd.A };
		}

		public static XflMatrix MakeImageTransform(XflParser.InstanceData imgInst)
		{
			
			
			
			
			if (imgInst == null) return XflMatrix.Identity;
			return ToXflMatrix(imgInst.Matrix);
		}

		public List<LeafData> ResolvePose(XflParser.InstanceData inst)
		{
			var pose = GetSymbol(inst.ItemName);
			var outList = new List<LeafData>();
			if (pose == null) return outList;

			if (pose.IsImage)
			{
				var imgInst = ImageInstance(pose);
				var eff = MakeImageTransform(imgInst);
				outList.Add(new LeafData
				{
					SlotKey = "",
					Path = "",
					Pose = pose.Name,
					Image = pose.BitmapName,
					Matrix = ToXflMatrix(inst.Matrix) * eff,
					Color = ToXflColor(inst.Color),
				});
			}
			else
			{
				CollectPoseLeaves(pose, ToXflMatrix(inst.Matrix), ToXflColor(inst.Color), "", outList, 0, IsStatesPose(pose.Name));
				if (outList.Count == 1)
				{
					outList[0].SlotKey = "";
				}
				else
				{
					foreach (var leaf in outList) leaf.SlotKey = pose.Name + "/" + leaf.Path;
				}
				foreach (var leaf in outList) leaf.Pose = pose.Name;
			}
			return outList;
		}

		public bool IsStatesPose(string poseName)
		{
			var mapped = Extra.AnimMapper.TryGetValue(poseName, out var m) ? m : "";
			return mapped.Contains("states");
		}

		public void CollectPoseLeaves(XflParser.SymbolData pose, XflMatrix parentT, XflColor parentC,
			string pathPrefix, List<LeafData> outList, int depth, bool onlyDefaultState)
		{
			if (depth > MaxPoseDepth)
			{
				Warnings.Add("姿态符号嵌套过深（疑似循环引用），已截断: " + pose.Name);
				return;
			}
			var layers = pose.Layers;
			if (onlyDefaultState && layers.Count > 0)
			{
				layers = new List<XflParser.LayerData> { layers[0] };
			}
			foreach (var layer in layers)
			{
				var layerPath = pathPrefix == "" ? layer.Name : pathPrefix + "/" + layer.Name;
				var fr = layer.Frames.Count > 0 ? layer.Frames[0] : null;
				if (fr == null) continue;
				foreach (var inst in fr.Instances)
				{
					var t = parentT * ToXflMatrix(inst.Matrix);
					var c = parentC * ToXflColor(inst.Color);
					var sub = GetSymbol(inst.ItemName);
					if (sub != null && sub.IsImage)
					{
						var imgInst = ImageInstance(sub);
						var eff = MakeImageTransform(imgInst);
						outList.Add(new LeafData
						{
							SlotKey = "",
							Path = layerPath,
							Pose = pose.Name,
							Image = sub.BitmapName,
							Matrix = t * eff,
							Color = c,
						});
					}
					else if (sub != null)
					{
						CollectPoseLeaves(sub, t, c, layerPath, outList, depth + 1, IsStatesPose(sub.Name));
					}
				}
			}
		}

		
		public void BuildFrameLeaves()
		{
			FrameLeaves.Clear();
			foreach (var layer in MainSym.Layers)
			{
				var frames = new XflParser.FrameData[TotalFrames];
				foreach (var fr in layer.Frames)
				{
					for (var k = 0; k < fr.Duration; k++)
					{
						var idx = fr.Index + k;
						if (idx >= 0 && idx < TotalFrames) frames[idx] = fr;
					}
				}
				var leaves = new List<List<LeafData>>();
				for (var g = 0; g < TotalFrames; g++)
				{
					var list = new List<LeafData>();
					var fr = frames[g];
					if (fr != null)
					{
						foreach (var inst in fr.Instances) list.AddRange(ResolvePose(inst));
					}
					leaves.Add(list);
				}
				FrameLeaves.Add(leaves);
			}
		}

		public static LeafData FindLeaf(List<LeafData> leaves, string slotKey)
		{
			foreach (var leaf in leaves)
			{
				if (leaf.SlotKey == slotKey) return leaf;
			}
			return null;
		}

		public static int LayerNum(XflParser.LayerData layer)
		{
			if (layer.Name.Length > 1 && layer.Name[0] == 'l'
				&& int.TryParse(layer.Name.Substring(1), out var n) && n >= 0)
			{
				return n;
			}
			return 1000000;
		}

		
		public List<SlotData> CreateSprites()
		{
			var allSlots = new List<SlotData>();
			_usedSpriteNames.Clear();
			var entries = new List<(int li, int num)>();
			for (var li = 0; li < MainSym.Layers.Count; li++) entries.Add((li, LayerNum(MainSym.Layers[li])));
			entries.Sort((a, b) => a.num.CompareTo(b.num));
			foreach (var entry in entries) allSlots.AddRange(BuildLayerSlots(entry.li));
			return allSlots;
		}

		public List<SlotData> BuildLayerSlots(int li)
		{
			var info = new Dictionary<string, (int minIdx, int first)>();
			for (var g = 0; g < TotalFrames; g++)
			{
				var leaves = FrameLeaves[li][g];
				for (var li2 = 0; li2 < leaves.Count; li2++)
				{
					var leaf = leaves[li2];
					if (!info.ContainsKey(leaf.SlotKey))
					{
						info[leaf.SlotKey] = (li2, g);
					}
					else
					{
						var cur = info[leaf.SlotKey];
						if (li2 < cur.minIdx) info[leaf.SlotKey] = (li2, cur.first);
					}
				}
			}

			var baseName = LayerBaseName(li);
			var keys = new List<string>(info.Keys);
			keys.Sort((a, b) =>
			{
				var ia = info[a];
				var ib = info[b];
				if (ia.minIdx != ib.minIdx) return ib.minIdx.CompareTo(ia.minIdx);
				return ia.first.CompareTo(ib.first);
			});

			var slots = new List<SlotData>();
			foreach (var key in keys)
			{
				var semantic = SpriteName(baseName, key);
				slots.Add(new SlotData
				{
					Key = key,
					LayerIndex = li,
					Name = UniqueSpriteName(semantic),
					BaseName = semantic,
				});
			}
			return slots;
		}

		public string LayerBaseName(int li)
		{
			for (var g = 0; g < TotalFrames; g++)
			{
				var leaves = FrameLeaves[li][g];
				if (leaves.Count > 0) return ResolveBaseName(leaves[0].Pose);
			}
			return "layer" + li;
		}

		public string ResolveBaseName(string poseName)
		{
			var name = Extra.AnimMapper.TryGetValue(poseName, out var am) ? am : "";
			if (name == "")
			{
				name = Extra.ImgMapper.TryGetValue(poseName, out var im) ? im : "";
			}
			if (name.StartsWith("IMAGE_"))
			{
				if (_imagePartNames.TryGetValue(poseName, out var part) && part != "")
				{
					name = part;
				}
				else
				{
					
					
					
					var family = InferImageFamily(poseName);
					if (family != "")
					{
						var last = name.LastIndexOf('_');
						var sizePart = last >= 0 ? name.Substring(last + 1).ToLowerInvariant() : "";
						name = sizePart != "" ? family + "_" + sizePart : family;
					}
					else
					{
						name = SimplifyImageName(name);
					}
				}
			}
			name = name.TrimStart('_');
			if (name == "") name = poseName;
			return SanitizePartName(name);
		}

		private readonly Dictionary<string, string> _imageSegmentNames = new Dictionary<string, string>();

		
		
		
		
		
		
		
		private string InferImageFamily(string poseName)
		{
			if (_imageSegmentNames.TryGetValue(poseName, out var cached)) return cached;
			var segCount = new Dictionary<string, int>();
			var total = 0;
			for (var li = 0; li < FrameLeaves.Count; li++)
			{
				for (var g = 0; g < TotalFrames; g++)
				{
					foreach (var leaf in FrameLeaves[li][g])
					{
						if (leaf.Pose != poseName) continue;
						total++;
						var seg = Segments.Find(s => g >= s.Start && g < s.Start + s.Duration);
						if (seg == null) continue;
						
						
						var key = StripTrailingNumber(seg.Name);
						segCount[key] = segCount.TryGetValue(key, out var n) ? n + 1 : 1;
					}
				}
			}
			string best = "";
			var bestN = 0;
			foreach (var kv in segCount)
			{
				if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
			}
			if (best == "" || total == 0 || bestN * 100 < total * 85) best = "";
			_imageSegmentNames[poseName] = best;
			return best;
		}

		
		
		public static string SanitizePartName(string name)
		{
			if (string.IsNullOrEmpty(name)) return "";
			var di = name.IndexOf("Duplicate Items", StringComparison.Ordinal);
			if (di >= 0)
			{
				var after = di + "Duplicate Items".Length;
				if (name.Length > after + 7 && name.Substring(after, 7) == " Folder") after += 7;
				name = name.Substring(after);
			}
			name = name.Replace(' ', '_');
			name = name.Replace('/', '_');
			var parts = name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
			return string.Join("_", parts);
		}

		
		public static string SimplifyImageName(string name)
		{
			var parts = name.Split('_');
			if (parts.Length >= 2)
			{
				return "img_" + parts[parts.Length - 1].ToLowerInvariant();
			}
			return name;
		}

		public static string SpriteName(string baseName, string slotKey)
		{
			if (slotKey == "") return baseName;
			var parts = slotKey.Split('/');
			var suffix = "";
			for (var i = 1; i < parts.Length; i++)
			{
				if (suffix != "") suffix += "_";
				suffix += parts[i];
			}
			return suffix == "" ? baseName : baseName + "_" + suffix;
		}

		public string UniqueSpriteName(string baseName)
		{
			var name = baseName;
			var idx = 2;
			while (_usedSpriteNames.Contains(name))
			{
				name = baseName + "@" + idx;
				idx++;
			}
			_usedSpriteNames.Add(name);
			return name;
		}

		
		public static string StripAtSuffix(string name)
		{
			var at = name.IndexOf('@');
			if (at > 0)
			{
				var allDigits = at + 1 < name.Length;
				for (var j = at + 1; j < name.Length; j++)
				{
					if (!char.IsDigit(name[j])) { allDigits = false; break; }
				}
				if (allDigits) return name.Substring(0, at);
			}
			return name;
		}

		public static string SanitizeName(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			var sb = new System.Text.StringBuilder();
			foreach (var c in s)
			{
				if (char.IsLetterOrDigit(c) || c == '_' || c == '.') sb.Append(c);
			}
			return sb.ToString();
		}

		
		public void BuildImagePartNames()
		{
			_imagePartNames.Clear();
			var refs = new Dictionary<string, List<string>>();
			foreach (var kv in Extra.AnimMapper)
			{
				var poseName = kv.Key;
				var partName = SanitizePartName(kv.Value);
				if (partName == "") continue;
				var sym = GetSymbol(poseName);
				if (sym == null || sym.IsImage) continue;
				CollectImageRefs(sym, refs, partName);
			}
			var seen = new HashSet<string>();
			foreach (var layer in MainSym.Layers)
			{
				foreach (var fr in layer.Frames)
				{
					foreach (var inst in fr.Instances)
					{
						var sub = GetSymbol(inst.ItemName);
						if (sub == null || sub.IsImage) continue;
						if (!seen.Add(sub.Name)) continue;
						var partName = Extra.AnimMapper.TryGetValue(sub.Name, out var m)
							? SanitizePartName(m)
							: SanitizePartName(sub.Name);
						if (partName == "") continue;
						CollectImageRefs(sub, refs, partName);
					}
				}
			}
			foreach (var kv in refs)
			{
				var part = CommonPartName(kv.Value);
				if (part == "") part = kv.Value[0];
				_imagePartNames[kv.Key] = part;
			}
		}

		public void CollectImageRefs(XflParser.SymbolData sym, Dictionary<string, List<string>> refs, string refName, int depth = 0)
		{
			if (depth > MaxPoseDepth) return;
			foreach (var layer in sym.Layers)
			{
				foreach (var fr in layer.Frames)
				{
					foreach (var inst in fr.Instances)
					{
						var sub = GetSymbol(inst.ItemName);
						if (sub == null) continue;
						if (sub.IsImage)
						{
							if (!refs.TryGetValue(sub.Name, out var list))
							{
								list = new List<string>();
								refs[sub.Name] = list;
							}
							if (!list.Contains(refName)) list.Add(refName);
						}
						else
						{
							CollectImageRefs(sub, refs, refName, depth + 1);
						}
					}
				}
			}
		}

		public static string CommonPartName(List<string> names)
		{
			if (names.Count == 0) return "";
			if (names.Count == 1) return names[0];
			var first = names[0].Split('_');
			var common = new List<string>();
			for (var i = 0; i < first.Length; i++)
			{
				foreach (var n in names)
				{
					var parts = n.Split('_');
					if (i >= parts.Length || parts[i] != first[i]) return string.Join("_", common);
				}
				common.Add(first[i]);
			}
			return string.Join("_", common);
		}

		
		public List<SlotData> MergeSlotLists(List<SlotData> rawSlots)
		{
			if (rawSlots.Count < 2) return rawSlots;
			for (var i = 0; i < rawSlots.Count; i++) rawSlots[i].Z = i;

			var analysis = AnalyzeSlots(rawSlots);
			var visses = analysis.Visses;
			var texKeys = analysis.TexKeys;
			var frameSlots = analysis.FrameSlots;
			var segMasks = analysis.SegMasks;

			
			var pools = new Dictionary<string, List<int>>();
			for (var si = 0; si < rawSlots.Count; si++)
			{
				if (!pools.TryGetValue(texKeys[si], out var pool))
				{
					pool = new List<int>();
					pools[texKeys[si]] = pool;
				}
				pool.Add(si);
			}

			var groups = new List<(int rep, List<int> members)>();
			foreach (var kv in pools)
			{
				var poolGroups = new List<List<int>>();
				foreach (var si in kv.Value)
				{
					var placed = false;
					foreach (var pg in poolGroups)
					{
						if (CanAddToGroup(pg, si, rawSlots, visses, frameSlots, segMasks))
						{
							pg.Add(si);
							placed = true;
							break;
						}
					}
					if (!placed) poolGroups.Add(new List<int> { si });
				}
				foreach (var pg in poolGroups) groups.Add((pg[0], pg));
			}

			var merged = new List<SlotData>();
			foreach (var g in groups)
			{
				var rep = rawSlots[g.rep];
				var members = new List<MemberRef>();
				foreach (var si in g.members)
				{
					var m = rawSlots[si];
					members.Add(new MemberRef { LayerIndex = m.LayerIndex, Key = m.Key, Z = m.Z });
				}
				var outSlot = rep.Clone();
				outSlot.Members = members;
				outSlot.BaseName = rep.BaseName != "" ? rep.BaseName : StripAtSuffix(rep.Name);
				merged.Add(outSlot);
			}
			
			merged.Sort((a, b) => a.Z.CompareTo(b.Z));

			
			var usedNames = new HashSet<string>();
			if (Options.NormalizeNames)
			{
				var byBase = new Dictionary<string, List<SlotData>>();
				foreach (var m in merged)
				{
					if (!byBase.TryGetValue(m.BaseName, out var list))
					{
						list = new List<SlotData>();
						byBase[m.BaseName] = list;
					}
					list.Add(m);
				}
				foreach (var kv in byBase)
				{
					var idx = 2;
					foreach (var m in kv.Value)
					{
						var name = kv.Key;
						while (usedNames.Contains(name))
						{
							name = kv.Key + "_" + idx;
							idx++;
						}
						usedNames.Add(name);
						m.Name = name;
					}
				}
			}
			else
			{
				foreach (var m in merged)
				{
					var clean = m.BaseName != "" ? m.BaseName : StripAtSuffix(m.Name);
					var finalName = clean;
					var idx = 2;
					while (usedNames.Contains(finalName))
					{
						finalName = clean + "@" + idx;
						idx++;
					}
					usedNames.Add(finalName);
					m.Name = finalName;
				}
			}
			return merged;
		}

		public SlotAnalysis AnalyzeSlots(List<SlotData> rawSlots)
		{
			var frameSeg = new int[TotalFrames];
			for (var i = 0; i < TotalFrames; i++) frameSeg[i] = -1;
			for (var k = 0; k < Segments.Count; k++)
			{
				var seg = Segments[k];
				for (var f = 0; f < seg.Duration; f++)
				{
					var idx = seg.Start + f;
					if (idx >= 0 && idx < TotalFrames) frameSeg[idx] = k;
				}
			}

			var layerKeySets = new List<List<HashSet<string>>>();
			for (var li = 0; li < FrameLeaves.Count; li++)
			{
				var layer = new List<HashSet<string>>();
				for (var g = 0; g < TotalFrames; g++)
				{
					var set = new HashSet<string>();
					foreach (var leaf in FrameLeaves[li][g]) set.Add(leaf.SlotKey);
					layer.Add(set);
				}
				layerKeySets.Add(layer);
			}

			var analysis = new SlotAnalysis();
			for (var g = 0; g < TotalFrames; g++) analysis.FrameSlots.Add(new List<int>());

			for (var si = 0; si < rawSlots.Count; si++)
			{
				var slot = rawSlots[si];
				var vis = new List<int>();
				var texKey = "";
				var sets = layerKeySets[slot.LayerIndex];
				for (var g = 0; g < TotalFrames; g++)
				{
					if (sets[g].Contains(slot.Key))
					{
						vis.Add(g);
						analysis.FrameSlots[g].Add(si);
						if (texKey == "")
						{
							var leaf = FindLeaf(FrameLeaves[slot.LayerIndex][g], slot.Key);
							if (leaf != null) texKey = leaf.Image;
						}
					}
				}
				analysis.Visses.Add(vis.ToArray());
				analysis.TexKeys.Add(texKey);
				long mask = 0;
				foreach (var g in vis)
				{
					var sg = frameSeg[g];
					if (sg >= 0) mask |= 1L << sg;
				}
				analysis.SegMasks.Add(mask);
			}
			return analysis;
		}

		public bool CanAddToGroup(List<int> groupMembers, int si, List<SlotData> rawSlots,
			List<int[]> visses, List<List<int>> frameSlots, List<long> segMasks)
		{
			var siVis = visses[si];
			foreach (var m in groupMembers)
			{
				if (VisIntersects(visses[m], siVis)) return false;
				
				if (Options.MergeMode == 0 && (segMasks[m] & segMasks[si]) != 0 && rawSlots[m].Z != rawSlots[si].Z)
				{
					return false;
				}
			}
			if (Options.MergeBuffer > 0)
			{
				var owner = new Dictionary<int, int>();
				foreach (var m in groupMembers)
				{
					foreach (var f in visses[m]) owner[f] = m;
				}
				foreach (var f in siVis) owner[f] = si;
				var all = new List<int>(owner.Keys);
				all.Sort();
				for (var i = 1; i < all.Count; i++)
				{
					if (all[i] - all[i - 1] <= Options.MergeBuffer && owner[all[i]] != owner[all[i - 1]])
					{
						return false;
					}
				}
			}
			return true;
		}

		public static bool VisIntersects(int[] a, int[] b)
		{
			var i = 0; var j = 0;
			while (i < a.Length && j < b.Length)
			{
				if (a[i] == b[j]) return true;
				if (a[i] < b[j]) i++;
				else j++;
			}
			return false;
		}

		
		
		
		
		
		
		
		
		
		
		public void ApplyGrouping(List<SlotData> slots)
		{
			if (!Options.GroupSlots) return;
			var groupMembers = new Dictionary<string, List<SlotData>>();
			foreach (var s in slots)
			{
				var g = ComputeGroupBase(s);
				if (g == null) continue;
				if (!groupMembers.TryGetValue(g, out var list))
				{
					list = new List<SlotData>();
					groupMembers[g] = list;
				}
				list.Add(s);
			}
			var inGroup = new HashSet<SlotData>();
			foreach (var kv in groupMembers)
			{
				if (kv.Value.Count >= 2)
				{
					foreach (var s in kv.Value) inGroup.Add(s);
				}
			}
			var usedNames = new HashSet<string>();
			foreach (var s in slots)
			{
				if (!inGroup.Contains(s)) usedNames.Add(s.Name);
			}
			var ordered = new List<KeyValuePair<string, List<SlotData>>>(groupMembers);
			ordered.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
			foreach (var kv in ordered)
			{
				if (kv.Value.Count < 2) continue;
				var final = kv.Key;
				var idx = 2;
				while (usedNames.Contains(final))
				{
					final = kv.Key + "_" + idx;
					idx++;
				}
				usedNames.Add(final);
				foreach (var s in kv.Value) s.GroupName = final;
			}
		}

		
		public static string ComputeGroupBase(SlotData s)
		{
			var baseName = s.BaseName;
			if (string.IsNullOrEmpty(baseName) || baseName.StartsWith("img_")) return null;
			
			
			
			
			return NormalizeMiddleNumbers(StripTrailingNumber(StripAtSuffix(baseName)));
		}

		
		
		
		
		
		
		
		public List<ParticleGroupData> BuildParticleGroups(List<SlotData> slots)
		{
			var result = new List<ParticleGroupData>();
			var texOf = new Dictionary<SlotData, string>();
			var visOf = new Dictionary<SlotData, List<int>>();
			foreach (var s in slots)
			{
				var members = s.Members.Count > 0
					? s.Members
					: new List<MemberRef> { new MemberRef { LayerIndex = s.LayerIndex, Key = s.Key } };
				var tex = "";
				var vis = new List<int>();
				foreach (var m in members)
				{
					for (var g = 0; g < TotalFrames; g++)
					{
						var leaf = FindLeaf(FrameLeaves[m.LayerIndex][g], m.Key);
						if (leaf != null)
						{
							vis.Add(g);
							if (tex == "") tex = leaf.Image;
						}
					}
				}
				texOf[s] = tex;
				visOf[s] = vis;
			}

			var clusters = new Dictionary<string, List<SlotData>>();
			foreach (var s in slots)
			{
				if (s.GroupName == "") continue;
				var key = s.GroupName + "\u0001" + texOf[s];
				if (!clusters.TryGetValue(key, out var list))
				{
					list = new List<SlotData>();
					clusters[key] = list;
				}
				list.Add(s);
			}
			foreach (var kv in clusters)
			{
				if (kv.Value.Count < 2) continue;
				var anyOverlap = false;
				for (var i = 0; i < kv.Value.Count && !anyOverlap; i++)
				{
					var vi = visOf[kv.Value[i]].ToArray();
					for (var j = i + 1; j < kv.Value.Count && !anyOverlap; j++)
					{
						if (VisIntersects(vi, visOf[kv.Value[j]].ToArray())) anyOverlap = true;
					}
				}
				if (!anyOverlap) continue;
				var sep = kv.Key.IndexOf('\u0001');
				result.Add(new ParticleGroupData
				{
					GroupName = kv.Key.Substring(0, sep),
					ImageName = kv.Key.Substring(sep + 1),
					Slots = kv.Value,
				});
			}
			return result;
		}

		
		public class ParticleGroupData
		{
			public string GroupName = "";
			public string ImageName = "";
			public List<SlotData> Slots = new List<SlotData>();
		}

		
		public static string StripTrailingNumber(string name)
		{
			if (string.IsNullOrEmpty(name)) return name;
			var under = name.LastIndexOf('_');
			if (under <= 0 || under == name.Length - 1) return name;
			var tail = name.Substring(under + 1);
			if (!IsAllDigits(tail)) return name;
			return name.Substring(0, under);
		}

		
		public static string NormalizeMiddleNumbers(string name)
		{
			if (string.IsNullOrEmpty(name)) return name;
			var parts = name.Split('_');
			var sb = new System.Text.StringBuilder();
			for (var i = 0; i < parts.Length; i++)
			{
				
				if (i > 0 && i < parts.Length - 1 && IsAllDigits(parts[i])) continue;
				if (sb.Length > 0) sb.Append('_');
				sb.Append(parts[i]);
			}
			return sb.ToString();
		}

		public static bool IsAllDigits(string s)
		{
			if (string.IsNullOrEmpty(s)) return false;
			foreach (var c in s) if (!char.IsDigit(c)) return false;
			return true;
		}

		
		public FrameState GetFrameState(SlotData slot, int g, FrameState prev)
		{
			var members = slot.Members.Count > 0
				? slot.Members
				: new List<MemberRef> { new MemberRef { LayerIndex = slot.LayerIndex, Key = slot.Key, Z = slot.Z } };
			foreach (var m in members)
			{
				var leaves = FrameLeaves[m.LayerIndex][g];
				var leaf = FindLeaf(leaves, m.Key);
				if (leaf == null) continue;
				XflMatrix.DecomposeNested(leaf.Matrix, out var px, out var py, out var rot, out var sx, out var sy,
					out var childRot, out var childSx, out var childSy, out var skewErr);
				if (skewErr > 0.01)
				{
					var key = leaf.Pose + "/" + leaf.Path;
					if (_skewWarned.Add(key))
					{
						Warnings.Add($"矩阵含剪切（skew），已用嵌套子节点精确表达: {key} 误差={skewErr:F4}");
					}
				}
				return new FrameState
				{
					Pos = new XflVec2(px, py),
					Rot = rot,
					Scale = new XflVec2(sx, sy),
					ChildRot = childRot,
					ChildScale = new XflVec2(childSx, childSy),
					SkewErr = skewErr,
					Color = leaf.Color,
					ImageName = leaf.Image,
					Visible = true,
					Z = m.Z,
				};
			}
			return new FrameState
			{
				Pos = prev.Pos,
				Rot = prev.Rot,
				Scale = prev.Scale,
				ChildRot = prev.ChildRot,
				ChildScale = prev.ChildScale,
				SkewErr = prev.SkewErr,
				Color = prev.Color,
				ImageName = prev.ImageName,
				Visible = false,
				Z = prev.Z,
			};
		}

		public List<List<FrameState>> BuildSegmentStates(List<SlotData> slots, SegmentData seg)
		{
			var spriteStates = new List<List<FrameState>>();
			foreach (var slot in slots)
			{
				var states = new List<FrameState>();
				var prev = new FrameState
				{
					Pos = new XflVec2(0, 0),
					Rot = 0,
					Scale = new XflVec2(1, 1),
					Color = XflColor.White,
					Visible = false,
				};
				for (var f = 0; f < seg.Duration; f++)
				{
					var st = GetFrameState(slot, seg.Start + f, prev);
					states.Add(st);
					prev = st;
				}
				
				
				
				
				
				
				for (var i = 1; i < states.Count; i++)
				{
					var raw = states[i].Rot;
					var cRaw = states[i].ChildRot;
					double best = raw, bestDiff = double.MaxValue;
					var bestBranch = 0;
					for (var bi = 0; bi < 3; bi++)
					{
						var cand = raw + (bi == 1 ? Math.PI : (bi == 2 ? -Math.PI : 0.0));
						var dd = cand - states[i - 1].Rot;
						while (dd > Math.PI) { cand -= Math.PI * 2; dd -= Math.PI * 2; }
						while (dd < -Math.PI) { cand += Math.PI * 2; dd += Math.PI * 2; }
						var dist = Math.Abs(dd);
						if (dist < bestDiff) { bestDiff = dist; best = cand; bestBranch = bi; }
					}
					states[i].Rot = best;
					if (bestBranch == 1) states[i].ChildRot = cRaw - Math.PI;
					else if (bestBranch == 2) states[i].ChildRot = cRaw + Math.PI;
					var cdd = states[i].ChildRot - states[i - 1].ChildRot;
					while (cdd > Math.PI) { states[i].ChildRot -= Math.PI * 2; cdd -= Math.PI * 2; }
					while (cdd < -Math.PI) { states[i].ChildRot += Math.PI * 2; cdd += Math.PI * 2; }
				}
				spriteStates.Add(states);
			}
			return spriteStates;
		}

		public List<FrameState> ComputeDefaultStates(List<SlotData> slots)
		{
			var list = new List<FrameState>();
			foreach (var slot in slots) list.Add(ComputeDefaultState(slot));
			return list;
		}

		public FrameState ComputeDefaultState(SlotData slot)
		{
			
			var idle = Segments.Find(s => s.Name == "idle")
				?? (Segments.Count > 0 ? Segments[0] : null);
			var start = idle != null ? idle.Start : 0;
			var end = idle != null ? Math.Min(idle.Start + idle.Duration, TotalFrames) : TotalFrames;

			var members = slot.Members.Count > 0
				? slot.Members
				: new List<MemberRef> { new MemberRef { LayerIndex = slot.LayerIndex, Key = slot.Key } };

			
			int firstVisible = -1;
			for (var g = start; g < end; g++)
			{
				foreach (var m in members)
				{
					var leaves = FrameLeaves[m.LayerIndex][g];
					var leaf = FindLeaf(leaves, m.Key);
					if (leaf != null) { if (firstVisible < 0) firstVisible = g; break; }
				}
				if (firstVisible >= 0) break;
			}

			
			
			string defaultImage = "";
			for (var g = 0; g < TotalFrames && defaultImage == ""; g++)
			{
				foreach (var m in members)
				{
					var leaves = FrameLeaves[m.LayerIndex][g];
					var leaf = FindLeaf(leaves, m.Key);
					if (leaf != null) { defaultImage = leaf.Image; break; }
				}
			}

			var prev = new FrameState
			{
				Pos = new XflVec2(0, 0),
				Rot = 0,
				Scale = new XflVec2(1, 1),
				Color = XflColor.White,
				Visible = false,
			};
			var st = GetFrameState(slot, firstVisible < 0 ? start : firstVisible, prev);
			if (st.ImageName == "") st.ImageName = defaultImage;
			
			
			
			if (firstVisible < 0) st.Z = slot.Z;
			return st;
		}

		
		
		public enum SegmentGroupKind { Single, Phase, Toggle }

		
		public class SegmentGroupInfo
		{
			public string BaseName = "";
			public SegmentGroupKind Kind;
			public List<string> Members = new List<string>();
		}

		
		
		
		
		
		
		public List<SegmentGroupInfo> AnalyzeSegmentGroups()
		{
			var result = new List<SegmentGroupInfo>();
			var used = new HashSet<string>();
			var phaseBases = new Dictionary<string, List<string>>();
			var toggleBases = new Dictionary<string, List<string>>();
			foreach (var seg in Segments)
			{
				var n = seg.Name;
				if (!TrySplitSuffix(n, out var baseName, out var suffix)) continue;
				if (suffix == "start" || suffix == "loop" || suffix == "end")
				{
					used.Add(n);
					AddMember(phaseBases, baseName, n);
				}
				else if (suffix == "on" || suffix == "off")
				{
					used.Add(n);
					AddMember(toggleBases, baseName, n);
				}
			}
			foreach (var kv in phaseBases)
			{
				SortStateMembers(kv.Value);
				result.Add(new SegmentGroupInfo { BaseName = kv.Key, Kind = SegmentGroupKind.Phase, Members = kv.Value });
			}
			foreach (var kv in toggleBases)
			{
				
				var plain = Segments.Find(s => s.Name == kv.Key && !used.Contains(s.Name));
				if (plain != null)
				{
					kv.Value.Add(plain.Name);
					used.Add(plain.Name);
				}
				SortStateMembers(kv.Value);
				result.Add(new SegmentGroupInfo { BaseName = kv.Key, Kind = SegmentGroupKind.Toggle, Members = kv.Value });
			}
			foreach (var seg in Segments)
			{
				if (used.Contains(seg.Name)) continue;
				result.Add(new SegmentGroupInfo { BaseName = seg.Name, Kind = SegmentGroupKind.Single, Members = new List<string> { seg.Name } });
			}
			return result;
		}

		private static bool TrySplitSuffix(string name, out string baseName, out string suffix)
		{
			baseName = "";
			suffix = "";
			var u = name.LastIndexOf('_');
			if (u <= 0 || u == name.Length - 1) return false;
			baseName = name.Substring(0, u);
			suffix = name.Substring(u + 1);
			return true;
		}

		private static void AddMember(Dictionary<string, List<string>> d, string key, string val)
		{
			if (!d.TryGetValue(key, out var l)) { l = new List<string>(); d[key] = l; }
			l.Add(val);
		}

		
		private static void SortStateMembers(List<string> members)
		{
			var order = new Dictionary<string, int> { { "start", 0 }, { "loop", 1 }, { "end", 2 }, { "on", 0 }, { "off", 1 } };
			members.Sort((a, b) =>
			{
				var sa = a.Substring(a.LastIndexOf('_') + 1);
				var sb = b.Substring(b.LastIndexOf('_') + 1);
				var oa = order.TryGetValue(sa, out var x) ? x : 9;
				var ob = order.TryGetValue(sb, out var y) ? y : 9;
				return oa != ob ? oa - ob : string.Compare(a, b, StringComparison.Ordinal);
			});
		}
	}
}
