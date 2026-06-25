using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace PartsPortal.Functions.Availability;

/// <summary>
/// Checkout-gate availability surface (TDD §4.6, §6.1; Golden Rule #5: the live check
/// is authoritative). Live ATP check, band logic, soft reservation + release land in T6.
/// </summary>
public class CartFunctions
{
    [Function("CartValidate")]
    public HttpResponseData Validate(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "cart/validate")] HttpRequestData req)
        => Scaffolded(req, "T6"); // TODO(T6): IVS indexquery (ATP) + price/credit → band/decision.

    [Function("CartReserve")]
    public HttpResponseData Reserve(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "cart/reserve")] HttpRequestData req)
        => Scaffolded(req, "T6"); // TODO(T6): IVS /onhand/reserve (ifCheckAvailForReserv) → reservation IDs.

    [Function("CartRelease")]
    public HttpResponseData Release(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "cart/release")] HttpRequestData req)
        => Scaffolded(req, "T6"); // TODO(T6): release reservation(s) via IVS.

    private static HttpResponseData Scaffolded(HttpRequestData req, string task)
    {
        var res = req.CreateResponse(HttpStatusCode.NotImplemented);
        res.Headers.Add("Content-Type", "application/json; charset=utf-8");
        res.WriteString("{\"status\":\"scaffolded\",\"task\":\"" + task + "\"}");
        return res;
    }
}
