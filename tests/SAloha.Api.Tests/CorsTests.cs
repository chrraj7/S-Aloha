namespace SAloha.Api.Tests;

/// <summary>S3 (#122) : CORS limité aux origines de `Cors:AllowedOrigins` (celle d'ApiFixture).</summary>
[Collection("api")]
public class CorsTests(ApiFixture api)
{
    private async Task<HttpResponseMessage> Preflight(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/incidents");
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        return await api.AnonymousClient().SendAsync(req);
    }

    [Fact]
    public async Task S3_une_origine_listee_recoit_l_autorisation()
    {
        var res = await Preflight(ApiFixture.AllowedOrigin);
        Assert.Equal(ApiFixture.AllowedOrigin, Assert.Single(res.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Theory]
    [InlineData("https://ailleurs.example")]
    [InlineData("https://frontend.example.evil")]
    public async Task S3_une_origine_non_listee_ne_la_recoit_pas(string origin)
    {
        var res = await Preflight(origin);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
        // Requête simple aussi : pas d'en-tête, le navigateur bloque la lecture de la réponse.
        var get = new HttpRequestMessage(HttpMethod.Get, "/api/incidents");
        get.Headers.Add("Origin", origin);
        Assert.False((await api.Client("User", "s3.user").SendAsync(get)).Headers.Contains("Access-Control-Allow-Origin"));
    }
}
