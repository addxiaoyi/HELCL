using System;

namespace HelL.Core;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DefAttribute : Attribute
{
	public object Value { get; }

	public DefAttribute(object value)
	{
		Value = ((value is int num) ? ((object)(long)num) : value);
	}
}
