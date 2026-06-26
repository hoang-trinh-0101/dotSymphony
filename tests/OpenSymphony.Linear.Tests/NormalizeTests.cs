using OpenSymphony.Linear;

namespace OpenSymphony.Linear.Tests;

public class NormalizeTests
{
    [Fact]
    public void PriorityZeroBecomesNone()
    {
        Assert.Null(Normalize.NormalizePriority(0.0));
    }

    [Fact]
    public void FractionalPriorityIsRejected()
    {
        Assert.Throws<LinearError.InvalidResponseError>(() => Normalize.NormalizePriority(1.5));
    }

    [Fact]
    public void LinearPriorityIsPreservedForPromptConsumers()
    {
        Assert.Equal((byte)1, Normalize.NormalizePriority(1.0));
        Assert.Equal((byte)4, Normalize.NormalizePriority(4.0));
    }

    [Fact]
    public void UndocumentedLinearPriorityValuesAreRejected()
    {
        Assert.Throws<LinearError.InvalidResponseError>(() => Normalize.NormalizePriority(5.0));
    }
}
