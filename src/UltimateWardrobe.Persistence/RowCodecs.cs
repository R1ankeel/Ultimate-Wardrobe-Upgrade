using System.Globalization;

namespace UltimateWardrobe.Persistence;

/// <summary>
/// Conversions between Core-domain values and SQLite row values (Phase 4 Sprint 4.2). Shared by the
/// repositories so timestamps, enums and Guids are stored/read uniformly: ISO-8601 round-trip
/// strings, enum-name strings, and <c>GUID</c> text. JSON columns are handled separately by
/// <see cref="PersistenceJson"/>.
/// </summary>
internal static class RowCodecs
{
    public static string Utc(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTime DateTime(string value) => System.DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static string? UtcOrNull(DateTime? value) => value.HasValue ? Utc(value.Value) : null;

    public static DateTime? NullableDateTime(object? value)
        => value is null or DBNull ? null : DateTime(value.ToString()!);

    public static string EnumName<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString();

    public static TEnum ParseEnum<TEnum>(object? value) where TEnum : struct, Enum
        => (TEnum)Enum.Parse(typeof(TEnum), value?.ToString() ?? string.Empty);

    public static Guid Guid(object value) => value is Guid g ? g : System.Guid.Parse(value.ToString()!);

    public static Guid? NullableGuid(object? value)
        => value is null or DBNull ? null : Guid(value);

    public static string? NullableGuidToString(Guid? value) => value.HasValue ? value.Value.ToString() : null;

    public static string Text(object? value) => value?.ToString() ?? string.Empty;

    public static string? NullableText(object? value)
        => value is null or DBNull ? null : value.ToString();
}
