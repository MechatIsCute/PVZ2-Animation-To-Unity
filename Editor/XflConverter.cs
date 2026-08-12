using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace XflImporter
{
	
	[Serializable]
	public class XflConvertOptions : XflOptions
	{
		
		public bool VisibleToAlpha = false;
		
		public bool LoopClips = false;
		
		public bool PixelArt = false;
		
		public int FrameRate = 30;
		
		
		public float SpritePPU = 50f;
		
		
		
		
		public bool ParticleMerge = false;
		
		
		
		public bool BuildStateMachine = false;
	}

	
	
	
	
	
	
	public class XflConverter
	{
		private static readonly string[] LoopNoneSegments = { "die", "particles", "death" };

		public XflConvertOptions Options = new XflConvertOptions();
		public readonly XflPipeline Pipe = new XflPipeline();

		public List<string> Warnings => Pipe.Warnings;
		public string CharName => Pipe.CharName;
		public List<SegmentData> Segments => Pipe.Segments;

		private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
		private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();
		private readonly HashSet<SlotData> _particleSlots = new HashSet<SlotData>();
		
		
		private readonly HashSet<SlotData> _skewSlots = new HashSet<SlotData>();
		private readonly List<(XflPipeline.ParticleGroupData group, XflParticleData data)> _particleBuilt =
			new List<(XflPipeline.ParticleGroupData, XflParticleData)>();
		private double _frameTime = 1.0 / 30.0;
		private float _pixelScale = 1f / 50f;

		
		public bool Cancelled { get; private set; }

		
		public string ProgressTitle { get; private set; } = "";
		public float Progress { get; private set; }

		
		
		
		
		private bool ShowProgress(string title, float frac)
		{
			ProgressTitle = title;
			Progress = frac;
			XflImporterWindow.ReportProgress(title, frac);
			if (EditorUtility.DisplayCancelableProgressBar("PVZ2XFL → Unity", title, frac))
			{
				Cancelled = true;
				return false;
			}
			return true;
		}

		private static void EndProgressBar()
		{
			EditorUtility.ClearProgressBar();
			XflImporterWindow.EndProgress();
		}

		
		
		
		
		
		
		
		public bool Convert(string srcPath, string outputAbs, XflConvertOptions options, out string message)
		{
			Options = options ?? new XflConvertOptions();
			Pipe.Options = Options;
			message = "";
			Cancelled = false;
			_frameTime = 1.0 / Math.Max(Options.FrameRate, 1);
			_pixelScale = 1f / Mathf.Max(Options.SpritePPU, 1f);
			try
			{
				if (!Pipe.SetupSource(srcPath, out var setupErr))
				{
					message = setupErr;
					return false;
				}
				if (!ShowProgress("解析 XFL 源数据", 0.02f)) { message = "已取消转换"; return false; }

				var outRoot = outputAbs.Replace('\\', '/').TrimEnd('/');
				var texDir = outRoot + "/Textures";
				var animDir = outRoot + "/Animations";
				Directory.CreateDirectory(texDir);
				Directory.CreateDirectory(animDir);

				if (!CopyBitmaps(texDir)) { message = Cancelled ? "已取消转换" : "PNG 素材复制失败，请检查源 LIBRARY 目录"; return false; }
				AssetDatabase.Refresh();
				if (!ImportTextures(texDir)) { message = Cancelled ? "已取消转换" : "PNG 导入失败（输出目录必须在 Assets 下）"; return false; }

				if (!ShowProgress("构建图像名", 0.38f)) { message = "已取消转换"; return false; }
				Pipe.BuildImagePartNames();
				if (!ShowProgress("构建帧叶子", 0.42f)) { message = "已取消转换"; return false; }
				Pipe.BuildFrameLeaves();

				var slots = Pipe.CreateSprites();
				var rawCount = slots.Count;
				for (var i = 0; i < slots.Count; i++) slots[i].Z = i;
				if (Options.MergeSlots) slots = Pipe.MergeSlotLists(slots);
				Pipe.ApplyGrouping(slots);
				if (!ShowProgress("构建槽位与分组", 0.46f)) { message = "已取消转换"; return false; }

				
				
				_particleSlots.Clear();
				_particleBuilt.Clear();
				if (Options.ParticleMerge)
				{
					var candidates = Pipe.BuildParticleGroups(slots);
					var candTotal = Math.Max(candidates.Count, 1);
					for (var ci = 0; ci < candidates.Count; ci++)
					{
						if (!ShowProgress("烘焙粒子数据", 0.48f + 0.10f * (ci / (float)candTotal))) { message = "已取消转换"; return false; }
					var pdata = BuildParticleData(candidates[ci]);
					if (pdata == null) continue;
					foreach (var s in candidates[ci].Slots) _particleSlots.Add(s);
					_particleBuilt.Add((candidates[ci], pdata));
				}
			}

				
				
				if (!ShowProgress("扫描剪切槽位", 0.585f)) { message = "已取消转换"; return false; }
				_skewSlots.Clear();
				foreach (var slot in slots)
				{
					if (_particleSlots.Contains(slot)) continue;
					if (SlotNeedsChild(slot)) _skewSlots.Add(slot);
				}

				var defaults = Pipe.ComputeDefaultStates(slots);
				if (!ShowProgress("计算默认状态", 0.60f)) { message = "已取消转换"; return false; }

				if (!SaveAnimations(animDir, slots)) { message = Cancelled ? "已取消转换" : "动画保存失败"; return false; }
				if (Options.BuildStateMachine)
				{
					if (!ShowProgress("生成状态机", 0.89f)) { message = "已取消转换"; return false; }
					if (!SaveStateMachine(outRoot)) { message = Cancelled ? "已取消转换" : "状态机保存失败"; return false; }
				}
				if (!SavePrefab(outRoot, slots, defaults)) { message = Cancelled ? "已取消转换" : "Prefab 保存失败"; return false; }
				ShowProgress("完成", 1f);

				var msg = $"已生成 {Pipe.Segments.Count} 个动画、{slots.Count} 个 SpriteRenderer";
				if (Options.MergeSlots) msg += $"（合并前 {rawCount} 个）";
				msg += "，Prefab 已保存到 " + outRoot;
				if (Pipe.Warnings.Count > 0)
				{
					msg += "\n\n警告（" + Pipe.Warnings.Count + " 条）：\n" + string.Join("\n", Pipe.Warnings);
				}
				message = msg;
				return true;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				message = "转换异常: " + e;
				return false;
			}
			finally
			{
				EndProgressBar();
			}
		}

		public bool ShouldLoop(string segName)
		{
			return Array.IndexOf(LoopNoneSegments, segName) < 0;
		}

		
		public bool CopyBitmaps(string texDir)
		{
			var ok = true;
			var i = 0;
			var total = Math.Max(Pipe.Doc.Bitmaps.Count, 1);
			foreach (var kv in Pipe.Doc.Bitmaps)
			{
				if (!ShowProgress("复制位图素材", 0.05f + 0.10f * (i / (float)total))) return false;
				var href = kv.Value; 
				var src = Path.Combine(Pipe.LibDirAbs, href);
				var dst = Path.Combine(texDir, href);
				if (File.Exists(src))
				{
					try { File.Copy(src, dst, true); }
					catch (Exception e) { Debug.LogError("复制图片失败: " + src + "\n" + e); ok = false; }
				}
				else
				{
					Debug.LogError("缺少源图片: " + src);
					ok = false;
				}
				i++;
			}
			return ok;
		}

		public bool ImportTextures(string texDir)
		{
			var ok = true;
			_sprites.Clear();
			var pending = new List<(string key, string rel)>();
			
			
			
			
			if (!ShowProgress("导入纹理", 0.15f)) return false;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (var kv in Pipe.Doc.Bitmaps)
				{
					var name = kv.Key;
					var rel = (texDir + "/" + kv.Value).Replace('\\', '/');
					if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
					{
						Debug.LogError("输出目录必须在 Assets 下，无法导入纹理: " + rel);
						ok = false;
						continue;
					}
					var importer = AssetImporter.GetAtPath(rel) as TextureImporter;
					if (importer == null)
					{
						Debug.LogError("无法获取纹理导入器（资源尚未刷新？）: " + rel);
						ok = false;
						continue;
					}
					importer.textureType = TextureImporterType.Sprite;
					importer.spriteImportMode = SpriteImportMode.Single;
					
					
					
					
					
					importer.spritePivot = new Vector2(0f, 1f);
					importer.spritePixelsPerUnit = Options.SpritePPU;
					importer.filterMode = Options.PixelArt ? FilterMode.Point : FilterMode.Bilinear;
					importer.textureCompression = TextureImporterCompression.Uncompressed;
					importer.mipmapEnabled = false;
					var so = new SerializedObject(importer);
					var alignProp = so.FindProperty("m_Alignment");
					if (alignProp != null) alignProp.intValue = 9; 
					so.ApplyModifiedProperties();
					importer.SaveAndReimport();
					pending.Add((name, rel));
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			
			var j = 0;
			var loadTotal = Math.Max(pending.Count, 1);
			foreach (var p in pending)
			{
				if (!ShowProgress("加载精灵", 0.31f + 0.04f * (j / (float)loadTotal))) return false;
				var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p.rel);
				if (sprite == null)
				{
					Debug.LogError("Sprite 加载失败: " + p.rel);
					ok = false;
					continue;
				}
				_sprites[p.key] = sprite;
				j++;
			}
			return ok;
		}

		private Sprite ResolveSprite(string imageName)
		{
			if (string.IsNullOrEmpty(imageName)) return null;
			return _sprites.TryGetValue(imageName, out var s) ? s : null;
		}

		
		public bool SaveAnimations(string animDir, List<SlotData> slots)
		{
			var ok = true;
			_clips.Clear();
			var i = 0;
			var total = Math.Max(Pipe.Segments.Count, 1);
			foreach (var seg in Pipe.Segments)
			{
				if (!ShowProgress("生成动画剪辑 " + seg.Name, 0.60f + 0.28f * (i / (float)total))) return false;
				var states = Pipe.BuildSegmentStates(slots, seg);
				var clip = BuildAnimationClip(seg, slots, states);
				
				
				
				var path = animDir + "/" + seg.Name + ".anim";
				var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
				if (old != null) AssetDatabase.DeleteAsset(path);
				AssetDatabase.CreateAsset(clip, path);
				
				
				
				var loaded = AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? clip;
				if (loaded == null)
				{
					Debug.LogError("动画资产创建失败: " + path);
					ok = false;
					break;
				}
				
				_clips[seg.Name] = loaded;
				i++;
			}
			AssetDatabase.SaveAssets();
			return ok;
		}

		
		
		private bool SlotNeedsChild(SlotData slot)
		{
			var members = slot.Members.Count > 0
				? slot.Members
				: new List<MemberRef> { new MemberRef { LayerIndex = slot.LayerIndex, Key = slot.Key } };
			var prev = new FrameState
			{
				Pos = new XflVec2(0, 0),
				Rot = 0,
				Scale = new XflVec2(1, 1),
				Color = XflColor.White,
				Visible = false,
			};
			for (var g = 0; g < Pipe.TotalFrames; g++)
			{
				var st = Pipe.GetFrameState(slot, g, prev);
				if (st.Visible)
				{
					if (Math.Abs(st.ChildRot) > 0.001) return true;
					if (Math.Abs(st.ChildScale.X - 1.0) > 0.001 || Math.Abs(st.ChildScale.Y - 1.0) > 0.001) return true;
				}
				prev = st;
			}
			return false;
		}

		public AnimationClip BuildAnimationClip(SegmentData seg, List<SlotData> slots, List<List<FrameState>> states)
		{
			var clip = new AnimationClip();
			
			
			clip.name = seg.Name;
			clip.frameRate = Mathf.Max(Options.FrameRate, 1);
			clip.legacy = true;
			clip.wrapMode = Options.LoopClips && ShouldLoop(seg.Name) ? WrapMode.Loop : WrapMode.ClampForever;

			
			
			var bindings = new List<EditorCurveBinding>();
			var curves = new List<AnimationCurve>();

			for (var si = 0; si < slots.Count; si++)
			{
				var slot = slots[si];
				
				if (_particleSlots.Contains(slot)) continue;
				var st = states[si];
				var path = slot.GroupName != "" ? slot.GroupName + "/" + slot.Name : slot.Name;
				
				
				var needsChild = _skewSlots.Contains(slot);
				var childPath = path + "/Sprite";
				var renderPath = needsChild ? childPath : path;

				var everVisible = false;
				foreach (var s in st) if (s.Visible) { everVisible = true; break; }

				if (!everVisible)
				{
					if (Options.VisibleToAlpha) AddColorTrack(clip, renderPath, st, true, bindings, curves);
					else AddEnabledTrack(clip, renderPath, st, bindings, curves);
					continue;
				}
				AddVec2Track(clip, path, "m_LocalPosition.x", "m_LocalPosition.y", st, s => new Vector2((float)s.Pos.X * _pixelScale, (float)s.Pos.Y * _pixelScale), VecEq, bindings, curves);
				AddRotationTrack(clip, path, st, bindings, curves);
				AddVec2Track(clip, path, "m_LocalScale.x", "m_LocalScale.y", st, s => new Vector2((float)s.Scale.X, (float)s.Scale.Y), VecEq, bindings, curves);
				if (needsChild)
				{
					AddRotationTrack(clip, childPath, st, s => s.ChildRot, bindings, curves);
					AddVec2Track(clip, childPath, "m_LocalScale.x", "m_LocalScale.y", st, s => new Vector2((float)s.ChildScale.X, (float)s.ChildScale.Y), VecEq, bindings, curves);
				}
				if (Options.VisibleToAlpha) AddColorTrack(clip, renderPath, st, true, bindings, curves);
				else
				{
					AddEnabledTrack(clip, renderPath, st, bindings, curves);
					AddColorTrack(clip, renderPath, st, false, bindings, curves);
				}
				
				
				
				AddSortingOrderTrack(clip, renderPath, slot, st, bindings, curves);
			}
			AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
			return clip;
		}

		
		
		
		
		
		
		private XflParticleData BuildParticleData(XflPipeline.ParticleGroupData group)
		{
			var sprite = ResolveSprite(group.ImageName);
			if (sprite == null || group.Slots.Count == 0) return null;
			
			
			var idle = Pipe.Segments.Find(s => s.Name == "idle")
				?? (Pipe.Segments.Count > 0 ? Pipe.Segments[0] : null);
			if (idle != null)
			{
				var idleEnd = Math.Min(idle.Start + idle.Duration, Pipe.TotalFrames);
				for (var g = idle.Start; g < idleEnd; g++)
				{
					foreach (var s in group.Slots)
					{
						var members = s.Members.Count > 0 ? s.Members
							: new List<MemberRef> { new MemberRef { LayerIndex = s.LayerIndex, Key = s.Key } };
						foreach (var m in members)
						{
							if (XflPipeline.FindLeaf(Pipe.FrameLeaves[m.LayerIndex][g], m.Key) != null) return null;
						}
					}
				}
			}
			var data = ScriptableObject.CreateInstance<XflParticleData>();
			data.groupName = group.GroupName;
			data.sprite = sprite;
			data.particleCount = group.Slots.Count;
			data.frameRate = Options.FrameRate;
			
			
			
			

			foreach (var seg in Pipe.Segments)
			{
				var pseg = new XflParticleSegment { segmentName = seg.Name };
				var prev = new FrameState[group.Slots.Count];
				for (var pi = 0; pi < prev.Length; pi++)
				{
					prev[pi] = new FrameState
					{
						Pos = new XflVec2(0, 0),
						Rot = 0,
						Scale = new XflVec2(1, 1),
						Color = XflColor.White,
						Visible = false,
					};
				}
				for (var f = 0; f < seg.Duration; f++)
				{
					var g = seg.Start + f;
					var states = new XflParticleState[group.Slots.Count];
					for (var pi = 0; pi < group.Slots.Count; pi++)
					{
						var st = Pipe.GetFrameState(group.Slots[pi], g, prev[pi]);
						prev[pi] = st;
						if (!st.Visible)
						{
							states[pi].active = false;
							continue;
						}
						var (theta, kAvg, ratio) = PolarDecompose(st);
						if (ratio > 1.67f) return null;
						states[pi].active = true;
						states[pi].pos = new Vector2((float)st.Pos.X * _pixelScale, (float)st.Pos.Y * _pixelScale);
						states[pi].rot = (float)theta;
						states[pi].size = (float)kAvg;
						states[pi].color = ToUnityColor(st.Color);
					}
					pseg.frames.Add(new XflParticleFrame { states = states });
				}
				data.segments.Add(pseg);
			}
			return data;
		}

		
		
		
		
		
		
		private static (float theta, float kAvg, float ratio) PolarDecompose(FrameState st)
		{
			var r = (float)st.Rot;
			var cr = (float)st.ChildRot;
			var sx = (float)st.Scale.X;
			var sy = (float)st.Scale.Y;
			var csx = (float)st.ChildScale.X;
			var csy = (float)st.ChildScale.Y;
			
			var a1 = Mathf.Cos(r) * sx; var b1 = -Mathf.Sin(r) * sy;
			var c1 = Mathf.Sin(r) * sx; var d1 = Mathf.Cos(r) * sy;
			
			var a2 = Mathf.Cos(cr) * csx; var b2 = -Mathf.Sin(cr) * csy;
			var c2 = Mathf.Sin(cr) * csx; var d2 = Mathf.Cos(cr) * csy;
			
			var ma = a1 * a2 + b1 * c2;
			var mb = a1 * b2 + b1 * d2;
			var mc = c1 * a2 + d1 * c2;
			var md = c1 * b2 + d1 * d2;
			
			var j00 = ma * ma + mc * mc;
			var j01 = ma * mb + mc * md;
			var j11 = mb * mb + md * md;
			var tr = j00 + j11;
			var det = j00 * j11 - j01 * j01;
			var disc = Mathf.Sqrt(Mathf.Max(tr * tr * 0.25f - det, 0f));
			var l1 = tr * 0.5f + disc;
			var l2 = Mathf.Max(tr * 0.5f - disc, 0f);
			var k1 = Mathf.Sqrt(Mathf.Max(l1, 0f));
			var k2 = Mathf.Sqrt(l2);
			
			var vx = l1 - j11;
			var vy = j01;
			var vn = Mathf.Sqrt(vx * vx + vy * vy);
			if (vn > 1e-6f) { vx /= vn; vy /= vn; }
			else { vx = 1f; vy = 0f; }
			var k1Safe = k1 > 1e-6f ? k1 : 1f;
			var ux = (ma * vx + mb * vy) / k1Safe;
			var uy = (mc * vx + md * vy) / k1Safe;
			var theta = Mathf.Atan2(uy, ux);
			var kAvg = Mathf.Sqrt(k1 * k2);
			var minK = Mathf.Min(k1, k2) > 1e-6f ? Mathf.Min(k1, k2) : 1f;
			var ratio = Mathf.Max(k1, k2) / minK;
			return (theta, kAvg, ratio);
		}

		
		private static Mesh BuildSpriteMesh(Sprite sprite)
		{
			var mesh = new Mesh();
			var verts = sprite.vertices;
			var v3 = new Vector3[verts.Length];
			for (var i = 0; i < verts.Length; i++) v3[i] = verts[i];
			mesh.vertices = v3;
			mesh.uv = sprite.uv;
			var tris = sprite.triangles;
			var triInt = new int[tris.Length];
			for (var i = 0; i < tris.Length; i++) triInt[i] = tris[i];
			mesh.triangles = triInt;
			var n = verts.Length;
			var normals = new Vector3[n];
			var tangents = new Vector4[n];
			var colors = new Color[n];
			for (var i = 0; i < n; i++)
			{
				normals[i] = Vector3.forward;
				tangents[i] = new Vector4(1f, 0f, 0f, 1f);
				colors[i] = Color.white;
			}
			mesh.normals = normals;
			mesh.tangents = tangents;
			mesh.colors = colors;
			mesh.RecalculateBounds();
			return mesh;
		}

		
		private static Material CreateParticleMaterial(Sprite sprite)
		{
			var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
			if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
			if (shader == null) shader = Shader.Find("Sprites/Default");
			var mat = new Material(shader);
			if (shader.name.Contains("Universal") || shader.name.Contains("Standard"))
			{
				mat.SetTexture("_BaseMap", sprite.texture);
				
				
				if (shader.name.Contains("Universal"))
				{
					mat.SetFloat("_Surface", 1f);
					mat.SetFloat("_Blend", 0f);
					mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
					mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					mat.SetFloat("_ZWrite", 0f);
					mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
					mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
					mat.EnableKeyword("_BLENDMODE_ALPHA");
				}
			}
			else
			{
				mat.SetTexture("_MainTex", sprite.texture);
				mat.SetTexture("mainTexture", sprite.texture);
			}
			return mat;
		}

		
		private static void SetupParticleSystem(GameObject go, XflParticleData data)
		{
			var ps = go.AddComponent<ParticleSystem>();
			var main = ps.main;
			main.playOnAwake = false;
			main.loop = false;
			main.simulationSpace = ParticleSystemSimulationSpace.Local;
			main.startLifetime = 1f;
			main.startSpeed = 0f;
			main.maxParticles = Mathf.Max(data.particleCount, 1);
			var emission = ps.emission;
			emission.rateOverTime = 0f;
			emission.rateOverDistance = 0f;
			var shape = ps.shape;
			shape.enabled = false;
			var psr = go.GetComponent<ParticleSystemRenderer>();
			psr.renderMode = ParticleSystemRenderMode.Mesh;
			psr.mesh = BuildSpriteMesh(data.sprite);
			psr.sharedMaterial = CreateParticleMaterial(data.sprite);
		}

		private static int AvgZ(List<SlotData> group)
		{
			float sum = 0f;
			foreach (var s in group) sum += s.Z;
			return group.Count > 0 ? Mathf.RoundToInt(sum / group.Count) : 0;
		}

		
		private float T(int frame) => frame * (float)_frameTime;

		private static void SetCurve(AnimationClip clip, string path, Type type, string prop, AnimationCurve curve,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			if (curve == null || curve.length == 0) return;
			bindings.Add(new EditorCurveBinding { path = path, type = type, propertyName = prop });
			curves.Add(curve);
		}

		private static Keyframe StepKey(float time, float value)
		{
			var k = new Keyframe(time, value);
			k.inTangent = float.PositiveInfinity;
			k.outTangent = float.PositiveInfinity;
			return k;
		}

		
		private static AnimationCurve CurveFromKeys(List<Keyframe> keys, bool linear)
		{
			var curve = new AnimationCurve(keys.ToArray());
			if (!linear || curve.length < 2) return curve;
			for (var i = 0; i < curve.length; i++)
			{
				var outSlope = 0f;
				var inSlope = 0f;
				if (i < curve.length - 1)
				{
					outSlope = (curve[i + 1].value - curve[i].value) /
						Mathf.Max(curve[i + 1].time - curve[i].time, 1e-6f);
				}
				if (i > 0)
				{
					inSlope = (curve[i].value - curve[i - 1].value) /
						Mathf.Max(curve[i].time - curve[i - 1].time, 1e-6f);
				}
				var k = curve[i];
				k.outTangent = outSlope;
				k.inTangent = inSlope;
				curve.MoveKey(i, k);
			}
			return curve;
		}

		
		
		
		
		
		private List<Keyframe> CollectKeys(List<FrameState> states, Func<FrameState, float> getter,
			Func<float, float, bool> eq, bool skipInvisible)
		{
			var keys = new List<Keyframe>();
			if (skipInvisible)
			{
				var vis = new List<int>();
				for (var f = 0; f < states.Count; f++) if (states[f].Visible) vis.Add(f);
				if (vis.Count == 0) return keys;
				float? last = null;
				for (var vi = 0; vi < vis.Count; vi++)
				{
					var f = vis[vi];
					var v = getter(states[f]);
					var memberStart = vi == 0 || states[vis[vi - 1]].Z != states[f].Z;
					var memberEnd = vi == vis.Count - 1 || states[vis[vi + 1]].Z != states[f].Z;
					var valueJumpsNext = vi < vis.Count - 1 && !eq(v, getter(states[vis[vi + 1]]));
					if (!last.HasValue || !eq(v, last.Value) || memberStart || memberEnd || valueJumpsNext)
					{
						keys.Add(new Keyframe(T(f), v));
						last = v;
					}
				}
				if (states.Count > 1 && states[states.Count - 1].Visible)
				{
					var endTime = T(states.Count - 1);
					if (keys.Count == 0 || keys[keys.Count - 1].time < endTime - 1e-5f)
					{
						keys.Add(new Keyframe(endTime, getter(states[states.Count - 1])));
					}
				}
			}
			else
			{
				float? last = null;
				for (var f = 0; f < states.Count; f++)
				{
					var v = getter(states[f]);
					if (!last.HasValue || !eq(v, last.Value))
					{
						keys.Add(StepKey(T(f), v));
						last = v;
					}
				}
				if (states.Count > 1)
				{
					var endTime = T(states.Count - 1);
					if (keys.Count == 0 || keys[keys.Count - 1].time < endTime - 1e-5f)
					{
						keys.Add(StepKey(endTime, getter(states[states.Count - 1])));
					}
				}
			}
			return keys;
		}

		private void AddVec2Track(AnimationClip clip, string path, string propX, string propY,
			List<FrameState> states, Func<FrameState, Vector2> getter, Func<Vector2, Vector2, bool> eq,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			var keysX = new List<Keyframe>();
			var keysY = new List<Keyframe>();
			var vis = new List<int>();
			for (var f = 0; f < states.Count; f++) if (states[f].Visible) vis.Add(f);
			if (vis.Count == 0) return;
			Vector2? last = null;
			for (var vi = 0; vi < vis.Count; vi++)
			{
				var f = vis[vi];
				var v = getter(states[f]);
				var memberStart = vi == 0 || states[vis[vi - 1]].Z != states[f].Z;
				var memberEnd = vi == vis.Count - 1 || states[vis[vi + 1]].Z != states[f].Z;
				var valueJumpsNext = vi < vis.Count - 1 && !eq(v, getter(states[vis[vi + 1]]));
				if (!last.HasValue || !eq(v, last.Value) || memberStart || memberEnd || valueJumpsNext)
				{
					var t = T(f);
					keysX.Add(new Keyframe(t, v.x));
					keysY.Add(new Keyframe(t, v.y));
					last = v;
				}
			}
			if (states.Count > 1 && states[states.Count - 1].Visible)
			{
				var endTime = T(states.Count - 1);
				if (keysX.Count == 0 || keysX[keysX.Count - 1].time < endTime - 1e-5f)
				{
					var ev = getter(states[states.Count - 1]);
					keysX.Add(new Keyframe(endTime, ev.x));
					keysY.Add(new Keyframe(endTime, ev.y));
				}
			}
			SetCurve(clip, path, typeof(Transform), propX, CurveFromKeys(keysX, true), bindings, curves);
			SetCurve(clip, path, typeof(Transform), propY, CurveFromKeys(keysY, true), bindings, curves);
		}

		private void AddRotationTrack(AnimationClip clip, string path, List<FrameState> states,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			AddRotationTrack(clip, path, states, s => s.Rot, bindings, curves);
		}

		
		private void AddRotationTrack(AnimationClip clip, string path, List<FrameState> states, Func<FrameState, double> angle,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			var vis = new List<int>();
			for (var f = 0; f < states.Count; f++) if (states[f].Visible) vis.Add(f);
			if (vis.Count == 0) return;
			var keysZ = new List<Keyframe>();
			var keysW = new List<Keyframe>();
			float? last = null;
			for (var vi = 0; vi < vis.Count; vi++)
			{
				var f = vis[vi];
				var rad = (float)angle(states[f]);
				var memberStart = vi == 0 || states[vis[vi - 1]].Z != states[f].Z;
				var memberEnd = vi == vis.Count - 1 || states[vis[vi + 1]].Z != states[f].Z;
				var valueJumpsNext = vi < vis.Count - 1 && !FloatEq(rad, (float)angle(states[vis[vi + 1]]));
				if (!last.HasValue || !FloatEq(rad, last.Value) || memberStart || memberEnd || valueJumpsNext)
				{
					var t = T(f);
					var half = rad * 0.5f;
					keysZ.Add(new Keyframe(t, Mathf.Sin(half)));
					keysW.Add(new Keyframe(t, Mathf.Cos(half)));
					last = rad;
				}
			}
			if (states.Count > 1 && states[states.Count - 1].Visible)
			{
				var endTime = T(states.Count - 1);
				if (keysZ.Count == 0 || keysZ[keysZ.Count - 1].time < endTime - 1e-5f)
				{
					var half = (float)angle(states[states.Count - 1]) * 0.5f;
					keysZ.Add(new Keyframe(endTime, Mathf.Sin(half)));
					keysW.Add(new Keyframe(endTime, Mathf.Cos(half)));
				}
			}
			SetCurve(clip, path, typeof(Transform), "m_LocalRotation.z", CurveFromKeys(keysZ, true), bindings, curves);
			SetCurve(clip, path, typeof(Transform), "m_LocalRotation.w", CurveFromKeys(keysW, true), bindings, curves);
		}

		private void AddEnabledTrack(AnimationClip clip, string path, List<FrameState> states,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			var keys = CollectKeys(states, s => s.Visible ? 1f : 0f, (a, b) => Mathf.Abs(a - b) < 0.5f, false);
			if (keys.Count == 0) return;
			SetCurve(clip, path, typeof(SpriteRenderer), "m_Enabled", new AnimationCurve(keys.ToArray()), bindings, curves);
		}

		
		
		
		
		
		
		private void AddSortingOrderTrack(AnimationClip clip, string path, SlotData slot, List<FrameState> states,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			if (slot.Members.Count < 2) return;
			var anyDiff = false;
			foreach (var m in slot.Members)
			{
				if (m.Z != slot.Z) { anyDiff = true; break; }
			}
			if (!anyDiff) return;
			var keys = new List<Keyframe>();
			int? lastZ = null;
			for (var f = 0; f < states.Count; f++)
			{
				if (!states[f].Visible) continue;
				var z = states[f].Z;
				if (!lastZ.HasValue || z != lastZ.Value)
				{
					keys.Add(StepKey(T(f), z));
					lastZ = z;
				}
			}
			if (keys.Count == 0) return;
			if (states.Count > 1 && states[states.Count - 1].Visible)
			{
				var endTime = T(states.Count - 1);
				if (keys[keys.Count - 1].time < endTime - 1e-5f)
				{
					keys.Add(StepKey(endTime, states[states.Count - 1].Z));
				}
			}
			SetCurve(clip, path, typeof(SpriteRenderer), "m_SortingOrder", new AnimationCurve(keys.ToArray()), bindings, curves);
		}

		
		
		
		
		private void AddColorTrack(AnimationClip clip, string path, List<FrameState> states, bool visibleToAlpha,
			List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
		{
			if (visibleToAlpha)
			{
				var keysR = new List<Keyframe>();
				var keysG = new List<Keyframe>();
				var keysB = new List<Keyframe>();
				var keysA = new List<Keyframe>();
				var n = states.Count;
				XflColor? lastKey = null;
				for (var f = 0; f < n; f++)
				{
					var vis = states[f].Visible;
					var col = new XflColor
					{
						R = states[f].Color.R,
						G = states[f].Color.G,
						B = states[f].Color.B,
						A = vis ? states[f].Color.A : 0.0,
					};
					var prevVis = f > 0 ? (bool?)states[f - 1].Visible : null;
					var nextVis = f < n - 1 ? (bool?)states[f + 1].Visible : null;
					var rangeStart = prevVis.HasValue && prevVis.Value != vis;
					var rangeEnd = nextVis.HasValue && nextVis.Value != vis;
					var valueChanged = lastKey.HasValue && !ColEq(col, lastKey.Value);
					var nextCol = f < n - 1 ? (XflColor?)new XflColor
					{
						R = states[f + 1].Color.R,
						G = states[f + 1].Color.G,
						B = states[f + 1].Color.B,
						A = states[f + 1].Visible ? states[f + 1].Color.A : 0.0,
					} : null;
					var valueJumpsNext = nextCol.HasValue && !ColEq(col, nextCol.Value);
					if (f == 0 || rangeStart || rangeEnd || valueChanged || valueJumpsNext || f == n - 1)
					{
						var t = T(f);
						keysR.Add(new Keyframe(t, (float)col.R));
						keysG.Add(new Keyframe(t, (float)col.G));
						keysB.Add(new Keyframe(t, (float)col.B));
						keysA.Add(new Keyframe(t, (float)col.A));
						lastKey = col;
					}
				}
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.r", CurveFromKeys(keysR, true), bindings, curves);
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.g", CurveFromKeys(keysG, true), bindings, curves);
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.b", CurveFromKeys(keysB, true), bindings, curves);
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.a", CurveFromKeys(keysA, true), bindings, curves);
			}
			else
			{
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.r",
					CurveFromKeys(CollectKeys(states, s => (float)s.Color.R, FloatEq, true), true), bindings, curves);
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.g",
					CurveFromKeys(CollectKeys(states, s => (float)s.Color.G, FloatEq, true), true), bindings, curves);
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.b",
					CurveFromKeys(CollectKeys(states, s => (float)s.Color.B, FloatEq, true), true), bindings, curves);
				SetCurve(clip, path, typeof(SpriteRenderer), "m_Color.a",
					CurveFromKeys(CollectKeys(states, s => (float)s.Color.A, FloatEq, true), true), bindings, curves);
			}
		}



		
		private static Color ToUnityColor(XflColor c) => new Color((float)c.R, (float)c.G, (float)c.B, (float)c.A);

		private static bool VecEq(Vector2 a, Vector2 b)
		{
			return Mathf.Abs(a.x - b.x) < 1e-4f && Mathf.Abs(a.y - b.y) < 1e-4f;
		}

		private static bool FloatEq(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

		private static bool ColEq(XflColor a, XflColor b)
		{
			return Mathf.Abs((float)(a.R - b.R)) < 1e-4f && Mathf.Abs((float)(a.G - b.G)) < 1e-4f &&
				Mathf.Abs((float)(a.B - b.B)) < 1e-4f && Mathf.Abs((float)(a.A - b.A)) < 1e-4f;
		}

		
		public bool SavePrefab(string outRoot, List<SlotData> slots, List<FrameState> defaults)
		{
			if (!ShowProgress("保存 Prefab", 0.90f)) return false;
			var root = new GameObject(CharName);
			
			
			var rootSG = root.AddComponent<SortingGroup>();
			rootSG.sortingOrder = 0;
			try
			{
				var animGO = new GameObject("Animation");
				animGO.transform.SetParent(root.transform, false);
				
				
				
				
				var animSG = animGO.AddComponent<SortingGroup>();
				animSG.sortingOrder = 0;
				animGO.transform.localPosition = new Vector3(-(float)Pipe.Extra.Origin.X * _pixelScale, (float)Pipe.Extra.Origin.Y * _pixelScale, 0f);

				var topItems = new List<(float z, GameObject go)>();
				var groups = new Dictionary<string, List<SlotData>>();
				
				
				var slotGOs = new Dictionary<SlotData, GameObject>();
			for (var si = 0; si < slots.Count; si++)
			{
				if (si % 16 == 0 && !ShowProgress("保存 Prefab", 0.90f + 0.05f * (si / (float)Math.Max(slots.Count, 1)))) return false;
				var slot = slots[si];
				
				if (_particleSlots.Contains(slot)) continue;
				if (slot.GroupName == "")
				{
					var go = CreateSlotGO(slot);
					slotGOs[slot] = go;
					go.transform.SetParent(animGO.transform, false);
					topItems.Add((slot.Z, go));
				}
				else
				{
					if (!groups.TryGetValue(slot.GroupName, out var list))
					{
						list = new List<SlotData>();
						groups[slot.GroupName] = list;
					}
					list.Add(slot);
				}
			}
				foreach (var kv in groups)
				{
					var ggo = new GameObject(kv.Key);
					
					kv.Value.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
					foreach (var s in kv.Value)
					{
						var go = CreateSlotGO(s);
						slotGOs[s] = go;
						go.transform.SetParent(ggo.transform, false);
					}
					ggo.transform.SetParent(animGO.transform, false);
					topItems.Add((AvgZ(kv.Value), ggo));
				}
				
				
				if (!ShowProgress("创建粒子节点", 0.93f)) return false;
				foreach (var pb in _particleBuilt)
				{
					var pgo = new GameObject(pb.group.GroupName);
					SetupParticleSystem(pgo, pb.data);
					
					pgo.GetComponent<ParticleSystemRenderer>().sortingOrder = AvgZ(pb.group.Slots);
					var driver = pgo.AddComponent<XflParticleDriver>();
					driver.data = pb.data;
					driver.frameRate = Options.FrameRate;
					pgo.transform.SetParent(animGO.transform, false);
					var pdir = outRoot + "/Particles";
					Directory.CreateDirectory(pdir);
					var pPath = pdir + "/" + pb.group.GroupName + ".asset";
					var oldData = AssetDatabase.LoadAssetAtPath<XflParticleData>(pPath);
					if (oldData != null) AssetDatabase.DeleteAsset(pPath);
					AssetDatabase.CreateAsset(pb.data, pPath);
					topItems.Add((AvgZ(pb.group.Slots), pgo));
				}
				
				topItems.Sort((a, b) => string.Compare(a.go.name, b.go.name, StringComparison.Ordinal));
				foreach (var it in topItems) it.go.transform.SetAsLastSibling();

				
				for (var i = 0; i < slots.Count; i++)
				{
					if (i % 16 == 0 && !ShowProgress("应用默认状态", 0.95f + 0.03f * (i / (float)Math.Max(slots.Count, 1)))) return false;
					var slot = slots[i];
					var st = defaults[i];
					var go = slotGOs.TryGetValue(slot, out var slotGo) ? slotGo : null;
					if (go == null) continue;
					go.transform.localPosition = new Vector3((float)st.Pos.X * _pixelScale, (float)st.Pos.Y * _pixelScale, 0f);
					go.transform.localRotation = Quaternion.Euler(0f, 0f, (float)st.Rot * Mathf.Rad2Deg);
					go.transform.localScale = new Vector3((float)st.Scale.X, (float)st.Scale.Y, 1f);
					
					
					var spriteTr = go.transform.Find("Sprite");
					if (spriteTr != null)
					{
						spriteTr.localPosition = Vector3.zero;
						spriteTr.localRotation = Quaternion.Euler(0f, 0f, (float)st.ChildRot * Mathf.Rad2Deg);
						spriteTr.localScale = new Vector3((float)st.ChildScale.X, (float)st.ChildScale.Y, 1f);
					}
					var sr = spriteTr != null ? spriteTr.GetComponent<SpriteRenderer>() : go.GetComponent<SpriteRenderer>();
					if (sr != null)
					{
						sr.sprite = ResolveSprite(st.ImageName);
						if (Options.VisibleToAlpha)
						{
							sr.enabled = true;
							sr.color = new Color((float)st.Color.R, (float)st.Color.G, (float)st.Color.B,
								st.Visible ? (float)st.Color.A : 0f);
						}
						else
						{
							sr.enabled = st.Visible;
							sr.color = ToUnityColor(st.Color);
						}
					}
					
					if (sr != null) sr.sortingOrder = st.Z;
				}

				
				
				
				
				var center = ComputeVisibleCenter(animGO);
				if (center.HasValue) animGO.transform.position -= center.Value;

				
				
				
				if (Options.BuildStateMachine)
				{
					var animator = animGO.AddComponent<Animator>();
					var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(
						outRoot + "/" + CharName + "_StateMachine.controller");
					animator.runtimeAnimatorController = ctrl;
				}
				else
				{
					
					
					
					
					
					var anim = animGO.AddComponent<Animation>();
					var clipsToMount = new List<AnimationClip>();
					AnimationClip firstClip = null;
					foreach (var seg in Pipe.Segments)
					{
						if (!_clips.TryGetValue(seg.Name, out var clip) || clip == null) continue;
						clipsToMount.Add(clip);
						if (firstClip == null) firstClip = clip;
					}
					var animSo = new SerializedObject(anim);
					var animArr = animSo.FindProperty("m_Animations");
					if (animArr != null)
					{
						animArr.arraySize = clipsToMount.Count;
						for (var ci = 0; ci < clipsToMount.Count; ci++)
						{
							animArr.GetArrayElementAtIndex(ci).objectReferenceValue = clipsToMount[ci];
						}
						animSo.ApplyModifiedProperties();
					}
					
					
					if (firstClip != null)
					{
						anim.clip = firstClip;
						anim.playAutomatically = true;
					}
				}

				var prefabPath = outRoot + "/" + CharName + ".prefab";
				var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
				if (existing != null) AssetDatabase.DeleteAsset(prefabPath);
				PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				return true;
			}
			catch (Exception e)
			{
				Debug.LogError("Prefab 保存失败: " + e);
				return false;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		
		private bool SaveStateMachine(string outRoot)
		{
			if (!Options.BuildStateMachine) return true;
			var groups = Pipe.AnalyzeSegmentGroups();
			if (groups.Count == 0) return true;

			var ctrlPath = outRoot + "/" + CharName + "_StateMachine.controller";
			var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
			if (old != null) AssetDatabase.DeleteAsset(ctrlPath);
			var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
			if (ctrl == null) { Debug.LogError("状态机创建失败: " + ctrlPath); return false; }
			var baseSm = ctrl.layers[0].stateMachine;

			var meciDir = outRoot + "/Mecanim";
			Directory.CreateDirectory(meciDir);

			AnimatorState first = null;
			foreach (var g in groups)
			{
				
				var subName = g.BaseName == "" ? "Group" : g.BaseName;
				subName = baseSm.MakeUniqueStateMachineName(subName);
				var sub = baseSm.AddStateMachine(subName);

				
				
				var byName = new Dictionary<string, AnimatorState>();
				AnimatorState subFirst = null;
				foreach (var segName in g.Members)
				{
					if (!_clips.TryGetValue(segName, out var legacy) || legacy == null) continue;
					var meciClip = SaveMecanimClip(meciDir, segName, legacy);
					if (meciClip == null) continue;
					var st = sub.AddState(segName);
					st.motion = meciClip;
					byName[segName] = st;
					if (subFirst == null) subFirst = st;
					if (first == null) first = st;
					if (!HasParameter(ctrl, segName)) ctrl.AddParameter(segName, AnimatorControllerParameterType.Bool);
					var any = sub.AddAnyStateTransition(st);
					any.hasExitTime = false;
					any.AddCondition(AnimatorConditionMode.If, 0f, segName);
				}

				
				if (g.Kind == XflPipeline.SegmentGroupKind.Phase) BuildPhaseTransitions(byName);
				else if (g.Kind == XflPipeline.SegmentGroupKind.Toggle) BuildToggleTransitions(byName, g.Members);
				else BuildSingleLoop(byName);

				if (subFirst != null) sub.defaultState = subFirst;
			}
			if (first != null) baseSm.defaultState = first;
			AssetDatabase.SaveAssets();
			return true;
		}

		
		private static void BuildPhaseTransitions(Dictionary<string, AnimatorState> byName)
		{
			AnimatorState start = null, loop = null, end = null;
			foreach (var kv in byName)
			{
				var suffix = kv.Key.Substring(kv.Key.LastIndexOf('_') + 1);
				if (suffix == "start") start = kv.Value;
				else if (suffix == "loop") loop = kv.Value;
				else if (suffix == "end") end = kv.Value;
			}
			if (start != null && loop != null)
			{
				var t = start.AddTransition(loop);
				t.hasExitTime = true; 
				t.duration = 0f;
			}
			if (loop != null)
			{
				var self = loop.AddTransition(loop);
				self.hasExitTime = false; 
			}
			if (loop != null && end != null)
			{
				var t = loop.AddTransition(end);
				t.hasExitTime = false;
				t.AddCondition(AnimatorConditionMode.If, 0f, end.name); 
			}
		}

		
		private static void BuildToggleTransitions(Dictionary<string, AnimatorState> byName, List<string> members)
		{
			var list = new List<AnimatorState>();
			foreach (var m in members) if (byName.TryGetValue(m, out var st)) list.Add(st);
			for (var i = 0; i < list.Count; i++)
			{
				for (var j = 0; j < list.Count; j++)
				{
					if (i == j) continue;
					var t = list[i].AddTransition(list[j]);
					t.hasExitTime = false;
					t.AddCondition(AnimatorConditionMode.If, 0f, list[j].name);
				}
			}
		}

		
		private static void BuildSingleLoop(Dictionary<string, AnimatorState> byName)
		{
			foreach (var st in byName.Values)
			{
				var self = st.AddTransition(st);
				self.hasExitTime = false;
			}
		}

		private static bool HasParameter(AnimatorController ctrl, string name)
		{
			foreach (var p in ctrl.parameters) if (p.name == name) return true;
			return false;
		}

		
		
		private AnimationClip SaveMecanimClip(string dir, string segName, AnimationClip legacy)
		{
			var path = dir + "/" + segName + ".anim";
			var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (old != null) AssetDatabase.DeleteAsset(path);
			var clip = new AnimationClip { name = segName, legacy = false };
			clip.frameRate = legacy.frameRate;
			clip.wrapMode = legacy.wrapMode;
			foreach (var b in AnimationUtility.GetCurveBindings(legacy))
			{
				AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(legacy, b));
			}
			foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(legacy))
			{
				AnimationUtility.SetObjectReferenceCurve(clip, b, AnimationUtility.GetObjectReferenceCurve(legacy, b));
			}
			AssetDatabase.CreateAsset(clip, path);
			return AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? clip;
		}

	private GameObject CreateSlotGO(SlotData slot)
	{
		
		
		var go = new GameObject(slot.Name);
		
		
		if (_skewSlots.Contains(slot))
		{
			var spriteGO = new GameObject("Sprite");
			spriteGO.transform.SetParent(go.transform, false);
			spriteGO.AddComponent<SpriteRenderer>();
		}
		else
		{
			go.AddComponent<SpriteRenderer>();
		}
		return go;
	}

	
	
	
	
	
	private Vector3? ComputeVisibleCenter(GameObject root)
		{
			var rds = root.GetComponentsInChildren<SpriteRenderer>();
			var any = false;
			var bounds = new Bounds();
			foreach (var r in rds)
			{
				if (!r.enabled || r.sprite == null || r.color.a <= 0.0001f) continue;
				if (!any)
				{
					bounds = r.bounds;
					any = true;
				}
				else
				{
					bounds.Encapsulate(r.bounds);
				}
			}
			return any ? (Vector3?)bounds.center : null;
		}
	}
}

