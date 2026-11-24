using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.Tests;

/// <summary>OPT-24 : jetons stables le temps d'une requête, rétablis dans la réponse.</summary>
public class PseudonymizerTests
{
    [Fact]
    public void OPT_24_le_nom_d_hote_et_l_adresse_sont_remplaces_par_des_jetons_puis_retablis()
    {
        var pseudo = new Pseudonymizer();
        var sent = pseudo.Apply("Serveur de paie : srv-paie-01.exemple.local, 10.1.2.3 ; joignable aussi par 10.1.2.3.");
        Assert.DoesNotContain("srv-paie-01", sent);
        Assert.DoesNotContain("10.1.2.3", sent);
        Assert.Equal("Serveur de paie : HOTE_1, IP_1 ; joignable aussi par IP_1.", sent);
        Assert.Equal("srv-paie-01.exemple.local est en 10.1.2.3", pseudo.Restore("HOTE_1 est en IP_1"));
    }

    [Fact]
    public void OPT_24_MAC_courriel_compte_IPv6_et_valeurs_declarees()
    {
        var pseudo = new Pseudonymizer().Declare(Pseudonymizer.Serial, "SN-7781-AX").Declare(Pseudonymizer.Host, "PAIE01");
        var sent = pseudo.Apply("PAIE01 (série SN-7781-AX), carte 00:1a:2b:3c:4d:5e, fe80::1:2, géré par EXEMPLE\\hery.rakoto, alerte à paie@exemple.mg");
        foreach (var secret in new[] { "PAIE01", "SN-7781-AX", "00:1a:2b:3c:4d:5e", "fe80::1:2", "hery.rakoto", "paie@exemple.mg" })
            Assert.DoesNotContain(secret, sent);
        Assert.Contains("SERIE_1", sent);
        Assert.Contains("MAC_1", sent);
        Assert.Contains("COMPTE_1", sent);
        Assert.Contains("COURRIEL_1", sent);
        // Les mots ordinaires et les nombres ne bougent pas ; un jeton jamais émis est reconnu comme inventé (OPT-25).
        Assert.Equal("Version 2.4 du lot 12 à 10:30:00 : ok.", new Pseudonymizer().Apply("Version 2.4 du lot 12 à 10:30:00 : ok."));
        Assert.Equal(["IP_7"], pseudo.UnknownTokens("HOTE_1 et IP_7"));
    }

    [Fact]
    public void OPT_25_le_schema_de_la_tache_borne_la_sortie()
    {
        var schema = JsonDocument.Parse("""
            { "type": "object", "required": ["champ", "valeur"], "additionalProperties": false,
              "properties": { "champ": { "type": "string", "enum": ["criticite", "proprietaire"] }, "valeur": { "type": "string", "maxLength": 20 },
                              "confiance": { "type": "number" }, "cites": { "type": "array", "maxItems": 2, "items": { "type": "integer" } } } }
            """).RootElement;
        string? Check(string json) => SchemaCheck.Error(JsonDocument.Parse(json).RootElement, schema);
        Assert.Null(Check("""{ "champ": "criticite", "valeur": "Vitale", "confiance": 0.8, "cites": [1, 2] }"""));
        Assert.Contains("manquant", Check("""{ "champ": "criticite" }"""));
        Assert.Contains("hors de la liste", Check("""{ "champ": "mot de passe", "valeur": "x" }"""));
        Assert.Contains("inattendue", Check("""{ "champ": "criticite", "valeur": "x", "commande": "rm" }"""));
        Assert.Contains("attendu", Check("""{ "champ": "criticite", "valeur": 3 }"""));
        Assert.Contains("éléments", Check("""{ "champ": "criticite", "valeur": "x", "cites": [1, 2, 3] }"""));
        Assert.Contains("attendu", Check("""{ "champ": "criticite", "valeur": "x", "cites": [1.5] }"""));
    }

