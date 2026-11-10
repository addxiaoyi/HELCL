using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

internal sealed class UpdateVersion : IComparable<UpdateVersion>
{
	private const int MaxVersionLength = 128;
	private static readonly Regex Pattern = new Regex(
		@"\A[vV]?(?<core>[0-9]+(?:\.[0-9]+){0,3})(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
		RegexOptions.CultureInvariant);

	private readonly int[] _parts;
	private readonly string[] _prerelease;

	public IReadOnlyList<int> Parts => _parts;

	private UpdateVersion(int[] parts, string[] prerelease)
	{
		_parts = parts;
		_prerelease = prerelease;
	}

	public static bool TryParse(object value, out UpdateVersion version)
	{
		version = null;
		if (!(value is string text) || text.Length > MaxVersionLength)
			return false;

		Match match = Pattern.Match(text.Trim());
		if (!match.Success)
			return false;

		string[] components = match.Groups["core"].Value.Split('.');
		int[] parts = new int[4];
		for (int index = 0; index < components.Length; index++)
		{
			if (!int.TryParse(components[index], NumberStyles.None, CultureInfo.InvariantCulture, out parts[index]))
				return false;
		}

		string[] prerelease = match.Groups["pre"].Success
			? match.Groups["pre"].Value.Split('.')
			: Array.Empty<string>();
		version = new UpdateVersion(parts, prerelease);
		return true;
	}

	public int CompareTo(UpdateVersion other)
	{
		if (other == null)
			return 1;

		for (int index = 0; index < _parts.Length; index++)
		{
			int order = _parts[index].CompareTo(other._parts[index]);
			if (order != 0)
				return order;
		}

		if (_prerelease.Length == 0)
			return other._prerelease.Length == 0 ? 0 : 1;
		if (other._prerelease.Length == 0)
			return -1;

		for (int index = 0; index < Math.Min(_prerelease.Length, other._prerelease.Length); index++)
		{
			int order = CompareIdentifier(_prerelease[index], other._prerelease[index]);
			if (order != 0)
				return order;
		}
		return _prerelease.Length.CompareTo(other._prerelease.Length);
	}

	private static int CompareIdentifier(string left, string right)
	{
		bool leftNumeric = left.All(char.IsDigit);
		bool rightNumeric = right.All(char.IsDigit);
		if (leftNumeric != rightNumeric)
			return leftNumeric ? -1 : 1;
		if (!leftNumeric)
			return string.CompareOrdinal(left, right);

		// Compare numeric identifiers without overflowing on a long build number.
		left = left.TrimStart('0');
		right = right.TrimStart('0');
		int lengthOrder = left.Length.CompareTo(right.Length);
		return lengthOrder != 0 ? lengthOrder : string.CompareOrdinal(left, right);
	}
}
