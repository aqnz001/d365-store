using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// #7 B2B governance — company members &amp; roles: bootstrap admin, role resolution, admin-only
/// management, and the always-one-admin invariant. Companies are isolated by distinct accounts
/// (the member store is a shared singleton across the class fixture).
/// </summary>
public class BffCompanyTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    private HttpClient Client(string company, string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.CustomerHeader, company);
        client.DefaultRequestHeaders.Add(DevAuthenticationHandler.UserHeader, user);
        return client;
    }

    private static object Member(string userId, string name, string role, decimal? spendLimit = null) =>
        new { userId, name, role, spendLimit };

    [Fact]
    public async Task First_user_of_a_company_is_admin_and_can_add_members()
    {
        var alice = Client("ACME-1", "alice@acme.test");

        var me = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("Admin", me.GetProperty("role").GetString()); // bootstrap admin on an empty company

        var add = await alice.PostAsJsonAsync("/api/company/members", Member("bob@acme.test", "Bob", "Buyer", 500m));
        add.EnsureSuccessStatusCode();

        var members = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/members");
        Assert.Equal(2, members!.Count); // alice is persisted as Admin on the first management action
        Assert.Contains(members, m => m.GetProperty("userId").GetString() == "bob@acme.test" && m.GetProperty("role").GetString() == "Buyer");

        var bobRole = await Client("ACME-1", "bob@acme.test").GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("Buyer", bobRole.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Non_admin_cannot_manage_members()
    {
        var alice = Client("ACME-2", "alice@acme.test");
        await alice.PostAsJsonAsync("/api/company/members", Member("bob@acme.test", "Bob", "Buyer"));

        var bob = Client("ACME-2", "bob@acme.test");
        var response = await bob.PostAsJsonAsync("/api/company/members", Member("carol@acme.test", "Carol", "Buyer"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_removed()
    {
        var alice = Client("ACME-3", "alice@acme.test");
        await alice.PostAsJsonAsync("/api/company/members", Member("bob@acme.test", "Bob", "Buyer"));

        var blocked = await alice.DeleteAsync("/api/company/members/alice@acme.test");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode); // alice is the only admin

        // Promote bob to Admin, then alice can be removed.
        await alice.PostAsJsonAsync("/api/company/members", Member("bob@acme.test", "Bob", "Admin"));
        var removed = await alice.DeleteAsync("/api/company/members/alice@acme.test");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Fact]
    public async Task Edit_is_keyed_by_the_route_and_cannot_change_the_member_email()
    {
        var alice = Client("ACME-5", "alice@acme.test");
        await alice.PostAsJsonAsync("/api/company/members", Member("bob@acme.test", "Bob", "Buyer"));

        // PUT the bob record but with a DIFFERENT userId in the body — the route id must win, so bob
        // is updated to Approver and no phantom "eve" member is created.
        var put = await alice.PutAsJsonAsync("/api/company/members/bob@acme.test",
            Member("eve@acme.test", "Bob", "Approver"));
        put.EnsureSuccessStatusCode();

        var members = await alice.GetFromJsonAsync<List<JsonElement>>("/api/company/members");
        Assert.Equal(2, members!.Count); // alice (admin) + bob — no phantom eve
        Assert.DoesNotContain(members, m => m.GetProperty("userId").GetString() == "eve@acme.test");
        var bob = Assert.Single(members, m => m.GetProperty("userId").GetString() == "bob@acme.test");
        Assert.Equal("Approver", bob.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Member_missing_a_required_field_is_rejected()
    {
        var alice = Client("ACME-4", "alice@acme.test");
        var response = await alice.PostAsJsonAsync("/api/company/members", new { userId = "", name = "No Email", role = "Buyer" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
