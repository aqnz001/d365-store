using PartsPortal.Bff.Account;
using PartsPortal.Bff.Cart;
using PartsPortal.Bff.Clients;
using PartsPortal.Bff.Payments;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Notifications;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Bff.Company;

/// <summary>Outcome of an approve/reject action.</summary>
public sealed record ApprovalResult(bool Ok, bool NotFound, string? Error, string? OrderReference);

/// <summary>
/// Order approval workflow (DR-027). An over-limit on-account order sits as a <see cref="PendingApproval"/>;
/// an Approver/Admin approves it — which re-reserves the snapshot lines, re-prices, and submits the
/// order on account (reserve-before-commit still holds — Golden Rule #5) — or rejects it. The
/// approver's decision is authoritative: a request is decided on its own merits and is deliberately
/// not re-validated against the buyer's current role/limit (which may have changed since placement).
/// </summary>
public sealed class ApprovalService(
    IApprovalStore store,
    IMiddlewareApi middleware,
    IOrderHistoryStore history,
    IEmailSender email)
{
    public IReadOnlyList<PendingApproval> Pending(string companyAccount) => store.List(companyAccount);

    public async Task<ApprovalResult> ApproveAsync(string companyAccount, string approverUserId, string id, string correlationId, CancellationToken ct = default)
    {
        // Atomically claim the request: only the caller that flips it out of Pending proceeds, so two
        // concurrent approvals (or a double-click) can't both reserve+submit and place two orders
        // (Golden Rule #6). We revert to Pending below if the reserve/submit fails.
        var claimed = false;
        var pending = store.Transition(companyAccount, id, a =>
        {
            if (a.Status != ApprovalStatus.Pending)
            {
                return null;
            }

            claimed = true;
            return a with { Status = ApprovalStatus.Approved, DecidedBy = approverUserId };
        });
        if (pending is null)
        {
            return new ApprovalResult(false, true, null, null);
        }

        if (!claimed)
        {
            return new ApprovalResult(false, false, "This request has already been decided.", null);
        }

        List<string>? acquired = null;
        try
        {
            // Reserve the snapshot lines now (the pending order held none) — reserve-before-commit.
            var reserveRequest = new ReserveRequest { Customer = new CustomerRef { CustomerAccount = companyAccount } };
            foreach (var line in pending.Lines)
            {
                reserveRequest.Lines.Add(new CartLineInput { ItemNumber = line.ItemNumber, Quantity = (double)line.Quantity, Site = line.Site });
            }

            var (reserved, reserveResponse) = await middleware.ReserveAsync(reserveRequest, correlationId, ct);
            if (!reserved)
            {
                RevertToPending(companyAccount, id);
                return new ApprovalResult(false, false, "The items are no longer available to reserve — the order can't be approved.", null);
            }

            acquired = reserveResponse.ReservationIds.ToList();

            // Re-resolve pricing (authoritative) and submit the order on account.
            var pricing = await middleware.ResolvePricingAsync(
                new PricingResolveRequest(companyAccount, pending.Lines.Select(l => new PricingResolveLine(l.ItemNumber, l.Quantity)).ToList()),
                correlationId,
                ct);
            var unitPriceByItem = pricing.Lines
                .GroupBy(l => l.ItemNumber)
                .ToDictionary(g => g.Key, g => g.First().UnitPrice, StringComparer.Ordinal);

            // Every reserved line must have an authoritative price. If the (later) re-price is missing a
            // line — e.g. an item discontinued since the request — never submit it at a silent £0. Release
            // the reservation and re-open the request rather than book a zero-price order.
            if (pending.Lines.Any(l => !unitPriceByItem.ContainsKey(l.ItemNumber)))
            {
                await ReleaseQuietlyAsync(acquired, correlationId, ct);
                RevertToPending(companyAccount, id);
                return new ApprovalResult(false, false, "Pricing is unavailable for one or more items — the order can't be approved right now.", null);
            }

            var order = new OrderRequest
            {
                IdempotencyKey = PaymentService.DeriveIdempotencyKey(companyAccount, acquired),
                CorrelationId = correlationId,
                Customer = new CustomerRef { CustomerAccount = companyAccount },
                Currency = pending.Currency,
                PaymentMethod = "OnAccount",
            };
            if (!string.IsNullOrWhiteSpace(pending.PoNumber))
            {
                order.PurchaseOrderNumber = pending.PoNumber;
            }

            order.ReservationIds = acquired;
            foreach (var line in pending.Lines)
            {
                order.Lines.Add(new OrderLineInput
                {
                    ItemNumber = line.ItemNumber,
                    Quantity = (double)line.Quantity,
                    Unit = "ea",
                    Site = line.Site,
                    RequestedShipDate = DateTimeOffset.UtcNow,
                    LockedPrice = new Money { Amount = (double)unitPriceByItem[line.ItemNumber], Currency = pending.Currency },
                });
            }

            var ack = await middleware.SubmitOrderAsync(order, correlationId, ct);
            var reference = ack.SalesOrderNumber ?? ack.OrderId ?? "pending";
            history.Record(companyAccount, new PlacedOrder(reference, DateTimeOffset.UtcNow));
            store.Transition(companyAccount, id, a => a with { OrderReference = reference });
            await SendConfirmationAsync(pending.BuyerUserId, reference, ct);

            return new ApprovalResult(true, false, null, reference);
        }
        catch
        {
            // A mid-approval failure (after we reserved) must not strand the row as Approved or leak the
            // reservation until the TTL sweep — release it and re-open the request.
            if (acquired is not null)
            {
                await ReleaseQuietlyAsync(acquired, correlationId, ct);
            }

            RevertToPending(companyAccount, id);
            throw;
        }
    }

    private async Task ReleaseQuietlyAsync(IReadOnlyList<string> reservationIds, string correlationId, CancellationToken ct)
    {
        try
        {
            await middleware.ReleaseAsync(
                new ReleaseRequest { CorrelationId = correlationId, ReservationIds = reservationIds.ToList() }, correlationId, ct);
        }
        catch
        {
            // Best-effort release; the reservation TTL sweep (DR-015) is the backstop.
        }
    }

    public ApprovalResult Reject(string companyAccount, string approverUserId, string id)
    {
        // Atomically claim (mirrors ApproveAsync) so an approve and a reject can't both win.
        var claimed = false;
        var pending = store.Transition(companyAccount, id, a =>
        {
            if (a.Status != ApprovalStatus.Pending)
            {
                return null;
            }

            claimed = true;
            return a with { Status = ApprovalStatus.Rejected, DecidedBy = approverUserId };
        });
        if (pending is null)
        {
            return new ApprovalResult(false, true, null, null);
        }

        return claimed
            ? new ApprovalResult(true, false, null, null)
            : new ApprovalResult(false, false, "This request has already been decided.", null);
    }

    private void RevertToPending(string companyAccount, string id) =>
        store.Transition(companyAccount, id, a => a with { Status = ApprovalStatus.Pending, DecidedBy = null });

    private async Task SendConfirmationAsync(string buyerEmail, string orderReference, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(EmailTemplates.OrderConfirmation(buyerEmail, orderReference), ct);
        }
        catch
        {
            // A confirmation-email failure must never fail an approved order.
        }
    }
}