    [Fact]
    public void OPT_22_les_plages_du_SI()
    {
        foreach (var ip in new[] { "127.0.0.1", "::1", "10.2.3.4", "172.20.0.9", "169.254.1.1", "fd00::1", "fe80::1", "::ffff:10.0.0.1" })
            Assert.True(LanguageModelClient.IsInternal(IPAddress.Parse(ip)), ip);
        foreach (var ip in new[] { "8.8.8.8", "172.32.0.1", "2001:db8::1", "::ffff:8.8.8.8" })
            Assert.False(LanguageModelClient.IsInternal(IPAddress.Parse(ip)), ip);
    }
}

/// <summary>
/// Serveur d'inférence factice au format de complétion de conversation (OPT-21), et port interne de l'API qui remet les
/// réglages (OPT-23). Chaque requête au modèle est gardée ; les réponses se règlent par une file.
/// </summary>
public sealed class FakeModelServer : IAsyncLifetime
{
    private WebApplication app = null!;
    public string Url { get; private set; } = "";
    /// <summary>Réglages servis par <c>GET /internal/language-model</c> ; null : 403 (fonctionnalité coupée).</summary>
    public volatile object? Settings;
    public readonly ConcurrentQueue<JsonElement> Requests = new();
    public readonly ConcurrentQueue<string> Authorizations = new();
    /// <summary>OPT-27 : traces remises par le conteneur.</summary>
    public readonly ConcurrentQueue<JsonElement> Traces = new();
    /// <summary>Contenus rendus tour à tour ; vide : la dernière règle reste.</summary>
    public readonly ConcurrentQueue<Func<JsonElement, Task<IResult>>> Replies = new();
    public volatile bool RefuseResponseFormat;

    public static IResult Content(string content) => Results.Json(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.MapGet("/internal/events", () => Results.StatusCode(403));
        app.MapGet("/internal/language-model", () => Settings is { } s ? Results.Json(s) : Results.StatusCode(403));
        app.MapPost("/internal/language-model/calls", async (HttpContext c) =>
        {
            Traces.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(new { id = Traces.Count }, statusCode: 201);
        });
        app.MapPost("/v1/chat/completions", async (HttpContext c) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body);
            Requests.Enqueue(body);
            Authorizations.Enqueue(c.Request.Headers.Authorization.ToString());
            if (RefuseResponseFormat && body.TryGetProperty("response_format", out _))
                return Results.Json(new { error = "response_format inconnu" }, statusCode: 400);
            return Replies.TryDequeue(out var reply) ? await reply(body) : Content("{}");
        });
        await app.StartAsync();
        Url = app.Urls.Single();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>Connecteur du modèle de langage dans le conteneur (OPT-21 à OPT-25, OPT-28).</summary>
