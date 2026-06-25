using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace PartsPortal.Functions.PricingCredit;

/// <summary>
/// Resolves effective price per line + customer credit status at the checkout gate
/// (TDD §4.5, §6.1). Calls the pricing/credit service (mock now). Logic lands in T7.
/// </summary>
public class PricingFunctions
{
    [Function("PricingResolve")]
    public HttpResponseData Resolve(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "pricing/resolve")] HttpRequestData req)
    {
        // TODO(T7): resolve net price (trade agreements) + credit status; lock prices on cart.
        var res = req.CreateResponse(HttpStatusCode.NotImplemented);
        res.Headers.Add("Content-Type", "application/json; charset=utf-8");
        res.WriteString("{\"status\":\"scaffolded\",\"task\":\"T7\"}");
        return res;
    }
}
