using PartsPortal.Shared.Models;
using Xunit;

namespace PartsPortal.Tests.Unit;

public sealed class CorrelationContextTests
{
    [Fact]
    public void HeaderName_IsLowercaseDashedConvention()
    {
        Assert.Equal("x-correlation-id", CorrelationContext.HeaderName);
    }

    [Fact]
    public void New_ProducesNonEmptyId()
    {
        var ctx = CorrelationContext.New();

        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
    }

    [Fact]
    public void New_ProducesUniqueIdsAcrossCalls()
    {
        var a = CorrelationContext.New();
        var b = CorrelationContext.New();

        Assert.NotEqual(a.CorrelationId, b.CorrelationId);
    }

    [Fact]
    public void FromValueOrNew_RealValue_IsUsedVerbatim()
    {
        var ctx = CorrelationContext.FromValueOrNew("abc-123");

        Assert.Equal("abc-123", ctx.CorrelationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void FromValueOrNew_NullOrBlank_GeneratesFreshNonEmptyId(string? candidate)
    {
        var ctx = CorrelationContext.FromValueOrNew(candidate);

        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
    }

    [Fact]
    public void FromHeaders_WhenHeaderPresent_ReadsTheValue()
    {
        var headers = new Dictionary<string, string?>
        {
            ["other"] = "ignored",
            [CorrelationContext.HeaderName] = "from-header",
        };

        var ctx = CorrelationContext.FromHeaders(headers);

        Assert.Equal("from-header", ctx.CorrelationId);
    }

    [Fact]
    public void FromHeaders_IsCaseInsensitiveOnHeaderName()
    {
        var headers = new Dictionary<string, string?>
        {
            ["X-Correlation-Id"] = "mixed-case",
        };

        var ctx = CorrelationContext.FromHeaders(headers);

        Assert.Equal("mixed-case", ctx.CorrelationId);
    }

    [Fact]
    public void FromHeaders_WhenHeaderAbsent_GeneratesFreshNonEmptyId()
    {
        var headers = new Dictionary<string, string?>
        {
            ["unrelated"] = "value",
        };

        var ctx = CorrelationContext.FromHeaders(headers);

        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
    }

    [Fact]
    public void FromHeaders_WhenHeaderPresentButBlank_GeneratesFreshNonEmptyId()
    {
        var headers = new Dictionary<string, string?>
        {
            [CorrelationContext.HeaderName] = "   ",
        };

        var ctx = CorrelationContext.FromHeaders(headers);

        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
    }

    [Fact]
    public void FromHeaders_EmptyDictionary_GeneratesFreshNonEmptyId()
    {
        var ctx = CorrelationContext.FromHeaders(new Dictionary<string, string?>());

        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
    }
}
