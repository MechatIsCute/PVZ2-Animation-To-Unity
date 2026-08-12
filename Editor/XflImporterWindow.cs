using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace XflImporter
{
	
	
	
	
	public class XflImporterWindow : EditorWindow
	{
		private const string PrefSrc = "XflImporter.srcFolder";
		private const string PrefOut = "XflImporter.outputFolder";
		
		private const string PrefBrowseSrc = "XflImporter.browseSrc";
		private const string PrefBrowseOut = "XflImporter.browseOut";

		private string _srcFolder = "";
		private string _outputFolder = "Assets/XflCharacters";

		
		private bool _mergeSlots = true;
		private int _mergeMode = 1;
		private bool _normalizeNames = true;
		private int _mergeBuffer = 0;
		private bool _groupSlots = true;
		private bool _visibleToAlpha = false;
		private bool _particleMerge = false;
		private bool _buildStateMachine = false;
		private bool _loopClips = false;
		private bool _pixelArt = false;
		private float _spritePPU = 50f;

		private string _log = "";
		private Vector2 _logScroll;

		
		private bool _converting;
		private string _progressTitle = "";
		private float _progress;

		
		private static XflImporterWindow Current
		{
			get
			{
				var all = Resources.FindObjectsOfTypeAll<XflImporterWindow>();
				return all.Length > 0 ? all[0] : null;
			}
		}

		
		public static void ReportProgress(string title, float frac)
		{
			var w = Current;
			if (w == null) return;
			w._converting = true;
			w._progressTitle = title;
			w._progress = frac;
			w.Repaint();
		}

		
		public static void EndProgress()
		{
			var w = Current;
			if (w == null) return;
			w._converting = false;
			w.Repaint();
		}

		
		private static GUIStyle _headerStyle, _subHeaderStyle, _sectionStyle, _logTextStyle, _logBoxStyle;

		private static GUIStyle HeaderStyle => _headerStyle ??= MakeStyle(EditorStyles.largeLabel, 18, FontStyle.Bold, TextAnchor.MiddleCenter, null);
		private static GUIStyle SubHeaderStyle => _subHeaderStyle ??= MakeStyle(EditorStyles.miniLabel, 0, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.55f, 0.55f, 0.55f));
		private static GUIStyle SectionStyle => _sectionStyle ??= MakeStyle(EditorStyles.boldLabel, 12, FontStyle.Bold, TextAnchor.MiddleLeft, null);

		
		private static GUIStyle LogTextStyle
		{
			get
			{
				if (_logTextStyle == null)
				{
					_logTextStyle = new GUIStyle(EditorStyles.label)
					{
						richText = true,
						wordWrap = true,
					};
					_logTextStyle.font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Menlo" }, 12);
					_logTextStyle.normal.textColor = new Color(0.78f, 0.82f, 0.85f);
					_logTextStyle.padding = new RectOffset(8, 8, 6, 6);
				}
				return _logTextStyle;
			}
		}

		
		private static GUIStyle LogBoxStyle
		{
			get
			{
				if (_logBoxStyle == null)
				{
					_logBoxStyle = new GUIStyle();
					_logBoxStyle.normal.background = MakeTex(new Color(0.07f, 0.08f, 0.09f, 1f));
					_logBoxStyle.padding = new RectOffset(2, 2, 2, 2);
					_logBoxStyle.border = new RectOffset(2, 2, 2, 2);
				}
				return _logBoxStyle;
			}
		}

		private static GUIStyle MakeStyle(GUIStyle baseStyle, int fontSize, FontStyle fontStyle, TextAnchor align, Color? color)
		{
			var s = new GUIStyle(baseStyle);
			if (fontSize > 0) s.fontSize = fontSize;
			s.fontStyle = fontStyle;
			s.alignment = align;
			if (color.HasValue) s.normal.textColor = color.Value;
			return s;
		}

		private static Texture2D MakeTex(Color c)
		{
			var t = new Texture2D(1, 1);
			t.SetPixel(0, 0, c);
			t.Apply();
			return t;
		}

		[MenuItem("Tools/PVZ XFL → Unity")]
		public static void OpenWindow()
		{
			var w = GetWindow<XflImporterWindow>("PVZ XFL → Unity");
			w.minSize = new Vector2(520, 680);
			w.Show();
		}

		
		private void OnEnable()
		{
			_srcFolder = EditorPrefs.GetString(PrefSrc, "");
			_outputFolder = EditorPrefs.GetString(PrefOut, "Assets/XflCharacters");
		}

		private void OnDisable()
		{
			SavePrefs();
		}

		private void SavePrefs()
		{
			EditorPrefs.SetString(PrefSrc, _srcFolder);
			EditorPrefs.SetString(PrefOut, _outputFolder);
		}

		private void OnGUI()
		{
			EditorGUILayout.Space(6);
			GUILayout.Label("PVZ2XFL→Unity", HeaderStyle);
			GUILayout.Label("作者@Mechat 3.2.5LTS", SubHeaderStyle);
			EditorGUILayout.Space(6);

			DrawSourceSection();
			EditorGUILayout.Space(4);
			DrawMergeSection();
			EditorGUILayout.Space(4);
			DrawNamingSection();
			EditorGUILayout.Space(4);
			DrawRenderSection();
			EditorGUILayout.Space(8);

			
			var btnText = _converting
				? "转换中… " + Mathf.RoundToInt(_progress * 100f) + "%"
				: "开 始 转 换";
			using (new EditorGUI.DisabledScope(_converting))
			{
				if (GUILayout.Button(btnText, GUILayout.Height(40)))
				{
					var src = _srcFolder.Replace('\\', '/').TrimEnd('/');
					var outp = _outputFolder.Replace('\\', '/').TrimEnd('/');
					_log = "$ xfl2unity --source \"" + src + "\" --output \"" + outp + "\"";
					_log += "\n" + RunConvert();
					SavePrefs();
				}
			}
			EditorGUILayout.Space(8);

			DrawLogSection();
		}

		private void DrawSourceSection()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			GUILayout.Label("Source & Output", SectionStyle);
			using (new EditorGUILayout.HorizontalScope())
			{
				_srcFolder = EditorGUILayout.TextField(
					new GUIContent("源文件夹", "XFL 源文件夹绝对路径，需包含 DOMDocument.xml、extra.json 与 LIBRARY 目录。"), _srcFolder);
				if (GUILayout.Button("浏览…", GUILayout.Width(64)))
				{
					var start = EditorPrefs.GetString(PrefBrowseSrc, "");
					if (start == "") start = ToAbs(_srcFolder);
					var sel = EditorUtility.OpenFolderPanel("选择 XFL 源文件夹", start, "");
					if (!string.IsNullOrEmpty(sel))
					{
						_srcFolder = ToAssetRelative(sel);
						EditorPrefs.SetString(PrefBrowseSrc, sel);
					}
				}
			}
			using (new EditorGUILayout.HorizontalScope())
			{
				_outputFolder = EditorGUILayout.TextField(
					new GUIContent("输出文件夹", "Assets 相对路径，生成的 Prefab、AnimationClip 与粒子数据资产将写入该目录。"), _outputFolder);
				if (GUILayout.Button("浏览…", GUILayout.Width(64)))
				{
					var start = EditorPrefs.GetString(PrefBrowseOut, "");
					if (start == "") start = ToAbs(_outputFolder);
					var sel = EditorUtility.OpenFolderPanel("选择输出文件夹", start, "");
					if (!string.IsNullOrEmpty(sel))
					{
						_outputFolder = ToAssetRelative(sel);
						EditorPrefs.SetString(PrefBrowseOut, sel);
					}
				}
			}
			EditorGUILayout.EndVertical();
		}

		private void DrawMergeSection()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			GUILayout.Label("Merge", SectionStyle);
			DrawToggle("合并互斥精灵", "将逐帧互斥出现的精灵槽位合并为单一节点，按纹理池化以减少节点数量与绘制调用。", ref _mergeSlots);
			using (new EditorGUI.DisabledScope(!_mergeSlots))
			{
				_mergeMode = EditorGUILayout.Popup(
					new GUIContent("合并模式", "按图层：同段内 z 坐标必须一致，保守；按可见性：仅要求逐帧互斥，节点更少。"),
					_mergeMode, new[] { "按图层", "按可见性" });
				_mergeBuffer = EditorGUILayout.IntSlider(
					new GUIContent("合并缓冲", "跨成员切换处至少间隔的不可见帧数，避免紧邻切换产生的跳变。"), _mergeBuffer, 0, 10);
			}
			EditorGUILayout.EndVertical();
		}

		private void DrawNamingSection()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			GUILayout.Label("Naming & Grouping", SectionStyle);
			DrawToggle("规范命名", "对同名节点按层级顺序追加连续数字后缀，消除命名冲突。", ref _normalizeNames);
			DrawToggle("自动分组", "将同名或相似名称的槽位归入同一组节点，组基名通过剥除序号与中间编号归一化推导。", ref _groupSlots);
			EditorGUILayout.EndVertical();
		}

		private void DrawRenderSection()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			GUILayout.Label("Animation & Rendering", SectionStyle);
			DrawToggle("粒子合并", "将同帧共存的粒子类槽位合并为单一 ParticleSystem 节点与单一绘制调用，由运行时驱动按帧读取烘焙数据。", ref _particleMerge);
			DrawToggle("状态机资源", "生成 AnimatorController 状态机：根据段名猜测分组（start/loop/end、on/off/空白）并自动生成占位参数与过渡条件，引用非 Legacy 的 Mecanim clip 副本（原 Legacy clip 不受影响）。", ref _buildStateMachine);
			DrawToggle("可见性转Alpha", "将可见性编码进 SpriteRenderer 颜色 alpha，不可见时为 0；不生成 enabled 轨道。", ref _visibleToAlpha);
			DrawToggle("循环片段", "对非终结片段（die、particles、death 除外）设置 Loop 循环模式。", ref _loopClips);
			DrawToggle("像素采样", "纹理使用点采样过滤，保留像素边缘的锐利观感。", ref _pixelArt);
			_spritePPU = EditorGUILayout.Slider(
				new GUIContent("精灵PPU", "像素到世界单位的换算比例（Pixels Per Unit），数值越小角色在世界空间中越大。"), _spritePPU, 10f, 200f);
			EditorGUILayout.EndVertical();
		}

		private void DrawLogSection()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			GUILayout.Label("Output", SectionStyle);
			_logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
			
			using (new EditorGUILayout.VerticalScope(LogBoxStyle, GUILayout.ExpandHeight(true)))
			{
				if (_converting)
				{
					GUILayout.Label(ProgressLine(), LogTextStyle);
					GUILayout.Space(2);
				}
				if (string.IsNullOrEmpty(_log))
				{
					if (!_converting)
					{
						GUILayout.Label("<color=#6A737D>$ xfl2unity --source &lt;folder&gt;</color>", LogTextStyle);
					}
				}
				else
				{
					foreach (var raw in _log.Split('\n'))
					{
						var line = raw.TrimEnd('\r');
						if (string.IsNullOrEmpty(line.Trim()))
						{
							GUILayout.Space(3);
							continue;
						}
						GUILayout.Label(ColoredLine(line), LogTextStyle);
					}
				}
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		
		private static void DrawToggle(string label, string tooltip, ref bool value)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.ExpandWidth(true));
			value = EditorGUILayout.Toggle(value, GUILayout.Width(28));
			EditorGUILayout.EndHorizontal();
		}

		
		private static string ColoredLine(string line)
		{
			var esc = line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
			string color = "#C8CFD4";
			if (esc.StartsWith("成功：", StringComparison.Ordinal)) color = "#6FDF7A";
			else if (esc.StartsWith("失败：", StringComparison.Ordinal) || esc.StartsWith("错误：", StringComparison.Ordinal)) color = "#FF7A6E";
			else if (esc.StartsWith("警告：", StringComparison.Ordinal)) color = "#FFD479";
			return "<color=" + color + ">" + esc + "</color>";
		}

		
		private string ProgressLine()
		{
			var pct = Mathf.Clamp01(_progress);
			const int barLen = 22;
			var filled = Mathf.RoundToInt(pct * barLen);
			var bar = new string('█', filled) + new string('░', barLen - filled);
			var title = _progressTitle.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
			return "<color=#7FD0FF>$ " + title + "  [" + bar + "] " + Mathf.RoundToInt(pct * 100f) + "%</color>";
		}

		private string RunConvert()
		{
			var src = _srcFolder.Replace('\\', '/').TrimEnd('/');
			var outp = _outputFolder.Replace('\\', '/').TrimEnd('/');
			if (string.IsNullOrEmpty(src)) return "错误：请选择源 XFL 文件夹";
			if (string.IsNullOrEmpty(outp)) return "错误：输出文件夹为空";
			if (!outp.StartsWith("Assets/", StringComparison.Ordinal))
			{
				return "错误：输出文件夹必须是 Assets 下的相对路径，例如 Assets/XflCharacters";
			}
			if (!File.Exists(Path.Combine(src, "DOMDocument.xml")))
			{
				return "错误：源文件夹中未找到 DOMDocument.xml（不是有效的 XFL 文件夹）";
			}

			var conv = new XflConverter();
			var opts = new XflConvertOptions
			{
				MergeSlots = _mergeSlots,
				MergeMode = _mergeMode,
				NormalizeNames = _normalizeNames,
				MergeBuffer = Mathf.Max(0, _mergeBuffer),
				GroupSlots = _groupSlots,
				VisibleToAlpha = _visibleToAlpha,
				ParticleMerge = _particleMerge,
				BuildStateMachine = _buildStateMachine,
				LoopClips = _loopClips,
				PixelArt = _pixelArt,
				SpritePPU = _spritePPU,
			};
			var ok = conv.Convert(src, outp, opts, out var msg);
			return (ok ? "成功：\n" : "失败：\n") + msg;
		}

		
		private static string ToAbs(string path)
		{
			if (string.IsNullOrEmpty(path)) return "";
			path = path.Replace('\\', '/').TrimEnd('/');
			if (path.Length >= 2 && path[1] == ':') return path; 
			return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
		}

		
		private static string ToAssetRelative(string abs)
		{
			var full = Path.GetFullPath(abs).Replace('\\', '/').TrimEnd('/');
			var project = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
			if (full.StartsWith(project + "/", StringComparison.Ordinal))
			{
				return full.Substring(project.Length + 1);
			}
			return full;
		}
	}
}
