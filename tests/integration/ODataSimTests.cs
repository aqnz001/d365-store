using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PartsPortal.Mocks.ODataSim;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// T3 — exercises odata-sim in-process: header create (returns FinOps SO number),
/// header→lines FK ordering, master-data validation (unknown customer/item), and
/// transient-failure injection (Handover §8, TDD §4.4). Distinct customer/item ids
/// per test avoid shared-state collisions.
/// </summary>
public class ODataSimTests(WebApplicationFactory<ODataSimApp> factory) : IClassFixture<WebApplicationFactory<ODataSimApp>>
{
    private static Task SeedAsync(HttpClient client, string[]? items = null, string[]? customers = null)
        => client.PostJsonAsync("/admin/seed", new { items = items ?? [], customers = customers ?? [] });

    private static async Task<string> CreateHeaderAsync(HttpClient client, string customer)
    {
        var resp = await client.PostJsonAsync("/data/SalesOrderHeadersV2", new { customerAccount = customer });
        resp.EnsureSuccessStatusCode();
        return (await resp.ReadJsonAsync()).GetProperty("salesOrderNumber").GetString()!;
    }

    [Fact]
    public async Task Header_create_returns_finops_sales_order_number()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, customers: ["C-HDR"]);

        var resp = await client.PostJsonAsync("/data/SalesOrderHeadersV2", new { customerAccount = "C-HDR" });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.StartsWith("SO-", (await resp.ReadJsonAsync()).GetProperty("salesOrderNumber").GetString());
    }

    [Fact]
    public async Task Header_with_unknown_customer_is_rejected()
    {
        var client = factory.CreateClient();

        var resp = await client.PostJsonAsync("/data/SalesOrderHeadersV2", new { customerAccount = "C-NOPE" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("InvalidCustomer", (await resp.ReadJsonAsync()).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Line_create_succeeds_for_valid_header_and_item()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, items: ["ITEM-OK"], customers: ["C-LINE"]);
        var so = await CreateHeaderAsync(client, "C-LINE");

        var resp = await client.PostJsonAsync(
            "/data/SalesOrderLines",
            new { salesOrderNumber = so, itemNumber = "ITEM-OK", quantity = 2m });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal(so, (await resp.ReadJsonAsync()).GetProperty("salesOrderNumber").GetString());
    }

    [Fact]
    public async Task Line_without_existing_header_is_rejected()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, items: ["ITEM-OK"]);

        var resp = await client.PostJsonAsync(
            "/data/SalesOrderLines",
            new { salesOrderNumber = "SO-999999", itemNumber = "ITEM-OK", quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("MissingSalesOrder", (await resp.ReadJsonAsync()).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Line_with_unknown_item_is_rejected()
    {
        var client = factory.CreateClient();
        await SeedAsync(client, customers: ["C-BADITEM"]);
        var so = await CreateHeaderAsync(client, "C-BADITEM");

        var resp = await client.PostJsonAsync(
            "/data/SalesOrderLines",
            new { salesOrderNumber = so, itemNumber = "ITEM-NOPE", quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("InvalidItem", (await resp.ReadJsonAsync()).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transient_failure_header_returns_503()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-sim-fail", "transient");
        await SeedAsync(client, customers: ["C-TRANSIENT"]);

        var resp = await client.PostJsonAsync("/data/SalesOrderHeadersV2", new { customerAccount = "C-TRANSIENT" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }
}
