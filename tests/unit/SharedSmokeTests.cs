using PartsPortal.Shared.Models;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Smoke tests verifying the shared library is wired up and reachable.
/// These guard the cross-cutting contracts (correlation propagation) that
/// every function and mock depends on. Richer behavioural tests land per task.
/// </summary>
public class SharedSmokeTests
{
    [Fact]
    public void CorrelationContext_HeaderName_IsCanonicalHeader()
    {
        // The correlation header travels cart -> reserve -> order -> fulfilment;
        // its name must stay stable across every hop.
        Assert.Equal("x-correlation-id", CorrelationContext.HeaderName);
    }

    [Fact]
    public void CorrelationContext_PreservesCorrelationId()
    {
        var id = "11111111-2222-3333-4444-555555555555";

        var context = new CorrelationContext(id);

        Assert.Equal(id, context.CorrelationId);
    }

    [Fact]
    public void CorrelationContext_RecordEquality_HoldsForSameId()
    {
        var a = new CorrelationContext("abc-123");
        var b = new CorrelationContext("abc-123");

        Assert.Equal(a, b);
    }
}
