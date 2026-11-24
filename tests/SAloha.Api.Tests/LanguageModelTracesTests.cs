using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Features;

namespace SAloha.Api.Tests;

/// <summary>Traces des appels au modèle de langage, remises par le conteneur (OPT-27).</summary>
[Collection("api")]
public class LanguageModelTracesTests(ApiFixture api) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Le démarrage de l'API applique les migrations : lancé seul, ce test arrive avant elles.
        await api.Client().GetAsync("/health/live");
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        await using var db = api.Database();
        await db.LanguageModelCalls.ExecuteDeleteAsync();
        await db.OptionalFeatures.ExecuteDeleteAsync();
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("ConnectionStrings:Default", api.Database().Database.GetConnectionString())
        .UseSetting("Jwt:Key", ApiFixture.JwtKey).UseSetting("Jwt:Issuer", "S-Aloha")
        .UseSetting("Discovery:IntervalMinutes", "0").UseSetting("Discovery:VaultKey", "cle-du-coffre-de-test-uniquement-0123456789")
        .UseSetting("Features:Token", FakeFeaturesContainer.Token).UseSetting("Features:InternalPort", "8081"));

    private static async Task<int> Send(WebApplicationFactory<Program> host, object call, string? bearer = FakeFeaturesContainer.Token)
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Request.Method = "POST";
            c.Request.Path = "/internal/language-model/calls";
            c.Connection.LocalPort = 8081;
            if (bearer is not null) c.Request.Headers.Authorization = $"Bearer {bearer}";
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(call));
        });
        return context.Response.StatusCode;
    }

    internal static object Call(string feature = FeatureCatalog.AssistedCmdb, string outcome = LanguageModelCall.Valid, int duration = 1200,
        int rejected = 0, string? hash = null) => new
    {
        feature, task = "proprietaire", model = "qwen2.5:7b", location = "Dans le SI", promptVersion = "1", inputHash = hash ?? new string('0', 64),
        durationMs = duration, outcome, attempts = rejected + 1, rejected, error = (string?)null,
    };

    [Fact]
    public async Task OPT_27_le_conteneur_remet_la_trace_de_chaque_appel_sans_l_invite_ni_la_reponse()
    {
        // CMDB assistée coupée : aucune trace reçue (OPT-04) ; sans le jeton, rien (OPT-10).
        using (var off = Factory())
        {
            Assert.Equal(403, await Send(off, Call()));
            Assert.Equal(401, await Send(off, Call(), bearer: null));
        }
        await using (var db = api.Database())
        {
            db.OptionalFeatures.Add(new OptionalFeature { Key = FeatureCatalog.AssistedCmdb, Enabled = true, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        // La garde garde l'état des fonctionnalités en mémoire : un hôte neuf le relit.
        using var factory = Factory();
        Assert.Equal(201, await Send(factory, Call(duration: 1000)));
        Assert.Equal(201, await Send(factory, Call(outcome: LanguageModelCall.NonConforming, rejected: 2)));
        // Hors bornes : issue, fonctionnalité ou empreinte inconnues.
        Assert.Equal(400, await Send(factory, Call(outcome: "réussi")));
        Assert.Equal(400, await Send(factory, Call(feature: FeatureCatalog.McpServer)));
        Assert.Equal(400, await Send(factory, Call(hash: "pas-une-empreinte")));

        await using (var db = api.Database())
        {
            var calls = await db.LanguageModelCalls.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
            Assert.Equal([(LanguageModelCall.Valid, 0, 1000), (LanguageModelCall.NonConforming, 2, 1200)],
                calls.Select(c => (c.Outcome, c.Rejected, c.DurationMs)));
            Assert.All(calls, c => Assert.Equal((FeatureCatalog.AssistedCmdb, "proprietaire", new string('0', 64)), (c.Feature, c.Task, c.InputHash)));
        }
        // Ni l'invite ni la réponse : la table n'a pas de colonne pour elles.
        Assert.DoesNotContain(typeof(LanguageModelCall).GetProperties(), p => p.Name is "Prompt" or "Input" or "Response" or "Output" or "Content");
    }
}
