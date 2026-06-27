using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PartsPortal.Shared.Status;

namespace PartsPortal.Functions.Sync;

/// <summary>
/// HTTP order-status read (the production endpoint the BFF calls for account/order detail). Returns
/// the storefront order-status mirror (TDD §6.3) — accumulated fulfilments + remaining backorder —
/// mapped to the middleware contract. 404 until the first status event for the order arrives.
/// </summary>
public class OrderStatusFunction(IOrderStatusStore store)
{
    [Function("OrderStatusRead")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "order/{reference}/status")] HttpRequestData req,
        string reference)
    {
        var view = store.Get(reference);
        if (view is null)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(OrderStatusMapper.ToResponse(view));
        return response;
    }
}
