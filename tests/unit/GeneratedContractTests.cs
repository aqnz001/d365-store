using System.Text.Json;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Contracts.Middleware;
using Xunit;

// Money is defined by multiple contracts; this test uses the order-message envelope's.
using Money = PartsPortal.Shared.Contracts.Messages.Money;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// Smoke tests over the DTOs generated from integration/contracts (T2). They prove the
/// generated models are usable and that the contract wire-names survive serialization —
/// and act as a regression guard if a contract change silently breaks generation.
/// </summary>
public class GeneratedContractTests
{
    [Fact]
    public void OrderInboundMessage_round_trips_through_system_text_json()
    {
        var message = new OrderInboundMessage
        {
            IdempotencyKey = "idem-123",
            CorrelationId = "corr-456",
            SessionId = "C-1001/site-1",
            CustomerAccount = "C-1001",
            Currency = "GBP",
            PlacedAtUtc = new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
            Lines =
            {
                new OrderLine
                {
                    ItemNumber = "PART-001",
                    Quantity = 2,
                    Unit = "ea",
                    Site = "1",
                    Backorder = false,
                    ReservationReference = "RSV-abc",
                    LockedPrice = new Money { Amount = 19.95, Currency = "GBP" },
                },
            },
        };

        var json = JsonSerializer.Serialize(message);

        // camelCase wire names from the contract's JsonPropertyName mappings are honored.
        Assert.Contains("\"idempotencyKey\":\"idem-123\"", json);
        Assert.Contains("\"lockedPrice\"", json);

        var roundTripped = JsonSerializer.Deserialize<OrderInboundMessage>(json)!;
        Assert.Equal("idem-123", roundTripped.IdempotencyKey);
        Assert.Single(roundTripped.Lines);
        Assert.Equal("PART-001", roundTripped.Lines.First().ItemNumber);
        Assert.Equal(19.95, roundTripped.Lines.First().LockedPrice.Amount);
    }

    [Fact]
    public void AvailabilityBand_enum_is_generated_from_the_middleware_contract()
    {
        // /cart/validate publishes bands, never raw on-hand (Golden Rule #4).
        Assert.Equal(0, (int)AvailabilityBand.InStock);
        Assert.True(Enum.IsDefined(AvailabilityBand.Backorder));
    }
}
