using OpenSymphony.Domain;

namespace OpenSymphony.Domain.Tests;

public class HarnessTests
{
    // ht: dummy adapter proves the interface compiles and is usable. No JSON parity.
    private sealed class DummyAdapter : IHarnessAdapter
    {
        public string HarnessKind => "dummy";
    }

    [Fact]
    public void IHarnessAdapter_HarnessKind_ReturnsKindString()
    {
        IHarnessAdapter adapter = new DummyAdapter();
        Assert.Equal("dummy", adapter.HarnessKind);
    }
}
