using System.Collections.Generic;
using UnityEngine;

namespace XflImporter
{
	
	
	
	
	
	
	
	
	[RequireComponent(typeof(ParticleSystem))]
	public class XflParticleDriver : MonoBehaviour
	{
		public XflParticleData data;
		public float frameRate = 30f;

		private Animation _anim;
		private ParticleSystem _system;
		private ParticleSystem.Particle[] _pool;
		private Dictionary<string, int> _segIndex;
		private int _lastSegIdx = -1;
		private int _lastFrame = -1;
		private int _lastCount = -1;
		private bool _initialized;

		void Awake()
		{
			Init();
		}

		void OnEnable()
		{
			if (_initialized) return;
			
			Init();
		}

		private void Init()
		{
			if (_initialized) return;
			_initialized = true;
			_system = GetComponent<ParticleSystem>();
			_anim = GetComponentInParent<Animation>();
			if (_system != null)
			{
				var main = _system.main;
				main.playOnAwake = false;
				main.loop = false;
				main.simulationSpace = ParticleSystemSimulationSpace.Local;
				main.startLifetime = 1f;
				main.startSpeed = 0f;
				main.startDelay = 0f;
				main.maxParticles = data != null ? Mathf.Max(data.particleCount, 1) : 1;
				var emission = _system.emission;
				emission.rateOverTime = 0f;
				emission.rateOverDistance = 0f;
			}
			if (data != null)
			{
				_pool = new ParticleSystem.Particle[data.particleCount];
				
				_segIndex = new Dictionary<string, int>();
				for (var i = 0; i < data.segments.Count; i++)
				{
					_segIndex[data.segments[i].segmentName] = i;
				}
				
				
				if (data.sprite != null)
				{
					var psr = GetComponent<ParticleSystemRenderer>();
					if (psr != null)
					{
						psr.renderMode = ParticleSystemRenderMode.Mesh;
						psr.mesh = BuildSpriteMesh(data.sprite);
						psr.sharedMaterial = CreateParticleMaterial(data.sprite);
					}
				}
			}
			
			
			_system.Play();
			_system.Pause();
		}

		
		private static Mesh BuildSpriteMesh(Sprite sprite)
		{
			var mesh = new Mesh();
			var verts = sprite.vertices;
			var v3 = new Vector3[verts.Length];
			for (var i = 0; i < verts.Length; i++)
			{
				v3[i] = new Vector3(verts[i].x, verts[i].y, 0f);
			}
			mesh.vertices = v3;
			mesh.uv = sprite.uv;
			var tris = sprite.triangles;
			var triInt = new int[tris.Length];
			for (var i = 0; i < tris.Length; i++) triInt[i] = tris[i];
			mesh.triangles = triInt;
			var n = v3.Length;
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

		void LateUpdate()
		{
			Sync();
		}

		
		
		
		
		
		
		public void Sync()
		{
			if (!_initialized) Init();
			if (data == null || _system == null || _pool == null) return;

			
			
			
			
			var lsx = transform.lossyScale;
			var sx = Mathf.Max(Mathf.Abs(lsx.x), 1e-6f);
			var sy = Mathf.Max(Mathf.Abs(lsx.y), 1e-6f);
			var scaleComp = Mathf.Sqrt(sx * sy);  

			
			int segIdx = -1, frame = -1;
			if (_anim != null && _anim.isPlaying)
			{
				foreach (AnimationState st in _anim)
				{
					if (!_anim.IsPlaying(st.name)) continue;
					if (!_segIndex.TryGetValue(st.name, out segIdx)) continue;
					frame = Mathf.FloorToInt(st.time * frameRate);
					break;
				}
			}

			
			if (segIdx < 0 || frame < 0 || frame >= data.segments[segIdx].frames.Count)
			{
				if (_lastCount != 0) { _system.SetParticles(_pool, 0); _lastCount = 0; }
				_lastSegIdx = -1;
				_lastFrame = -1;
				return;
			}

			
			if (segIdx == _lastSegIdx && frame == _lastFrame && _lastCount >= 0)
			{
				_system.SetParticles(_pool, _lastCount);
				return;
			}

			
			var states = data.segments[segIdx].frames[frame].states;
			int count = 0;
			for (var i = 0; i < states.Length && i < _pool.Length; i++)
			{
				var ps = states[i];
				if (!ps.active) continue;
				var p = new ParticleSystem.Particle();
				p.position = new Vector3(ps.pos.x * sx, ps.pos.y * sy, 0f);
				p.rotation = ps.rot * Mathf.Rad2Deg;
				p.startSize = ps.size * scaleComp;
				p.startColor = ps.color;
				p.remainingLifetime = 1f;
				p.startLifetime = 1f;
				p.velocity = Vector3.zero;
				_pool[count] = p;
				count++;
			}
			_system.SetParticles(_pool, count);
			_lastSegIdx = segIdx;
			_lastFrame = frame;
			_lastCount = count;
		}
	}
}
