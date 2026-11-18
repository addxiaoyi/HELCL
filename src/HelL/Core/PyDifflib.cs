using System.Collections.Generic;

namespace HelL.Core;

public static class PyDifflib
{
	public static int[] CodePoints(string s)
	{
		List<int> list = new List<int>(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
			{
				list.Add(char.ConvertToUtf32(s[i], s[i + 1]));
				i++;
			}
			else
			{
				list.Add(s[i]);
			}
		}
		return list.ToArray();
	}

	public static int PyLen(string s)
	{
		return CodePoints(s).Length;
	}

	public static double Ratio(string a0, string b0)
	{
		int[] array = CodePoints(a0);
		int[] array2 = CodePoints(b0);
		int num = array.Length;
		int num2 = array2.Length;
		if (num + num2 == 0)
		{
			return 1.0;
		}
		Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
		for (int i = 0; i < num2; i++)
		{
			if (!dictionary.TryGetValue(array2[i], out var value))
			{
				value = new List<int>();
				dictionary[array2[i]] = value;
			}
			value.Add(i);
		}
		if (num2 >= 200)
		{
			int num3 = num2 / 100 + 1;
			List<int> list = new List<int>();
			foreach (KeyValuePair<int, List<int>> item in dictionary)
			{
				if (item.Value.Count > num3)
				{
					list.Add(item.Key);
				}
			}
			foreach (int item2 in list)
			{
				dictionary.Remove(item2);
			}
		}
		int num4 = 0;
		List<int[]> list2 = new List<int[]>();
		list2.Add(new int[4] { 0, num, 0, num2 });
		List<int[]> list3 = list2;
		List<int> list4 = new List<int>();
		while (list3.Count > 0)
		{
			int[] array3 = list3[list3.Count - 1];
			list3.RemoveAt(list3.Count - 1);
			int num5 = array3[0];
			int num6 = array3[1];
			int num7 = array3[2];
			int num8 = array3[3];
			int num9 = num5;
			int num10 = num7;
			int j = 0;
			Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
			for (int k = num5; k < num6; k++)
			{
				Dictionary<int, int> dictionary3 = new Dictionary<int, int>();
				if (!dictionary.TryGetValue(array[k], out var value2))
				{
					value2 = list4;
				}
				foreach (int item3 in value2)
				{
					if (item3 >= num7)
					{
						if (item3 >= num8)
						{
							break;
						}
						dictionary2.TryGetValue(item3 - 1, out var value3);
						int num11 = (dictionary3[item3] = value3 + 1);
						int num13 = num11;
						if (num13 > j)
						{
							num9 = k - num13 + 1;
							num10 = item3 - num13 + 1;
							j = num13;
						}
					}
				}
				dictionary2 = dictionary3;
			}
			while (num9 > num5 && num10 > num7 && array[num9 - 1] == array2[num10 - 1])
			{
				num9--;
				num10--;
				j++;
			}
			for (; num9 + j < num6 && num10 + j < num8 && array[num9 + j] == array2[num10 + j]; j++)
			{
			}
			int num14 = num9;
			int num15 = num10;
			int num16 = j;
			if (num16 > 0)
			{
				num4 += num16;
				if (num5 < num14 && num7 < num15)
				{
					list3.Add(new int[4] { num5, num14, num7, num15 });
				}
				if (num14 + num16 < num6 && num15 + num16 < num8)
				{
					list3.Add(new int[4]
					{
						num14 + num16,
						num6,
						num15 + num16,
						num8
					});
				}
			}
		}
		return 2.0 * (double)num4 / (double)(num + num2);
	}
}
