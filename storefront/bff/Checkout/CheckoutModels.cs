using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Checkout;

/// <summary>Outcome of the checkout gate.</summary>
public enum CheckoutStatus
{
    /// <summary>Reserved, priced, credit OK (or approval-required) — proceed to payment.</summary>
    Ready,

    /// <summary>One or more lines are unavailable (decision Block) — cannot proceed.</summary>
    AvailabilityBlocked,

    /// <summary>Account is on credit hold — blocked (over-limit is allowed through as approval-required).</summary>
    CreditBlocked,

    /// <summary>Insufficient availability to reserve the whole cart (all-or-nothing, DR-009).</summary>
    Shortfall,
}

/// <summary>
/// Result of the checkout gate (TDD §6.1): live availability + locked prices + credit decision +
/// soft reservation, evaluated before any payment. <see cref="AllowOnAccount"/> tells the SPA
/// whether the net-terms (pay-on-account) option may be offered — true only when credit is
/// Approved (DR-019). The server re-checks this authoritatively at pay time regardless.
/// </summary>
public sealed record CheckoutResult(
    CheckoutStatus Status,
    IReadOnlyList<string> ReservationIds,
    CartPricingResult? Pricing,
    CartValidateResponse Availability,
    string? Message,
    bool AllowOnAccount);
