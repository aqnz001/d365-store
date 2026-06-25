using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PartsPortal.Shared.Pricing;

namespace PartsPortal.Functions.PricingCredit;

/// <summary>
/// Resolves effective price per line + customer credit status at the checkout gate
/// (TDD §4.5, §6.1). Thin HTTP trigger delegating to <see cref="IPricingCreditService"/>.
/// </summary>
public class PricingFunctions(IPricingCreditService pricing)
{
    [Function("PricingResolve")]
    public async Task<HttpResponseData> Resolve(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "pricing/resolve")] HttpRequestData req)
    {
        var request = await req.ReadFromJsonAsync<PricingResolveRequest>() ?? new PricingResolveRequest(string.Empty, []);
        var result = await pricing.ResolveAsync(request, req.FunctionContext.CancellationToken);

        var response = req.CreateResponse();
        await response.WriteAsJsonAsync(result);
        response.StatusCode = HttpStatusCode.OK; // set after writing — WriteAsJsonAsync defaults it to 200
        return response;
    }
}
