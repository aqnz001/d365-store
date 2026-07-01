using Microsoft.Extensions.Logging;

namespace PartsPortal.Shared.Notifications;

/// <summary>A transactional email to send (no secrets or card data; minimal PII).</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Transactional email abstraction (prod-readiness #7), shared by the BFF (order confirmation) and
/// the Sync app (shipment notifications). The implementation is chosen by config (Email:Provider) —
/// a real provider in production, a logging stand-in for Phase 1. A send failure must never fail the
/// business action that triggered it (a placed order, an applied shipment event).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Phase-1 email sender: logs the message instead of delivering it. Swapped for a real
/// provider at deploy via Email:Provider.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        logger.LogInformation("Transactional email queued → {To}: {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}

/// <summary>Customer-facing transactional email bodies (plain text; no marketing; no PII beyond the
/// recipient and order/tracking references).</summary>
public static class EmailTemplates
{
    public static EmailMessage OrderConfirmation(string to, string orderReference) => new(
        to,
        $"Your order {orderReference} is confirmed",
        $"Thanks for your order.\n\nYour order reference is {orderReference}. It's queued for processing — "
        + "you can track its status any time under your account.\n\nParts Portal");

    public static EmailMessage ShipmentDispatched(string to, string orderReference, IReadOnlyList<string> trackingNumbers)
    {
        var tracking = trackingNumbers.Count > 0
            ? "Tracking: " + string.Join(", ", trackingNumbers)
            : "Tracking details will follow shortly.";
        return new EmailMessage(
            to,
            $"Your order {orderReference} has shipped",
            $"Good news — your order {orderReference} is on its way.\n\n{tracking}\n\n"
            + "You can follow its status under your account.\n\nParts Portal");
    }
}
