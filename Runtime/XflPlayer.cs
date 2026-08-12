using UnityEngine;

namespace XflImporter
{
	
	
	
	
	[DisallowMultipleComponent]
	public class XflPlayer : MonoBehaviour
	{
		private Animation _anim;

		
		public string[] ClipNames
		{
			get
			{
				EnsureAnim();
				if (_anim == null) return new string[0];
				var names = new string[_anim.GetClipCount()];
				var i = 0;
				foreach (AnimationState st in _anim)
				{
					names[i++] = st.name;
				}
				return names;
			}
		}

		private void Awake()
		{
			EnsureAnim();
		}

		private void Reset()
		{
			EnsureAnim();
		}

		private void EnsureAnim()
		{
			if (_anim == null)
			{
				_anim = GetComponentInChildren<Animation>();
			}
		}

		
		
		
		
		
		
		
		public Transform AnimationRoot
		{
			get
			{
				EnsureAnim();
				return _anim != null ? _anim.transform : null;
			}
		}

		
		
		
		
		public void OffsetOrigin(Vector3 offset)
		{
			var root = AnimationRoot;
			if (root != null) root.localPosition += offset;
		}

		
		
		
		
		
		
		public void SetOriginPivot(Vector2 pivot, Vector3 worldPoint)
		{
			EnsureAnim();
			if (_anim == null) return;
			var b = VisualBounds();
			var pivotWorld = new Vector3(
				Mathf.Lerp(b.min.x, b.max.x, pivot.x),
				Mathf.Lerp(b.min.y, b.max.y, pivot.y),
				Mathf.Lerp(b.min.z, b.max.z, 0.5f));
			_anim.transform.position += worldPoint - pivotWorld;
		}

		
		public Bounds VisualBounds()
		{
			EnsureAnim();
			var rds = GetComponentsInChildren<SpriteRenderer>(true);
			var any = false;
			var bounds = new Bounds();
			foreach (var r in rds)
			{
				if (!r.enabled || r.sprite == null || r.color.a <= 0.0001f) continue;
				if (!any) { bounds = r.bounds; any = true; }
				else bounds.Encapsulate(r.bounds);
			}
			return any ? bounds : new Bounds(transform.position, Vector3.zero);
		}

		
		public void Play(string clip)
		{
			EnsureAnim();
			if (_anim != null) _anim.Play(clip);
		}

		
		public void PlayLoop(string clip)
		{
			EnsureAnim();
			if (_anim == null) return;
			var st = _anim[clip];
			if (st != null) st.wrapMode = WrapMode.Loop;
			_anim.Play(clip);
		}

		public void Stop()
		{
			EnsureAnim();
			if (_anim != null) _anim.Stop();
		}

		public bool IsPlaying(string clip)
		{
			EnsureAnim();
			return _anim != null && _anim.IsPlaying(clip);
		}

		public bool HasClip(string clip)
		{
			EnsureAnim();
			return _anim != null && _anim.GetClip(clip) != null;
		}

		public string CurrentClip
		{
			get
			{
				EnsureAnim();
				return _anim != null && _anim.clip != null ? _anim.clip.name : null;
			}
		}

		public float CurrentTime
		{
			get
			{
				EnsureAnim();
				
				if (_anim == null || _anim.clip == null) return 0f;
				var st = _anim[_anim.clip.name];
				return st != null ? st.time : 0f;
			}
		}
	}
}
