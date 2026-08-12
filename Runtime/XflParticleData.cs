using System;
using System.Collections.Generic;
using UnityEngine;

namespace XflImporter
{
	
	
	
	
	
	[CreateAssetMenu(fileName = "XflParticleData", menuName = "Xfl/Particle Data")]
	public class XflParticleData : ScriptableObject
	{
		public string groupName = "";
		public Sprite sprite;
		public int particleCount = 1;
		public float frameRate = 30f;
		public List<XflParticleSegment> segments = new List<XflParticleSegment>();

		public XflParticleSegment GetSegment(string name)
		{
			for (var i = 0; i < segments.Count; i++)
			{
				if (segments[i].segmentName == name) return segments[i];
			}
			return null;
		}
	}

	[Serializable]
	public class XflParticleSegment
	{
		public string segmentName = "";
		public List<XflParticleFrame> frames = new List<XflParticleFrame>();
	}

	[Serializable]
	public class XflParticleFrame
	{
		public XflParticleState[] states;
	}

	
	[Serializable]
	public struct XflParticleState
	{
		public bool active;
		public Vector2 pos;
		public float rot;
		public float size;
		public Color color;
	}
}
