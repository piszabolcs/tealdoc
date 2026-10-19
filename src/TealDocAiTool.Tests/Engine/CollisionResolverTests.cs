using System.Collections.Concurrent;
using TealDocAiTool.Engine;

namespace TealDocAiTool.Tests.Engine;

public class CollisionResolverTests
{
    [Fact]
    public void ResolveUniqueName_ReturnsPlainNameWhenNotTaken()
    {
        var taken = new ConcurrentDictionary<string, byte>();
        string result = CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        Assert.Equal("Cím.pdf", result);
    }

    [Fact]
    public void ResolveUniqueName_AppendsSuffixOnCollision()
    {
        var taken = new ConcurrentDictionary<string, byte>();
        CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        string second = CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        Assert.Equal("Cím (2).pdf", second);
    }

    [Fact]
    public void ResolveUniqueName_IncrementsThroughMultipleCollisions()
    {
        var taken = new ConcurrentDictionary<string, byte>();
        CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        string third = CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        Assert.Equal("Cím (3).pdf", third);
    }

    [Fact]
    public void ResolveUniqueName_IsCaseInsensitive()
    {
        var taken = new ConcurrentDictionary<string, byte>();
        CollisionResolver.ResolveUniqueName("Cím", ".pdf", taken);
        string result = CollisionResolver.ResolveUniqueName("cím", ".pdf", taken);
        Assert.Equal("cím (2).pdf", result);
    }
}
