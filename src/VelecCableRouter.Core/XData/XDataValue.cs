using System;
using System.Globalization;

namespace VelecCableRouter.Core.XData
{
    /// <summary>DXF group codes used by BobrovXDATA v2.</summary>
    public static class DxfCode
    {
        public const short String = 1000;
        public const short RegAppName = 1001;
        public const short ControlString = 1002;
        public const short Real = 1040;
        public const short Int16 = 1070;
        public const short Int32 = 1071;
    }

    /// <summary>One typed XDATA item (DXF code + value), mirrors AutoCAD <c>TypedValue</c> without referencing it.</summary>
    public readonly struct XDataValue : IEquatable<XDataValue>
    {
        public XDataValue(short typeCode, object? value)
        {
            TypeCode = typeCode;
            Value = value;
        }

        public short TypeCode { get; }

        public object? Value { get; }

        public static XDataValue Str(string value) => new XDataValue(DxfCode.String, value);

        public static XDataValue Real(double value) => new XDataValue(DxfCode.Real, value);

        public static XDataValue Int16(short value) => new XDataValue(DxfCode.Int16, value);

        public static XDataValue Int32(int value) => new XDataValue(DxfCode.Int32, value);

        public static XDataValue App(string name) => new XDataValue(DxfCode.RegAppName, name);

        public static XDataValue Control(string brace) => new XDataValue(DxfCode.ControlString, brace);

        /// <summary>The value as text (numbers in invariant culture), or <c>null</c>.</summary>
        public string? AsString()
        {
            switch (Value)
            {
                case null:
                    return null;
                case string s:
                    return s;
                case double d:
                    return d.ToString("R", CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case IConvertible c:
                    return c.ToString(CultureInfo.InvariantCulture);
                default:
                    return Value.ToString();
            }
        }

        /// <summary>The value as a number; strings are parsed with '.' or ',' as decimal separator.</summary>
        public double? AsDouble()
        {
            switch (Value)
            {
                case null:
                    return null;
                case string s:
                    var text = s.Trim().Replace(',', '.');
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null;
                case bool b:
                    return b ? 1 : 0;
                case IConvertible c:
                    try
                    {
                        return c.ToDouble(CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                    catch (InvalidCastException)
                    {
                        return null;
                    }
                default:
                    return null;
            }
        }

        public bool Equals(XDataValue other) => TypeCode == other.TypeCode && Equals(Value, other.Value);

        public override bool Equals(object? obj) => obj is XDataValue other && Equals(other);

        public override int GetHashCode() => unchecked((TypeCode * 397) ^ (Value?.GetHashCode() ?? 0));

        public override string ToString() => "(" + TypeCode.ToString(CultureInfo.InvariantCulture) + " . " + (Value is string ? "\"" + Value + "\"" : AsString()) + ")";
    }
}
