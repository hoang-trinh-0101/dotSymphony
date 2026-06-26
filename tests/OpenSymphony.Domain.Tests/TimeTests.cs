using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Domain.Tests;

public class TimeTests
{
    [Fact]
    public void DurationMs_New_AsU64_RoundTrip()
    {
        var d = DurationMs.New(300000);
        Assert.Equal(300000UL, d.AsU64());
        Assert.Equal(300000UL, d.Value);
    }

    [Fact]
    public void TimestampMs_New_AsU64_RoundTrip()
    {
        var t = TimestampMs.New(42);
        Assert.Equal(42UL, t.AsU64());
        Assert.Equal(42UL, t.Value);
    }

    [Fact]
    public void TimestampMs_SaturatingAdd_NormalCase()
    {
        var t = TimestampMs.New(100);
        Assert.Equal(TimestampMs.New(150), t.SaturatingAdd(DurationMs.New(50)));
    }

    [Fact]
    public void TimestampMs_SaturatingAdd_SaturatesAtUlongMax()
    {
        var t = TimestampMs.New(ulong.MaxValue);
        Assert.Equal(ulong.MaxValue, t.SaturatingAdd(DurationMs.New(1)).AsU64());
    }

    [Fact]
    public void DurationMs_SerializesAsBareInteger()
    {
        var json = JsonSerializer.Serialize(DurationMs.New(300000), DomainJsonOptions.Default);
        Assert.Equal("300000", json);
    }

    [Fact]
    public void TimestampMs_SerializesAsBareInteger()
    {
        var json = JsonSerializer.Serialize(TimestampMs.New(100), DomainJsonOptions.Default);
        Assert.Equal("100", json);
    }

    [Fact]
    public void TimestampMs_DeserializesFromBareInteger()
    {
        var t = JsonSerializer.Deserialize<TimestampMs>("100", DomainJsonOptions.Default);
        Assert.Equal(TimestampMs.New(100), t);
    }

    [Fact]
    public void DurationMs_DeserializesFromBareInteger()
    {
        var d = JsonSerializer.Deserialize<DurationMs>("300000", DomainJsonOptions.Default);
        Assert.Equal(DurationMs.New(300000), d);
    }
}
