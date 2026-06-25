using System.Net.Http.Json;
using System.Text.Json;

namespace PartsPortal.Tests.Integration;

/// <summary>Small helpers for posting/reading JSON against the in-process mock servers.</summary>
internal static class JsonHttp
{
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body)
        => client.PostAsJsonAsync(url, body);

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage resp)
        => await resp.Content.ReadFromJsonAsync<JsonElement>();
}
