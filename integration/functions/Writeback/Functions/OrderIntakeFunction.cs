using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Contracts.Middleware;
using PartsPortal.Shared.Writeback;

namespace PartsPortal.Functions.Writeback;

/// <summary>
/// HTTP order-intake (the production producer for the orders-inbound queue). The BFF posts the
/// placed order here (via APIM); we enqueue it for the queue-backed writeback and return a Queued
/// ack with the portal order id — checkout never blocks on the ERP (Golden Rule #7). The FinOps SO
/// number is back-filled by the writeback consumer (DR-014).
/// </summary>
public class OrderIntakeFunction(IOrderIntake intake)
{
    [Function("OrderIntake")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "order")] HttpRequestData req,
        FunctionContext context)
    {
        var order = await req.ReadFromJsonAsync<OrderRequest>();
        if (order is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Could not read the order.");
            return bad;
        }

        var ack = await intake.SubmitAsync(order, context.CancellationToken);
        context.GetLogger<OrderIntakeFunction>()
            .LogInformation("Order intake {OrderId} → {Status}.", ack.OrderId, ack.Status);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(ack);
        return response;
    }
}
