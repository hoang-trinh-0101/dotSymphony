using System.Text.Json.Serialization;

namespace OpenSymphony.Domain;

// ht: Rust transparent newtype over u64. Bare JSON integer via converter.
[JsonConverter(typeof(DurationMsConverter))]
public readonly struct DurationMs : IEquatable<DurationMs>, IComparable<DurationMs>
{
    public ulong Value { get; }

    private DurationMs(ulong value) => Value = value;

    public static DurationMs New(ulong value) => new(value);
    public ulong AsU64() => Value;

    public bool Equals(DurationMs other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is DurationMs other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(DurationMs other) => Value.CompareTo(other.Value);

    public static bool operator ==(DurationMs left, DurationMs right) => left.Equals(right);
    public static bool operator !=(DurationMs left, DurationMs right) => !left.Equals(right);
    public static bool operator <(DurationMs left, DurationMs right) => left.Value < right.Value;
    public static bool operator <=(DurationMs left, DurationMs right) => left.Value <= right.Value;
    public static bool operator >(DurationMs left, DurationMs right) => left.Value > right.Value;
    public static bool operator >=(DurationMs left, DurationMs right) => left.Value >= right.Value;

    // ht: Rust u64::saturating_mul — clamp at ulong.MaxValue on overflow.
    public DurationMs SaturatingMul(ulong multiplier)
    {
        try { return DurationMs.New(checked(Value * multiplier)); }
        catch (OverflowException) { return DurationMs.New(ulong.MaxValue); }
    }

    public override string ToString() => Value.ToString();
}

[JsonConverter(typeof(TimestampMsConverter))]
public readonly struct TimestampMs : IEquatable<TimestampMs>, IComparable<TimestampMs>
{
    public ulong Value { get; }

    private TimestampMs(ulong value) => Value = value;

    public static TimestampMs New(ulong value) => new(value);
    public ulong AsU64() => Value;

    // ht: Rust u64::saturating_add — clamp at ulong.MaxValue on overflow.
    public TimestampMs SaturatingAdd(DurationMs duration)
    {
        try { return new TimestampMs(checked(Value + duration.AsU64())); }
        catch (OverflowException) { return new TimestampMs(ulong.MaxValue); }
    }

    // ht: Rust u64::saturating_sub — clamp at 0 on underflow.
    public TimestampMs SaturatingSub(DurationMs duration)
    {
        var dur = duration.AsU64();
        return dur > Value ? new TimestampMs(0) : new TimestampMs(Value - dur);
    }

    public bool Equals(TimestampMs other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is TimestampMs other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(TimestampMs other) => Value.CompareTo(other.Value);

    public static bool operator ==(TimestampMs left, TimestampMs right) => left.Equals(right);
    public static bool operator !=(TimestampMs left, TimestampMs right) => !left.Equals(right);
    public static bool operator <(TimestampMs left, TimestampMs right) => left.Value < right.Value;
    public static bool operator <=(TimestampMs left, TimestampMs right) => left.Value <= right.Value;
    public static bool operator >(TimestampMs left, TimestampMs right) => left.Value > right.Value;
    public static bool operator >=(TimestampMs left, TimestampMs right) => left.Value >= right.Value;

    public override string ToString() => Value.ToString();
}
