using System;

namespace XflImporter
{
	
	public struct XflColor
	{
		public double R, G, B, A;
		public static XflColor White => new XflColor { R = 1, G = 1, B = 1, A = 1 };
		public static XflColor operator *(XflColor a, XflColor b) =>
			new XflColor { R = a.R * b.R, G = a.G * b.G, B = a.B * b.B, A = a.A * b.A };
	}

	
	
	
	
	
	
	public struct XflMatrix
	{
		public double A, B, C, D, Tx, Ty;
		public static XflMatrix Identity => new XflMatrix { A = 1, D = 1 };

		public static XflMatrix operator *(XflMatrix a, XflMatrix b)
		{
			return new XflMatrix
			{
				A = a.A * b.A + a.C * b.B,
				B = a.B * b.A + a.D * b.B,
				C = a.A * b.C + a.C * b.D,
				D = a.B * b.C + a.D * b.D,
				Tx = a.A * b.Tx + a.C * b.Ty + a.Tx,
				Ty = a.B * b.Tx + a.D * b.Ty + a.Ty,
			};
		}

		
		public void Apply(double x, double y, out double ox, out double oy)
		{
			ox = A * x + C * y + Tx;
			oy = B * x + D * y + Ty;
		}

		
		
		
		
		
		
		
		
		
		
		
		public static void Decompose(XflMatrix m, out double px, out double py, out double rot,
			out double sx, out double sy, out double skewErr)
		{
			var a = m.A; var b = m.B; var c = m.C; var d = m.D;
			sx = Math.Sqrt(a * a + b * b);
			var det = a * d - b * c;
			sy = sx > 1e-9 ? det / sx : 0.0;
			rot = Math.Atan2(-b, a);
			px = m.Tx;
			py = -m.Ty;
			var ca = Math.Cos(rot);
			var sa = Math.Sin(rot);
			var errA = Math.Abs(sx * ca - a);
			var errB = Math.Abs(sx * sa + b);
			var errC = Math.Abs(-sy * sa + c);
			var errD = Math.Abs(sy * ca - d);
			skewErr = Math.Max(Math.Max(errA, errB), Math.Max(errC, errD));
		}

		
		
		
		
		
		
		
		
		
		
		public static void DecomposeNested(XflMatrix m, out double px, out double py, out double rot,
			out double sx, out double sy, out double childRot,
			out double childScaleX, out double childScaleY, out double skewErr)
		{
			var l11 = m.A; var l12 = -m.C; var l21 = -m.B; var l22 = m.D;
			var sumAng = Math.Atan2(l21 - l12, l11 + l22);
			var diffAng = Math.Atan2(l21 + l12, l11 - l22);
			rot = (sumAng + diffAng) * 0.5;
			childRot = (sumAng - diffAng) * 0.5;
			var p = Math.Sqrt((l21 - l12) * (l21 - l12) + (l11 + l22) * (l11 + l22));
			var q = Math.Sqrt((l21 + l12) * (l21 + l12) + (l11 - l22) * (l11 - l22));
			sx = (p + q) * 0.5;
			sy = (p - q) * 0.5;
			childScaleX = 1.0;
			childScaleY = 1.0;
			px = m.Tx;
			py = -m.Ty;
			
			var ca = Math.Cos(rot); var sa = Math.Sin(rot);
			var cr = Math.Cos(childRot); var sr = Math.Sin(childRot);
			var ra = sx * ca * cr - sy * sa * sr;
			var rb = -sx * ca * sr - sy * sa * cr;
			var rc = sx * sa * cr + sy * ca * sr;
			var rd = -sx * sa * sr + sy * ca * cr;
			skewErr = Math.Max(Math.Max(Math.Abs(ra - l11), Math.Abs(rb - l12)),
				Math.Max(Math.Abs(rc - l21), Math.Abs(rd - l22)));
		}

		
		public static double Rotation(XflMatrix m) => Math.Atan2(-m.B, m.A);
	}
}
