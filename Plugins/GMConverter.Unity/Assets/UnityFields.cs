using AssetsTools.NET;

namespace GMConverter.Unity.Assets;

/// <summary>Helpers for reading common Unity field shapes.</summary>
internal static class UnityFields
{
    /// <summary>The bytes of a <c>vector&lt;UInt8&gt;</c> field or of typeless data.</summary>
    public static byte[] ReadBytes(AssetTypeValueField field)
    {
        var array = field["Array"];
        if (array.IsDummy)
        {
            return field.AsByteArray;
        }

        if (array.Value?.ValueType == AssetValueType.ByteArray)
        {
            return array.AsByteArray;
        }

        return [.. array.Children.Select(child => child.AsByte)];
    }

    /// <summary>The children of a vector or array field.</summary>
    public static IReadOnlyList<AssetTypeValueField> Items(AssetTypeValueField field)
    {
        var array = field["Array"];
        return array.IsDummy ? [] : array.Children;
    }

    public static bool IsNullPointer(AssetTypeValueField pointer)
    {
        return pointer.IsDummy || pointer["m_PathID"].AsLong == 0;
    }
}
