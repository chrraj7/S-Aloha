using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Observability;

namespace SAloha.Api.Tests;

/// <summary>O1 (partie 1, #121) : sondes de vie et de disponibilité, sans jeton.</summary>
[Collection("api")]
public class HealthProbesTests(ApiFixture api)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task O1_les_sondes_repondent_200_sans_jeton_et_sans_detail(string route)
    {
        var res = await api.AnonymousClient().GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        // Ni chaîne de connexion ni version : l'état seul.
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task O1_base_injoignable_disponibilite_en_echec()
    {
        // Port 1 : aucune base n'y répond ; la connexion échoue sans attendre.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2").Options);
        var result = await new DatabaseReady(db).CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", _ => null!, HealthStatus.Unhealthy, null)
        });
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
