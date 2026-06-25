using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Mapping;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Behavioural tests for the availability band calculator (Golden Rules #4/#5,
/// TDD §7.2). Buffers and thresholds are supplied via config — the tests assert
/// banding/boundary logic, the published = max(0, ATP - buffer) rule, and band
/// precedence, never raw on-hand exposure.
/// </summary>
public class AvailabilityBandCalculatorTests
{
    // Deliberately non-round, config-supplied values so no hardcoded literal can
    // accidentally satisfy the assertions.
    private const string StandardClass = "STD";
    private const string FastMovingClass = "FAST";

    private static AvailabilityBandCalculator NewCalculator(
        decimal defaultBuffer = 5m,
        decimal lowStockThreshold = 10m,
        decimal? fastMovingBuffer = 2m)
    {
        var options = new AvailabilityOptions
        {
            DefaultBuffer = defaultBuffer,
            LowStockThreshold = lowStockThreshold,
            ClassBuffers = new Dictionary<string, decimal>(),
        };

        if (fastMovingBuffer is not null)
        {
            options.ClassBuffers[FastMovingClass] = fastMovingBuffer.Value;
        }

        return new AvailabilityBandCalculator(options);
    }

    [Fact]
    public void Calculate_PublishedAtpAboveThreshold_IsInStock()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 100 - 5 = 95, well above the 10 threshold.
        var result = calc.Calculate(rawAtp: 100m, StandardClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(AvailabilityBand.InStock, result.Band);
        Assert.Equal(95m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_PublishedAtpExactlyAtThreshold_IsLowStock()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 15 - 5 = 10, exactly the threshold -> not strictly greater -> LowStock.
        var result = calc.Calculate(rawAtp: 15m, StandardClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(AvailabilityBand.LowStock, result.Band);
        Assert.Equal(10m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_PublishedAtpJustAboveThreshold_IsInStock()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 16 - 5 = 11, just over the threshold.
        var result = calc.Calculate(rawAtp: 16m, StandardClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(AvailabilityBand.InStock, result.Band);
        Assert.Equal(11m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_PublishedAtpBetweenZeroAndThreshold_IsLowStock()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 8 - 5 = 3, in (0, threshold] -> LowStock.
        var result = calc.Calculate(rawAtp: 8m, StandardClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(AvailabilityBand.LowStock, result.Band);
        Assert.Equal(3m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_BufferBringsPublishedToZero_BackorderableIsBackorder()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 5 - 5 = 0 (exactly), not > 0 -> backorderable yields Backorder.
        var result = calc.Calculate(rawAtp: 5m, StandardClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(AvailabilityBand.Backorder, result.Band);
        Assert.Equal(0m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_PublishedZero_NotBackorderable_IsUnavailable()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 5 - 5 = 0, not backorderable -> Unavailable.
        var result = calc.Calculate(rawAtp: 5m, StandardClass, backorderable: false, madeToOrder: false, discontinued: false);

        Assert.Equal(AvailabilityBand.Unavailable, result.Band);
        Assert.Equal(0m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_BufferExceedsRawAtp_PublishedNeverNegative_AndBackorder()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // 2 - 5 = -3 -> clamped to 0 (never publish a negative count).
        var result = calc.Calculate(rawAtp: 2m, StandardClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(0m, result.PublishedAtp);
        Assert.Equal(AvailabilityBand.Backorder, result.Band);
    }

    [Fact]
    public void Calculate_NegativeRawAtp_PublishedClampedToZero()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // Raw ATP can go negative on oversell; published must still clamp to 0.
        var result = calc.Calculate(rawAtp: -50m, StandardClass, backorderable: false, madeToOrder: false, discontinued: false);

        Assert.Equal(0m, result.PublishedAtp);
        Assert.Equal(AvailabilityBand.Unavailable, result.Band);
    }

    [Fact]
    public void Calculate_ClassSpecificBuffer_BeatsDefault()
    {
        // Default buffer 5 would yield 100 - 5 = 95; class buffer 2 yields 98.
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m, fastMovingBuffer: 2m);

        var result = calc.Calculate(rawAtp: 100m, FastMovingClass, backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(98m, result.PublishedAtp);
        Assert.Equal(AvailabilityBand.InStock, result.Band);
    }

    [Fact]
    public void Calculate_UnknownClass_FallsBackToDefaultBuffer()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m, fastMovingBuffer: 2m);

        // "UNKNOWN" has no class entry -> default buffer 5 -> 100 - 5 = 95.
        var result = calc.Calculate(rawAtp: 100m, "UNKNOWN", backorderable: true, madeToOrder: false, discontinued: false);

        Assert.Equal(95m, result.PublishedAtp);
        Assert.Equal(AvailabilityBand.InStock, result.Band);
    }

    [Fact]
    public void Calculate_Discontinued_TakesPrecedenceOverEverything()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // Plenty of stock and made-to-order/backorderable flags set, but discontinued wins.
        var result = calc.Calculate(rawAtp: 100m, StandardClass, backorderable: true, madeToOrder: true, discontinued: true);

        Assert.Equal(AvailabilityBand.Unavailable, result.Band);
        // Published ATP is still computed/clamped, just not used for banding.
        Assert.Equal(95m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_MadeToOrder_TakesPrecedenceOverStockBands()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // Healthy stock, but made-to-order (and not discontinued) wins over InStock.
        var result = calc.Calculate(rawAtp: 100m, StandardClass, backorderable: true, madeToOrder: true, discontinued: false);

        Assert.Equal(AvailabilityBand.MadeToOrder, result.Band);
        Assert.Equal(95m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_MadeToOrder_WithZeroStock_StillMadeToOrder()
    {
        var calc = NewCalculator(defaultBuffer: 5m, lowStockThreshold: 10m);

        // No effective stock, but made-to-order precedence beats Backorder/Unavailable.
        var result = calc.Calculate(rawAtp: 0m, StandardClass, backorderable: false, madeToOrder: true, discontinued: false);

        Assert.Equal(AvailabilityBand.MadeToOrder, result.Band);
        Assert.Equal(0m, result.PublishedAtp);
    }

    [Fact]
    public void Calculate_ZeroBuffers_PublishesRawAtpAndBandsByThreshold()
    {
        // With no buffer configured, published == raw ATP (still banded, never raw-on-hand semantics).
        var calc = NewCalculator(defaultBuffer: 0m, lowStockThreshold: 10m, fastMovingBuffer: null);

        var result = calc.Calculate(rawAtp: 10m, StandardClass, backorderable: false, madeToOrder: false, discontinued: false);

        // 10 - 0 = 10, exactly at threshold -> LowStock.
        Assert.Equal(10m, result.PublishedAtp);
        Assert.Equal(AvailabilityBand.LowStock, result.Band);
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AvailabilityBandCalculator(null!));
    }
}