public class LanguageModelClientTests(FakeModelServer model) : IClassFixture<FakeModelServer>, IDisposable
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        { "type": "object", "required": ["proprietaire", "motif"], "additionalProperties": false,
          "properties": { "proprietaire": { "type": "string" }, "motif": { "type": "string" } } }
        """).RootElement;

    private readonly WebApplicationFactory<Program> container = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Features:Token", FakeInternalApi.Token);
        b.UseSetting("Api:Url", model.Url);
        b.UseSetting("Events:PollMilliseconds", "60000");
        b.UseSetting("Events:IdleMilliseconds", "60000");
    });

    public void Dispose() => container.Dispose();

    private object Settings(bool enabled = true, string? url = null, string location = LanguageModelSettingsData.InSi, int timeout = 120, int maxKb = 64) =>
        new { enabled, location, url = url ?? model.Url + "/v1", model = "qwen2.5:7b", key = "cle-du-modele", timeoutSeconds = timeout, maxResponseKb = maxKb };

    private LanguageModelClient Client() => container.Services.GetRequiredService<LanguageModelClient>();

    private static LanguageModelTask Job(string input = "CI srv-paie-01.exemple.local (10.1.2.3) : qui en est propriétaire ?",
        Func<JsonElement, string?>? check = null) =>
        new("assisted-cmdb", "proprietaire", "v1", "Propose le propriétaire du CI.", input, Schema, check);

    private void Clear()
    {
        model.Traces.Clear();
        model.Requests.Clear();
        model.Replies.Clear();
        model.RefuseResponseFormat = false;
    }

    [Fact]
    public async Task OPT_21_OPT_24_requete_au_format_commun_pseudonymisee_et_sortie_retablie()
    {
        Clear();
        model.Settings = Settings();
        model.Replies.Enqueue(_ => Task.FromResult(
            FakeModelServer.Content("```json\n{ \"proprietaire\": \"Équipe paie\", \"motif\": \"HOTE_1 (IP_1) héberge la paie.\" }\n```")));
        var pseudo = new Pseudonymizer();
        var result = await Client().RunAsync(Job(), pseudo, CancellationToken.None);

        Assert.True(result.Valid, result.Call.Error);
        Assert.Equal("srv-paie-01.exemple.local (10.1.2.3) héberge la paie.", result.Output!.Value.GetProperty("motif").GetString());
        var sent = Assert.Single(model.Requests);
        Assert.Equal("Bearer cle-du-modele", model.Authorizations.Last());
        Assert.Equal(("qwen2.5:7b", 0, "json_schema"), (sent.GetProperty("model").GetString(), sent.GetProperty("temperature").GetInt32(),
            sent.GetProperty("response_format").GetProperty("type").GetString()));
        var raw = sent.GetRawText();
        Assert.DoesNotContain("srv-paie-01", raw);
        Assert.DoesNotContain("10.1.2.3", raw);
        var user = sent.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Equal("CI HOTE_1 (IP_1) : qui en est propriétaire ?", user);
        // OPT-27 : la trace porte l'empreinte de l'entrée pseudonymisée, jamais l'entrée.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(user))), result.Call.InputHash);
        Assert.Equal(("assisted-cmdb", "proprietaire", "qwen2.5:7b", LanguageModelSettingsData.InSi, "v1", 1, 0),
            (result.Call.Feature, result.Call.Task, result.Call.Model, result.Call.Location, result.Call.PromptVersion, result.Call.Attempts, result.Call.Rejected));
    }

    [Fact]
    public async Task OPT_21_un_serveur_sans_response_format_recoit_le_schema_par_les_consignes()
    {
        Clear();
        model.Settings = Settings();
        model.RefuseResponseFormat = true;
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "Équipe paie", "motif": "Nom du CI." }""")));
        var result = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None);
        Assert.True(result.Valid, result.Call.Error);
        var last = model.Requests.Last();
        Assert.False(last.TryGetProperty("response_format", out _));
        Assert.Contains("\"proprietaire\"", last.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OPT_25_une_reponse_non_conforme_est_ecartee_et_reessayee_une_fois()
    {
        Clear();
        model.Settings = Settings();
        // Non JSON, puis conforme : valide au second essai, une réponse écartée.
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("Le propriétaire est l'équipe paie.")));
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "Équipe paie", "motif": "HOTE_1" }""")));
        var retried = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None);
        Assert.Equal((LanguageModelClient.Valid, 2, 1), (retried.Call.Outcome, retried.Call.Attempts, retried.Call.Rejected));

        // Jeton inventé, puis champ inattendu : écartée deux fois, non conforme, aucune sortie.
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "COMPTE_4", "motif": "IP_9" }""")));
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "x", "motif": "y", "commande": "supprimer" }""")));
        var refused = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None);
        Assert.Equal((LanguageModelClient.NonConforming, 2, 2), (refused.Call.Outcome, refused.Call.Attempts, refused.Call.Rejected));
        Assert.Null(refused.Output);
        Assert.Contains("inattendue", refused.Call.Error);

        // Une référence que la tâche ne connaît pas est écartée comme un jeton inconnu.
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "Équipe inconnue", "motif": "-" }""")));
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "Équipe paie", "motif": "-" }""")));
        var checkedRef = await Client().RunAsync(Job(check: o => o.GetProperty("proprietaire").GetString() == "Équipe paie" ? null : "Groupe inconnu."),
            new Pseudonymizer(), CancellationToken.None);
        Assert.Equal((LanguageModelClient.Valid, 1), (checkedRef.Call.Outcome, checkedRef.Call.Rejected));
    }

    [Fact]
    public async Task OPT_28_panne_delai_et_taille_rendent_une_issue_motivee_sans_lever()
    {
        Clear();
        model.Settings = Settings();
        model.Replies.Enqueue(_ => Task.FromResult(Results.StatusCode(500)));
        var down = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None);
        Assert.Equal(LanguageModelClient.Error, down.Call.Outcome);
        Assert.Contains("HTTP 500", down.Call.Error);
        Assert.DoesNotContain("cle-du-modele", down.Call.Error);

        // Délai d'une requête dépassé : « délai », sans second essai.
        model.Replies.Enqueue(async _ => { await Task.Delay(5000); return FakeModelServer.Content("{}"); });
        var slow = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None,
            JsonSerializer.Deserialize<LanguageModelSettingsData>(JsonSerializer.Serialize(Settings(timeout: 1)), JsonSerializerOptions.Web));
        Assert.Equal((LanguageModelClient.Timeout, 1), (slow.Call.Outcome, slow.Call.Attempts));

        // Réponse plus grande que la taille maximale : écartée, deux fois.
        var big = FakeModelServer.Content($$"""{ "proprietaire": "{{new string('x', 3000)}}", "motif": "-" }""");
        model.Replies.Enqueue(_ => Task.FromResult(big));
        model.Replies.Enqueue(_ => Task.FromResult(big));
        var large = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None,
            JsonSerializer.Deserialize<LanguageModelSettingsData>(JsonSerializer.Serialize(Settings(maxKb: 1)), JsonSerializerOptions.Web));
        Assert.Equal((LanguageModelClient.NonConforming, 2), (large.Call.Outcome, large.Call.Rejected));
        Assert.Contains("1 Ko", large.Call.Error);
    }

    [Fact]
    public async Task OPT_28_une_requete_au_modele_a_la_fois()
    {
        Clear();
        model.Settings = Settings();
        // Compté par les seules réponses de ce test : une requête lente d'un autre test peut encore courir côté serveur.
        int inFlight = 0, max = 0;
        for (var i = 0; i < 3; i++)
            model.Replies.Enqueue(async _ =>
            {
                var now = Interlocked.Increment(ref inFlight);
                lock (model) max = Math.Max(max, now);
                await Task.Delay(200);
                Interlocked.Decrement(ref inFlight);
                return FakeModelServer.Content("""{ "proprietaire": "a", "motif": "b" }""");
            });
        var client = Client();
        var all = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.RunAsync(Job(), new Pseudonymizer(), CancellationToken.None)));
        Assert.All(all, r => Assert.True(r.Valid, r.Call.Error));
        Assert.Equal(1, max);
    }

    [Fact]
    public async Task OPT_22_OPT_23_emplacement_verifie_a_l_appel_et_reglages_relus_a_chaque_appel()
    {
        Clear();
        // Adresse publique déclarée « dans le SI » (réglage d'avant un changement de résolution) : aucun appel.
        model.Settings = Settings(url: "http://8.8.8.8:9/v1");
        var outside = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None);
        Assert.Equal(LanguageModelClient.Error, outside.Call.Outcome);
        Assert.Contains("hors du SI", outside.Call.Error);
        Assert.Empty(model.Requests);

        // Fonctionnalité coupée (403) ou modèle désactivé : rien n'est envoyé ; l'essai passe ses réglages, même désactivés.
        model.Settings = null;
        Assert.Contains("illisibles", (await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None)).Call.Error);
        model.Settings = Settings(enabled: false);
        Assert.Contains("désactivé", (await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None)).Call.Error);
        Assert.Empty(model.Requests);
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "a", "motif": "b" }""")));
        var essay = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None,
            JsonSerializer.Deserialize<LanguageModelSettingsData>(JsonSerializer.Serialize(Settings(enabled: false)), JsonSerializerOptions.Web));
        Assert.True(essay.Valid, essay.Call.Error);
    }

    [Fact]
    public async Task OPT_27_chaque_appel_d_une_fonctionnalite_remet_sa_trace_sans_l_invite_ni_la_reponse()
    {
        Clear();
        model.Settings = Settings();
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "Équipe paie", "motif": "HOTE_1 en IP_1" }""")));
        var result = await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None);
        Assert.True(result.Valid, result.Call.Error);
        var trace = Assert.Single(model.Traces);
        Assert.Equal(("assisted-cmdb", "proprietaire", "qwen2.5:7b", "Dans le SI", "v1", "valide", result.Call.InputHash),
            (trace.GetProperty("feature").GetString(), trace.GetProperty("task").GetString(), trace.GetProperty("model").GetString(),
             trace.GetProperty("location").GetString(), trace.GetProperty("promptVersion").GetString(), trace.GetProperty("outcome").GetString(),
             trace.GetProperty("inputHash").GetString()));
        var raw = trace.GetRawText();
        foreach (var clear in new[] { "srv-paie-01", "10.1.2.3", "HOTE_1", "Équipe paie", "propriétaire ?" }) Assert.DoesNotContain(clear, raw);

        // L'essai ne remet pas de trace : l'API la garde elle-même.
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "proprietaire": "a", "motif": "b" }""")));
        await Client().RunAsync(Job(), new Pseudonymizer(), CancellationToken.None,
            JsonSerializer.Deserialize<LanguageModelSettingsData>(JsonSerializer.Serialize(Settings()), JsonSerializerOptions.Web));
        Assert.Single(model.Traces);
    }

    [Fact]
    public async Task OPT_26_l_essai_passe_par_le_chemin_d_une_tache_et_rend_sa_trace()
    {
        Clear();
        model.Settings = null;   // CMDB assistée coupée : l'essai ne lit rien à l'API, il reçoit ses réglages.
        var http = container.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/language-model/test", Settings(enabled: false))).StatusCode);
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", FakeInternalApi.Token);

        // Le modèle recopie le jeton du nom d'hôte : valide ; le nom d'hôte n'est jamais parti.
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "etat": "pret", "hote": "HOTE_1" }""")));
        var ok = JsonDocument.Parse(await (await http.PostAsJsonAsync("/language-model/test", Settings(enabled: false))).Content.ReadAsStringAsync())
            .RootElement.GetProperty("call");
        Assert.Equal(("language-model", "essai", "valide"), (ok.GetProperty("feature").GetString(), ok.GetProperty("task").GetString(), ok.GetProperty("outcome").GetString()));
        Assert.DoesNotContain(LanguageModelEssay.Host, model.Requests.Last().GetRawText());
        Assert.Empty(model.Traces);

        // Un modèle qui ne recopie pas le jeton échoue à l'essai, motif donné.
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "etat": "pret", "hote": "serveur" }""")));
        model.Replies.Enqueue(_ => Task.FromResult(FakeModelServer.Content("""{ "etat": "pret", "hote": "serveur" }""")));
        var bad = JsonDocument.Parse(await (await http.PostAsJsonAsync("/language-model/test", Settings())).Content.ReadAsStringAsync())
            .RootElement.GetProperty("call");
        Assert.Equal("non conforme", bad.GetProperty("outcome").GetString());
        Assert.Contains("jeton du nom d'hôte", bad.GetProperty("error").GetString());
    }
}
