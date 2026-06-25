using PartsPortal.Shared.Contracts.Middleware;

namespace PartsPortal.Shared.Mapping;

/// <summary>
/// Config-bound thresholds and per-class buffers for availability publishing
/// (Open Decision #5 — values come from configuration, never hardcoded literals).
/// </summary>
public sealed class AvailabilityOptions
{
    /// <summary>
    /// Per-item-class buffer subtracted from raw ATP before publishing. Keyed by
    /// item class. Classes absent here fall back to <see cref="DefaultBuffer"/>.
    /// </summary>
    public Dictionary<string, decimal> ClassBuffers { get; set; } = new();

    /// <summary>
    /// Buffer applied when the item class has no specific entry in
    /// <see cref="ClassBuffers"/>.
    /// </summary>
    public decimal DefaultBuffer { get; set; }

    /// <summary>
    /// Published-ATP boundary above which an item is banded as InStock. At or below
    /// this value (but still &gt; 0) the item is LowStock.
    /// </summary>
    public decimal LowStockThreshold { get; set; }
}

/// <summary>
/// Outcome of an availability calculation: the band to publish and the
/// buffer-adjusted ATP. <see cref="PublishedAtp"/> is bandable/advisory and is
/// never negative — it is not a raw on-hand count (Golden Rules #4/#5, TDD §7.2).
/// </summary>
public readonly record struct AvailabilityResult(AvailabilityBand Band, decimal PublishedAtp);

/// <summary>
/// Derives the published availability band from raw ATP using config-driven
/// per-class buffers and thresholds. Publishes <c>max(0, ATP − classBuffer)</c>
/// and a band — never raw on-hand or exact counts (Golden Rule #4).
/// </summary>
public sealed class AvailabilityBandCalculator
{
    private readonly AvailabilityOptions _options;

    public AvailabilityBandCalculator(AvailabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Computes the published ATP and band for a single item.
    /// </summary>
    /// <param name="rawAtp">Raw available-to-promise from IVS (advisory; the live check is authoritative).</param>
    /// <param name="itemClass">Item class used to select the buffer.</param>
    /// <param name="backorderable">Whether a zero/negative published ATP may still be offered as backorder.</param>
    /// <param name="madeToOrder">Whether the item is made to order.</param>
    /// <param name="discontinued">Whether the item is discontinued.</param>
    public AvailabilityResult Calculate(
        decimal rawAtp,
        string itemClass,
        bool backorderable,
        bool madeToOrder,
        bool discontinued)
    {
        var buffer = BufferFor(itemClass);
        var publishedAtp = Math.Max(0m, rawAtp - buffer);

        var band = DeriveBand(publishedAtp, backorderable, madeToOrder, discontinued);

        return new AvailabilityResult(band, publishedAtp);
    }

    private decimal BufferFor(string itemClass)
    {
        if (itemClass is not null
            && _options.ClassBuffers.TryGetValue(itemClass, out var classBuffer))
        {
            return classBuffer;
        }

        return _options.DefaultBuffer;
    }

    private AvailabilityBand DeriveBand(
        decimal publishedAtp,
        bool backorderable,
        bool madeToOrder,
        bool discontinued)
    {
        // Precedence: discontinued > made-to-order > stock bands.
        if (discontinued)
        {
            return AvailabilityBand.Unavailable;
        }

        if (madeToOrder)
        {
            return AvailabilityBand.MadeToOrder;
        }

        if (publishedAtp > _options.LowStockThreshold)
        {
            return AvailabilityBand.InStock;
        }

        if (publishedAtp > 0m)
        {
            return AvailabilityBand.LowStock;
        }

        // publishedAtp <= 0
        return backorderable ? AvailabilityBand.Backorder : AvailabilityBand.Unavailable;
    }
}
