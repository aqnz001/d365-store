using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PartsPortal.Shared.Availability;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Models;

namespace PartsPortal.Functions.Availability;

/// <summary>
/// Checkout-gate availability surface (TDD §4.6, §6.1; Golden Rule #5). Thin HTTP triggers
/// delegating to <see cref="ICartAvailabilityService"/>; the live IVS check is authoritative.
/// </summary>
public class CartFunctions(ICartAvailabilityService availability)
{
    [Function("CartValidate")]
    public async Task<HttpResponseData> Validate(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "cart/validate")] HttpRequestData req)
    {
        var correlationId = CorrelationId(req);
        var request = await req.ReadFromJsonAsync<CartValidateRequest>() ?? new CartValidateRequest();
        var result = await availability.ValidateAsync(request, correlationId, req.FunctionContext.CancellationToken);
        return await Json(req, HttpStatusCode.OK, result);
    }

    [Function("CartReserve")]
    public async Task<HttpResponseData> Reserve(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "cart/reserve")] HttpRequestData req)
    {
        var correlationId = CorrelationId(req);
        var request = await req.ReadFromJsonAsync<ReserveRequest>() ?? new ReserveRequest();
        var (reserved, response) = await availability.ReserveAsync(request, correlationId, req.FunctionContext.CancellationToken);
        // Shortfall (couldn't reserve the whole cart) → 409 with per-line options (TDD §6.1).
        return await Json(req, reserved ? HttpStatusCode.OK : HttpStatusCode.Conflict, response);
    }

    [Function("CartRelease")]
    public async Task<HttpResponseData> Release(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "cart/release")] HttpRequestData req)
    {
        var request = await req.ReadFromJsonAsync<ReleaseRequest>() ?? new ReleaseRequest();
        await availability.ReleaseAsync(request, req.FunctionContext.CancellationToken);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static string CorrelationId(HttpRequestData req) =>
        req.Headers.TryGetValues(CorrelationContext.HeaderName, out var values)
            ? CorrelationContext.FromValueOrNew(values.FirstOrDefault()).CorrelationId
            : CorrelationContext.New().CorrelationId;

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var response = req.CreateResponse();
        await response.WriteAsJsonAsync(body);
        response.StatusCode = status; // set after writing — WriteAsJsonAsync defaults it to 200
        return response;
    }
}
