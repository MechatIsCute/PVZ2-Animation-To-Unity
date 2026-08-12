using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XflImporter
{
	
	
	
	
	
	
	public static class MiniJson
	{
		public static object Parse(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}
			int i = 0;
			return ParseValue(text, ref i);
		}

		private static object ParseValue(string s, ref int i)
		{
			SkipWs(s, ref i);
			if (i >= s.Length)
			{
				return null;
			}
			switch (s[i])
			{
				case '{': return ParseObject(s, ref i);
				case '[': return ParseArray(s, ref i);
				case '"': return ParseString(s, ref i);
				case 't': i += 4; return true;
				case 'f': i += 5; return false;
				case 'n': i += 4; return null;
				default: return ParseNumber(s, ref i);
			}
		}

		private static Dictionary<string, object> ParseObject(string s, ref int i)
		{
			var dict = new Dictionary<string, object>();
			i++; 
			SkipWs(s, ref i);
			if (i < s.Length && s[i] == '}')
			{
				i++;
				return dict;
			}
			while (i < s.Length)
			{
				SkipWs(s, ref i);
				var key = ParseString(s, ref i);
				SkipWs(s, ref i);
				if (i < s.Length && s[i] == ':')
				{
					i++;
				}
				var val = ParseValue(s, ref i);
				if (key != null)
				{
					dict[key] = val;
				}
				SkipWs(s, ref i);
				if (i < s.Length && s[i] == ',')
				{
					i++;
					continue;
				}
				if (i < s.Length && s[i] == '}')
				{
					i++;
					break;
				}
				break;
			}
			return dict;
		}

		private static List<object> ParseArray(string s, ref int i)
		{
			var list = new List<object>();
			i++; 
			SkipWs(s, ref i);
			if (i < s.Length && s[i] == ']')
			{
				i++;
				return list;
			}
			while (i < s.Length)
			{
				var val = ParseValue(s, ref i);
				list.Add(val);
				SkipWs(s, ref i);
				if (i < s.Length && s[i] == ',')
				{
					i++;
					continue;
				}
				if (i < s.Length && s[i] == ']')
				{
					i++;
					break;
				}
				break;
			}
			return list;
		}

		private static string ParseString(string s, ref int i)
		{
			if (i >= s.Length || s[i] != '"')
			{
				return null;
			}
			i++;
			var sb = new StringBuilder();
			while (i < s.Length)
			{
				var c = s[i];
				if (c == '"')
				{
					i++;
					break;
				}
				if (c == '\\' && i + 1 < s.Length)
				{
					var e = s[i + 1];
					switch (e)
					{
						case '"': sb.Append('"'); break;
						case '\\': sb.Append('\\'); break;
						case '/': sb.Append('/'); break;
						case 'b': sb.Append('\b'); break;
						case 'f': sb.Append('\f'); break;
						case 'n': sb.Append('\n'); break;
						case 'r': sb.Append('\r'); break;
						case 't': sb.Append('\t'); break;
						case 'u':
							if (i + 5 < s.Length &&
								int.TryParse(s.Substring(i + 2, 4), NumberStyles.HexNumber,
									CultureInfo.InvariantCulture, out var code))
							{
								sb.Append((char)code);
							}
							i += 4;
							break;
						default: sb.Append(e); break;
					}
					i += 2;
					continue;
				}
				sb.Append(c);
				i++;
			}
			return sb.ToString();
		}

		private static object ParseNumber(string s, ref int i)
		{
			var start = i;
			while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' ||
				s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
			{
				i++;
			}
			var text = s.Substring(start, i - start);
			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
			{
				return d;
			}
			return 0.0;
		}

		private static void SkipWs(string s, ref int i)
		{
			while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
			{
				i++;
			}
		}

		
		public static string Str(object o) => o as string;
		public static double Num(object o)
		{
			if (o is double d) return d;
			if (o is long l) return l;
			if (o is int n) return n;
			return 0.0;
		}
		public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
		public static List<object> Arr(object o) => o as List<object>;
	}
}
