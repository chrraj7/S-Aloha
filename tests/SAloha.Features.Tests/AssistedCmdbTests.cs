using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using SAloha.Features.AssistedCmdb;
using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.Tests;

/// <summary>CMA-07, CMA-09 : l'entrée d'un lot et la lecture des propositions, en fonctions pures.</summary>
public class CmdbTasksTests
{
    private static CiRecord Ci(int id, string title, string type = "Serveur", string? os = null, string? description = null, string? ip = null,
        string? serial = null, int perimeter = 7) =>
        new(id, perimeter, $"CI-{id}", title, description, "En service", type, "Production", os, null, null, serial, ip, null, "hery.rakoto",
            [new CiRelationRead(id, 3, "Héberge")], []);

    [Fact]
    public void CMA_07_seuls_les_champs_utiles_partent_et_les_valeurs_sensibles_sont_masquees()
    {
        var pseudo = new Pseudonymizer();
        var ci = Ci(1, "srvpaie1", os: "Win2019 std", description: "Paie, joignable en 10.1.2.3, série SN-7781-AX. " + new string('x', 1200),
            ip: "10.1.2.3", serial: "SN-7781-AX");
        var input = pseudo.Apply(CmdbTasks.Input([ci], new Dictionary<int, string> { [1] = "srvpaie1", [3] = "base-paie" }, pseudo));
        foreach (var secret in new[] { "10.1.2.3", "SN-7781-AX", "hery.rakoto" }) Assert.DoesNotContain(secret, input);
        var item = JsonDocument.Parse(input).RootElement[0];
        Assert.Equal(["id", "nom", "type", "environnement", "systeme", "constructeur", "modele", "description", "relations"],
            item.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Héberge base-paie", item.GetProperty("relations")[0].GetString());
        Assert.True(item.GetProperty("description").GetString()!.Length <= 1000);
    }

    [Fact]
    public void CMA_09_une_proposition_hors_du_lot_vide_ou_sans_convention_est_ecartee_et_comptee()
    {
        var batch = new[] { Ci(1, "srvpaie1", os: "Win2019 std"), Ci(2, "base-paie", type: "Base de données") }.ToDictionary(c => c.Id);
        var normalize = JsonDocument.Parse("""
            { "propositions": [
              { "ci": 1, "champ": "operatingSystem", "valeur": "Windows Server 2019 Standard", "motif": "Écriture normalisée." },
              { "ci": 1, "champ": "operatingSystem", "valeur": "Windows 2019", "motif": "Second avis : ignoré." },
              { "ci": 2, "champ": "vendor", "valeur": "  ", "motif": "Vide : écarté." },
              { "ci": 99, "champ": "model", "valeur": "DL380", "motif": "Hors du lot : écarté." } ] }
            """).RootElement;
        var (kept, rejected) = CmdbTasks.Read(CmdbTasks.Normalize, normalize, batch, new HashSet<string>());
        Assert.Equal([new CmdbProposal(1, "operatingSystem", "Windows Server 2019 Standard", "Écriture normalisée.")], kept);
        Assert.Equal(2, rejected);

        // Renommer un type sans convention : écarté ; garder le même nom : ignoré.
        var rename = JsonDocument.Parse("""
            { "propositions": [ { "ci": 1, "nom": "SRV-PAIE-01", "motif": "Convention des serveurs." },
              { "ci": 2, "nom": "BDD-PAIE-01", "motif": "Pas de convention : écarté." }, { "ci": 1, "nom": "srvpaie1", "motif": "Inchangé." } ] }
            """).RootElement;
        (kept, rejected) = CmdbTasks.Read(CmdbTasks.Rename, rename, batch, new HashSet<string> { "Serveur" });
        Assert.Equal([("title", "SRV-PAIE-01")], kept.Select(k => (k.Field, k.Value)));
        Assert.Equal(1, rejected);
        // Le schéma borne les types et les champs (OPT-25) : un type hors liste n'atteint jamais la lecture.
        Assert.NotNull(SchemaCheck.Error(JsonDocument.Parse("""{ "propositions": [ { "ci": 1, "type": "Imprimante", "motif": "-" } ] }""").RootElement,
            CmdbTasks.ReclassifySchema));
    }
}

/// <summary>CMA-06, CMA-09 : présélection des doublons candidats et lecture des fusions, en fonctions pures.</summary>
public class DuplicatesTests
{
    private static CiRecord Ci(int id, string title, string type = "Serveur", string environment = "Production") =>
        new(id, 7, $"CI-{id}", title, null, "En service", type, environment, null, null, null, null, null, null, null, [], []);

    [Fact]
    public void CMA_06_noms_proches_une_fois_normalises_mais_pas_deux_numeros_differents()
    {
        Assert.True(Duplicates.Closeness("srvpaie1", "SRV-PAIE-01") >= Duplicates.MinSimilarity);   // casse, tirets, zéro de tête
        Assert.Equal(0.6, Duplicates.Closeness("Serveur paie prod", "SRV-PAIE-01"));                // mot distinctif partagé
        Assert.Equal(0, Duplicates.Closeness("SRV-PAIE-01", "SRV-PAIE-02"));                        // deux serveurs numérotés
        Assert.Equal(0, Duplicates.Closeness("SRV-MAIL-01", "SRV-PAIE-01"));                        // seul le gabarit se ressemble
        Assert.Equal(0, Duplicates.Closeness("Serveur prod", "Serveur recette"));                   // mots communs seulement
        Assert.Equal("serveur paie prod", Duplicates.Normalize("  Serveur PAIE — prod !"));
    }

    [Fact]
    public void CMA_06_paires_du_meme_environnement_aux_types_compatibles_sans_les_paires_rejetees()
    {
        var items = new[]
        {
            Ci(1, "srvpaie1"), Ci(2, "SRV-PAIE-01"), Ci(3, "Serveur paie prod", "Autre"), Ci(4, "SRV-PAIE-01", environment: "Recette"),
            Ci(5, "SRV-PAIE-01", "Base de données"), Ci(6, "Gestion RH", "Application"), Ci(7, "gestion-rh", "Logiciel"),
        };
        var pairs = Duplicates.Candidates(items, new HashSet<(int, int)> { (2, 3) });
        // 6-7 (même nom, Application et Logiciel), 1-2 (proches), 1-3 et 3-5 (« paie », Autre compatible avec tout type) ; 2-3 rejetée ;
        // 4 en Recette, 5 d'un type incompatible avec les serveurs.
        Assert.Equal([(6, 7), (1, 2), (1, 3), (3, 5)], pairs.Select(p => (p.A, p.B)));
        Assert.True(pairs[0].Score > pairs[^1].Score);
    }

    [Fact]
    public void CMA_07_CMA_09_les_paires_partent_pseudonymisees_et_une_fusion_hors_des_paires_soumises_est_ecartee()
    {
        var a = Ci(1, "srvpaie1") with { IpAddress = "10.1.2.3", SerialNumber = "SN-7781-AX", Description = "Paie, en 10.1.2.3, série SN-7781-AX." };
        var b = Ci(2, "SRV-PAIE-01");
        var pseudo = new Pseudonymizer();
        var input = pseudo.Apply(Duplicates.Input([new CandidatePair(1, 2, 0.9)], new[] { a, b }.ToDictionary(c => c.Id),
            new Dictionary<int, string> { [1] = "srvpaie1", [2] = "SRV-PAIE-01" }, pseudo));
        foreach (var secret in new[] { "10.1.2.3", "SN-7781-AX" }) Assert.DoesNotContain(secret, input);
        var pair = JsonDocument.Parse(input).RootElement[0];
        Assert.Equal((1, 2), (pair.GetProperty("a").GetProperty("id").GetInt32(), pair.GetProperty("b").GetProperty("id").GetInt32()));

        var output = JsonDocument.Parse("""
            { "propositions": [
              { "conserver": 2, "doublon": 1, "motif": "Même serveur, nom selon la convention." },
              { "conserver": 1, "doublon": 2, "motif": "Second avis sur la même paire : ignoré." },
              { "conserver": 2, "doublon": 3, "motif": "Paire non soumise : écartée." },
              { "conserver": 1, "doublon": 1, "motif": "Avec lui-même : écartée." } ] }
            """).RootElement;
        var (kept, rejected) = Duplicates.Read(output, [new CandidatePair(1, 2, 0.9)]);
        Assert.Equal([new MergeProposal(1, 2, "Même serveur, nom selon la convention.")], kept);
        Assert.Equal(2, rejected);
        Assert.Null(SchemaCheck.Error(output, Duplicates.Schema));
    }
}

/// <summary>CMA-02, CMA-09 : Relier (CI cités par les descriptions) et Criticité (applications sans criticité), en fonctions pures.</summary>
public class RelationsTests
{
    private static CiRecord Ci(int id, string title, string type = "Serveur", string? description = null, string? criticality = null,
        List<CiRelationRead>? outgoing = null) =>
        new(id, 7, $"CI-2026-00{id}", title, description, "En service", type, "Production", null, null, null, null, null, null, null, outgoing ?? [], [],
            criticality);

    [Fact]
    public void CMA_02_seuls_les_CI_dont_la_description_cite_un_autre_CI_de_l_analyse_sont_soumis_avec_ces_cibles()
    {
        var items = new[]
        {
            Ci(11, "APP-PAIE", "Application", "Lit ses données dans BDD-PAIE ; tourne sur la VM CI-2026-0013."),
            Ci(12, "BDD-PAIE", "Base de données"),
            Ci(13, "VM-01"),
            Ci(14, "SRV-WEB", description: "Serveur web public, sans dépendance."),
            Ci(15, "RH", "Application", "Interface avec la paie RH."),   // « RH » : nom trop court pour être reconnu
        };
        var subjects = Relations.Subjects(items);
        var subject = Assert.Single(subjects);
        Assert.Equal(11, subject.Source.Id);
        Assert.Equal([12, 13], subject.Targets.Select(t => t.Id));
    }

    [Fact]
    public void CMA_09_une_relation_hors_analyse_vers_soi_ou_existante_est_ecartee_et_une_criticite_ne_vise_qu_une_application_sans_criticite()
    {
        var app = Ci(11, "APP-PAIE", "Application", "Lit BDD-PAIE.", outgoing: [new CiRelationRead(11, 13, "Dépend de")]);
        var subjects = new[] { new RelationSubject(app, [Ci(12, "BDD-PAIE", "Base de données")]) };
        var output = JsonDocument.Parse("""
            { "propositions": [
              { "source": 11, "cible": 12, "relation": "Dépend de", "motif": "La description cite la base." },
              { "source": 11, "cible": 12, "relation": "Dépend de", "motif": "Deux fois : ignorée." },
              { "source": 11, "cible": 11, "relation": "Dépend de", "motif": "Vers soi : écartée." },
              { "source": 11, "cible": 13, "relation": "Dépend de", "motif": "Existe déjà : écartée." },
              { "source": 11, "cible": 99, "relation": "Héberge", "motif": "Hors de l'analyse : écartée." },
              { "source": 12, "cible": 11, "relation": "Héberge", "motif": "Source non soumise : écartée." } ] }
            """).RootElement;
        var (kept, rejected) = Relations.Read(output, subjects, new HashSet<int> { 11, 12, 13 });
        Assert.Equal([new RelationProposal(11, 12, "Dépend de", "La description cite la base.")], kept);
        Assert.Equal(4, rejected);
        Assert.NotNull(SchemaCheck.Error(JsonDocument.Parse("""{ "propositions": [ { "source": 11, "cible": 12, "relation": "Utilise", "motif": "-" } ] }""").RootElement,
            Relations.Schema));

        var batch = new[] { Ci(21, "Paie", "Application"), Ci(22, "Messagerie", "Application", criticality: "Majeure"), Ci(23, "srv1") }.ToDictionary(c => c.Id);
        var criticality = JsonDocument.Parse("""
            { "propositions": [ { "ci": 21, "criticite": "Vitale", "motif": "La paie arrête l'activité." },
              { "ci": 22, "criticite": "Vitale", "motif": "A déjà sa criticité : écartée." },
              { "ci": 23, "criticite": "Mineure", "motif": "Pas une application : écartée." } ] }
            """).RootElement;
        var (critical, refused) = CmdbTasks.Read(CmdbTasks.Criticality, criticality, batch, new HashSet<string>());
        Assert.Equal([new CmdbProposal(21, "criticality", "Vitale", "La paie arrête l'activité.")], critical);
        Assert.Equal(2, refused);
    }
}

/// <summary>CMA-08 : aide au sas, candidats présélectionnés et lecture des réponses, en fonctions pures.</summary>
public class StagingTests
{
    private static CiRecord Ci(int id, string title, string type = "Serveur", string status = "En service", int perimeter = 7) =>
        new(id, perimeter, $"CI-{id}", title, null, status, type, "Production", null, null, null, null, null, null, null, [], []);

    private static StagingAsset Asset(int id, string host, int? matched = null, string type = "Serveur") =>
        new(id, 7, "À rapprocher", host, "10.9.0.4", "00:11:22:33:44:55", "SN-77-AX", null, null, "Linux", type, "22,443", "IpSweep", false, matched);

    [Fact]
    public void CMA_08_candidats_serveurs_ou_equipements_du_perimetre_aux_noms_proches_et_le_CI_en_conflit()
    {
        var items = new[]
        {
            Ci(1, "SRV-PAIE-01"), Ci(2, "srvpaie1", "Application"), Ci(3, "SRV-PAIE-01-old", status: "Retiré"), Ci(4, "srvpaie1", perimeter: 8),
            Ci(5, "SRV-MAIL-01"), Ci(6, "NAS-01", "Stockage"),
        };
        var subjects = Staging.Subjects([Asset(10, "srvpaie1.siege.local"), Asset(11, "inconnu-77", matched: 6)], items);
        Assert.Equal([1], subjects[0].Candidates.Select(c => c.Id));   // ni l'application, ni le retiré, ni l'autre périmètre
        Assert.Equal([6], subjects[1].Candidates.Select(c => c.Id));   // le CI que CFG-41 a trouvé en conflit
    }

    [Fact]
    public void CMA_07_CMA_09_actif_pseudonymise_et_reponse_hors_candidats_ecartee()
    {
        var subjects = Staging.Subjects([Asset(10, "srvpaie1.siege.local"), Asset(11, "imprimante-3")], [Ci(1, "srvpaie1")]);
        var pseudo = new Pseudonymizer();
        var input = pseudo.Apply(Staging.Input(subjects, pseudo));
        foreach (var secret in new[] { "srvpaie1", "10.9.0.4", "00:11:22:33:44:55", "SN-77-AX" }) Assert.DoesNotContain(secret, input);
        // Le CI qui porte le nom d'hôte reçoit le même jeton que l'actif.
        var first = JsonDocument.Parse(input).RootElement[0];
        Assert.Equal(first.GetProperty("candidats")[0].GetProperty("nom").GetString(), first.GetProperty("nom").GetString());
        Assert.StartsWith("HOTE", first.GetProperty("nom").GetString());

        var output = JsonDocument.Parse("""
            { "propositions": [
              { "actif": 10, "ci": 1, "type": "Serveur", "motif": "Même nom d'hôte." },
              { "actif": 10, "ci": 0, "type": "Réseau", "motif": "Second avis : ignoré." },
              { "actif": 11, "ci": 0, "type": "Poste de travail", "motif": "Ports d'impression." },
              { "actif": 11, "ci": 1, "type": "Serveur", "motif": "Pas son candidat : écarté." },
              { "actif": 99, "ci": 0, "type": "Autre", "motif": "Hors de la requête : écarté." } ] }
            """).RootElement;
        var (kept, rejected) = Staging.Read(output, subjects);
        Assert.Equal([new StagingProposal(10, "matchedCiId", "1", "Même nom d'hôte."), new StagingProposal(11, "ciType", "Poste de travail", "Ports d'impression.")], kept);
        Assert.Equal(2, rejected);
        Assert.Null(SchemaCheck.Error(output, Staging.Schema));
    }
}

/// <summary>Port interne factice de l'API et modèle factice : une analyse, ses CI, les conventions, les propositions et les comptes rendus.</summary>
public sealed class FakeCmdbApi : IAsyncLifetime
{
    private WebApplication app = null!;
    public string Url { get; private set; } = "";
    public readonly ConcurrentQueue<object> Events = new();
    public readonly ConcurrentDictionary<int, object> Analyses = new();
    public readonly ConcurrentQueue<(int Id, JsonElement Report)> Reports = new();
    public readonly ConcurrentQueue<JsonElement> Proposals = new();
    public readonly ConcurrentQueue<JsonElement> ModelRequests = new();
    public volatile object[] Items = [];
    public volatile object[] Naming = [];
    /// <summary>CMA-06 : paires rejetées servies au conteneur.</summary>
    public volatile int[][] Declined = [];
    /// <summary>CMA-08 : la tâche de découverte et ses actifs laissés au sas.</summary>
    public volatile object? StagingRead;
    /// <summary>CMA-12 : réglages de la CMDB assistée ; absents : 404, le conteneur garde ceux par défaut.</summary>
    public volatile object? CmdbSettings;
    /// <summary>Réponse du modèle selon la tâche (nom du schéma demandé) ; absente : panne (500).</summary>
    public readonly ConcurrentDictionary<string, string> Replies = new();
    /// <summary>CMA-11 : un vrai modèle (adresse terminée par /v1, nom, délai) à la place du faux ; null : le faux.</summary>
    public volatile string? ModelUrl;
    public volatile string ModelName = "qwen2.5:7b";
    public volatile int ModelTimeoutSeconds = 30;
    /// <summary>Traces des appels au modèle remises par le conteneur (OPT-27).</summary>
    public readonly ConcurrentQueue<JsonElement> Calls = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.MapGet("/internal/events", (long after) =>
        {
            var list = Events.Select(e => JsonSerializer.SerializeToElement(e, JsonSerializerOptions.Web)).Where(e => e.GetProperty("id").GetInt64() > after).ToList();
            return Results.Json(new { events = list, next = list.Count > 0 ? list[^1].GetProperty("id").GetInt64() : after, gap = false });
        });
        app.MapGet("/internal/assisted-cmdb/analyses/{id:int}", (int id) => Analyses.TryGetValue(id, out var a) ? Results.Json(a) : Results.NotFound());
        app.MapPut("/internal/assisted-cmdb/analyses/{id:int}", async (int id, HttpContext c) =>
        {
            Reports.Enqueue((id, await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body)));
            return Results.Json(new { id });
        });
        app.MapGet("/internal/configuration-items", (int? perimeter, int? after, string? ids) =>
        {
            var all = Items.Select(i => JsonSerializer.SerializeToElement(i, JsonSerializerOptions.Web)).ToList();
            if (ids is not null)
            {
                var wanted = ids.Split(',').Select(int.Parse).ToHashSet();
                return Results.Json(all.Where(i => wanted.Contains(i.GetProperty("id").GetInt32())));
            }
            return Results.Json(all.Where(i => i.GetProperty("id").GetInt32() > (after ?? 0)));
        });
        app.MapGet("/internal/assisted-cmdb/naming", () => Results.Json(Naming));
        app.MapGet("/internal/assisted-cmdb/declined-pairs", (int perimeter) => Results.Json(Declined));
        app.MapGet("/internal/discovery/staging", (int job) => StagingRead is { } read ? Results.Json(read) : Results.NotFound());
        app.MapGet("/internal/features/assisted-cmdb/settings", () => CmdbSettings is { } settings ? Results.Json(settings) : Results.NotFound());
        app.MapPost("/internal/proposals", async (HttpContext c) =>
        {
            Proposals.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(new { id = Proposals.Count, created = true }, statusCode: 201);
        });
        app.MapGet("/internal/language-model", () => Results.Json(new
        {
            enabled = true, location = "Dans le SI", url = ModelUrl ?? Url + "/v1", model = ModelName, key = (string?)null,
            timeoutSeconds = ModelTimeoutSeconds, maxResponseKb = 64,
        }));
        app.MapPost("/internal/language-model/calls", async (HttpContext c) =>
        {
            Calls.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body));
            return Results.Json(new { id = Calls.Count }, statusCode: 201);
        });
        app.MapPost("/v1/chat/completions", async (HttpContext c) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body);
            ModelRequests.Enqueue(body);
            var task = body.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString()!;
            return Replies.TryGetValue(task, out var content)
                ? Results.Json(new { choices = new[] { new { message = new { role = "assistant", content } } } })
                : Results.StatusCode(500);
        });
        await app.StartAsync();
        Url = app.Urls.Single();
    }

    public async Task DisposeAsync() => await app.DisposeAsync();
}

/// <summary>CMDB assistée de bout en bout dans le conteneur : une analyse, Renommer, Normaliser, Reclasser (CMA-01, CMA-02, CMA-07, CMA-09).</summary>
public class AssistedCmdbTests(FakeCmdbApi api) : IClassFixture<FakeCmdbApi>
{
    private static long eventId;

    private WebApplicationFactory<Program> Container() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Features:Token", FakeInternalApi.Token);
        b.UseSetting("Api:Url", api.Url);
        b.UseSetting("Events:PollMilliseconds", "50");
        b.UseSetting("Events:IdleMilliseconds", "100");
    });

    private static object Item(int id, string title, string type, string? os = null, string? description = null, string? ip = null, string? serial = null,
        int perimeter = 7) => new
    {
        id, perimeterId = perimeter, reference = $"CI-{id}", title, description, status = "En service", ciType = type, environment = "Production",
        operatingSystem = os, vendor = (string?)null, model = (string?)null, serialNumber = serial, ipAddress = ip, macAddress = (string?)null,
        ownerId = "hery.rakoto", outgoing = Array.Empty<object>(), incoming = Array.Empty<object>(),
    };

    private void Analysis(int id, string[] tasks, string status = "En file", int perimeter = 7)
    {
        api.Analyses[id] = new { id, perimeterId = perimeter, tasks, configurationItemIds = (int[]?)null, status, itemCount = 3 };
        api.Events.Enqueue(new { id = Interlocked.Increment(ref eventId), type = "analysis.requested", entityType = "cmdb-analysis", entityId = id,
            perimeterId = perimeter, at = DateTime.UtcNow });
    }

    private async Task<JsonElement?> FinalReport(int id)
    {
        for (var i = 0; i < 200; i++)
        {
            var last = api.Reports.Where(r => r.Id == id).Select(r => r.Report).LastOrDefault();
            if (last.ValueKind == JsonValueKind.Object && last.GetProperty("status").GetString() is "Terminée" or "En échec") return last;
            await Task.Delay(50);
        }
        return null;
    }

    [Fact]
    public async Task CMA_02_CMA_07_CMA_09_une_analyse_propose_renommer_normaliser_reclasser_sans_rien_envoyer_de_sensible()
    {
        api.Items =
        [
            Item(1, "srvpaie1", "Serveur", os: "Win2019 std", description: "Paie, joignable en 10.1.2.3, série SN-7781-AX.", ip: "10.1.2.3", serial: "SN-7781-AX"),
            Item(2, "Serveur paie prod", "Autre", os: "Windows Server 2019 Standard"),
            Item(3, "base-paie", "Base de données"),
            Item(4, "srv-usine-07", "Serveur", perimeter: 8),   // d'un autre périmètre : jamais envoyé (CMA-07)
        ];
        api.Naming = [new { ciType = "Serveur", description = "SRV-<RÔLE>-<NN>, en majuscules", examples = new[] { "SRV-PAIE-01", "SRV-MAIL-02" } }];
        api.Replies["rename"] = """{ "propositions": [ { "ci": 1, "nom": "SRV-PAIE-01", "motif": "Convention des serveurs." }, { "ci": 3, "nom": "BDD-PAIE", "motif": "Hors du lot." } ] }""";
        api.Replies["normalize"] = """
            { "propositions": [ { "ci": 1, "champ": "operatingSystem", "valeur": "Windows Server 2019 Standard", "motif": "Écriture normalisée." },
              { "ci": 2, "champ": "operatingSystem", "valeur": "Windows Server 2019 Standard", "motif": "Déjà ainsi : ignoré." },
              { "ci": 99, "champ": "model", "valeur": "DL380", "motif": "Inconnu." } ] }
            """;
        api.Replies["reclassify"] = """{ "propositions": [ { "ci": 2, "type": "Serveur", "motif": "Un serveur, d'après son nom et son système." } ] }""";
        Analysis(501, ["rename", "normalize", "reclassify"]);

        using var container = Container();
        container.CreateClient();
        var report = await FinalReport(501);
        Assert.NotNull(report);
        Assert.Equal(("Terminée", 3, 2), (report!.Value.GetProperty("status").GetString(), report.Value.GetProperty("proposalCount").GetInt32(),
            report.Value.GetProperty("rejectedCount").GetInt32()));
        Assert.Equal("En cours", api.Reports.First(r => r.Id == 501).Report.GetProperty("status").GetString());

        var proposals = api.Proposals.Where(p => p.GetProperty("feature").GetString() == "assisted-cmdb")
            .Select(p => (p.GetProperty("targetType").GetString(), p.GetProperty("targetId").GetInt32(), p.GetProperty("field").GetString(), p.GetProperty("after").GetString()))
            .ToList();
        Assert.Equal([("ci", 1, "title", "SRV-PAIE-01"), ("ci", 1, "operatingSystem", "Windows Server 2019 Standard"), ("ci", 2, "ciType", "Serveur")], proposals);

        // CMA-07 : ni adresse ni numéro de série en clair, aucun CI d'un autre périmètre ; Renommer ne voit que les types qui ont une convention.
        var requests = api.ModelRequests.Select(r => (Task: r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString(),
            User: r.GetProperty("messages")[1].GetProperty("content").GetString()!)).ToList();
        Assert.Equal(["rename", "normalize", "reclassify"], requests.Select(r => r.Task));
        foreach (var (_, user) in requests)
            foreach (var secret in new[] { "10.1.2.3", "SN-7781-AX", "hery.rakoto", "srv-usine-07" }) Assert.DoesNotContain(secret, user);
        Assert.DoesNotContain("base-paie", requests[0].User);
        Assert.Contains("SRV-PAIE-01", api.ModelRequests.First().GetProperty("messages")[0].GetProperty("content").GetString());   // la convention
    }

    [Fact]
    public async Task CMA_06_Fusionner_ne_soumet_que_les_paires_candidates_jamais_une_paire_rejetee_et_depose_la_fusion()
    {
        api.Items =
        [
            Item(21, "srvpaie1", "Serveur", ip: "10.1.2.3"),
            Item(22, "SRV-PAIE-01", "Serveur"),
            Item(23, "Serveur paie prod", "Serveur"),
            Item(24, "SRV-MAIL-01", "Serveur"),   // sans paire candidate : jamais envoyé
        ];
        api.Declined = [[22, 23]];
        api.Replies["merge"] = """
            { "propositions": [ { "conserver": 22, "doublon": 21, "motif": "Même serveur de paie." },
              { "conserver": 22, "doublon": 24, "motif": "Paire non soumise." } ] }
            """;
        Analysis(504, ["merge"]);

        using var container = Container();
        container.CreateClient();
        var report = await FinalReport(504);
        Assert.NotNull(report);
        Assert.Equal(("Terminée", 1, 1), (report!.Value.GetProperty("status").GetString(), report.Value.GetProperty("proposalCount").GetInt32(),
            report.Value.GetProperty("rejectedCount").GetInt32()));
        var merge = Assert.Single(api.Proposals, p => p.GetProperty("field").GetString() == "merge");
        Assert.Equal((21, "22"), (merge.GetProperty("targetId").GetInt32(), merge.GetProperty("after").GetString()));

        // Une seule requête : les paires 21-22 et 21-23 ; ni la paire rejetée 22-23, ni SRV-MAIL-01, ni l'adresse en clair.
        var request = Assert.Single(api.ModelRequests, r => r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString() == "merge");
        var pairs = JsonDocument.Parse(request.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement;
        Assert.Equal([(21, 22), (21, 23)], pairs.EnumerateArray()
            .Select(p => (p.GetProperty("a").GetProperty("id").GetInt32(), p.GetProperty("b").GetProperty("id").GetInt32())));
        foreach (var absent in new[] { "SRV-MAIL-01", "10.1.2.3" }) Assert.DoesNotContain(absent, request.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CMA_02_Relier_et_Criticite_deposent_une_relation_orientee_et_la_criticite_d_une_application()
    {
        var app = Item(31, "APP-PAIE", "Application", description: "Lit ses données dans BDD-PAIE.");
        api.Items = [app, Item(32, "BDD-PAIE", "Base de données"), Item(33, "SRV-WEB", "Serveur", description: "Serveur web, sans dépendance.")];
        api.Replies["relate"] = """{ "propositions": [ { "source": 31, "cible": 32, "relation": "Dépend de", "motif": "La description cite la base." } ] }""";
        api.Replies["criticality"] = """{ "propositions": [ { "ci": 31, "criticite": "Vitale", "motif": "La paie arrête l'activité." } ] }""";
        Analysis(505, ["relate", "criticality"]);

        using var container = Container();
        container.CreateClient();
        var report = await FinalReport(505);
        Assert.NotNull(report);
        Assert.Equal(("Terminée", 2, 0), (report!.Value.GetProperty("status").GetString(), report.Value.GetProperty("proposalCount").GetInt32(),
            report.Value.GetProperty("rejectedCount").GetInt32()));
        var mine = api.Proposals.Where(p => p.GetProperty("targetId").GetInt32() == 31)
            .Select(p => (p.GetProperty("field").GetString(), p.GetProperty("after").GetString())).ToList();
        Assert.Equal([("criticality", "Vitale"), ("relation", "32|Dépend de")], mine);
        // Relier ne soumet que la source qui cite un autre CI ; Criticité, que l'application sans criticité.
        var relate = Assert.Single(api.ModelRequests, r => r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString() == "relate");
        Assert.DoesNotContain("SRV-WEB", relate.GetProperty("messages")[1].GetProperty("content").GetString());
        var critical = Assert.Single(api.ModelRequests, r => r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString() == "criticality");
        Assert.Equal([31], JsonDocument.Parse(critical.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement.EnumerateArray()
            .Select(c => c.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task CMA_08_a_la_fin_d_une_decouverte_les_actifs_laisses_au_sas_recoivent_un_CI_candidat_ou_un_type()
    {
        api.Items = [Item(41, "srvweb1", "Serveur"), Item(42, "Paie", "Application")];
        api.StagingRead = new
        {
            job = new { id = 61, perimeterId = 7, assistedCmdb = true, lastStatus = "Réussie" },
            assets = new object[]
            {
                new { id = 71, perimeterId = 7, status = "À rapprocher", hostname = "srvweb1.siege.local", ipAddress = "10.9.0.7", detectedType = "Serveur", inDirectory = true },
                new { id = 72, perimeterId = 7, status = "À rapprocher", hostname = "imp-rdc", ipAddress = "10.9.0.8", detectedType = "Autre", inDirectory = false },
            },
        };
        api.Replies["staging"] = """
            { "propositions": [ { "actif": 71, "ci": 41, "type": "Serveur", "motif": "Même nom d'hôte et même système." },
              { "actif": 72, "ci": 0, "type": "Poste de travail", "motif": "Ports d'impression ouverts." } ] }
            """;
        api.Events.Enqueue(new { id = Interlocked.Increment(ref eventId), type = "discovery.finished", entityType = "discovery-job", entityId = 61,
            perimeterId = 7, at = DateTime.UtcNow });

        using var container = Container();
        container.CreateClient();
        List<(int, string?, string?)> assets = [];
        for (var i = 0; i < 200 && assets.Count < 2; i++)
        {
            await Task.Delay(50);
            assets = api.Proposals.Where(p => p.GetProperty("targetType").GetString() == "asset")
                .Select(p => (p.GetProperty("targetId").GetInt32(), p.GetProperty("field").GetString(), p.GetProperty("after").GetString())).ToList();
        }
        Assert.Equal([(71, "matchedCiId", "41"), (72, "ciType", "Poste de travail")], assets);
        var request = Assert.Single(api.ModelRequests, r => r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString() == "staging");
        var user = request.GetProperty("messages")[1].GetProperty("content").GetString()!;
        foreach (var secret in new[] { "srvweb1", "10.9.0.7", "Paie" }) Assert.DoesNotContain(secret, user);   // ni l'application, jamais candidate
        api.StagingRead = null;
    }

    [Fact]
    public async Task OPT_28_un_modele_en_panne_met_l_analyse_en_echec_motif_donne_et_une_analyse_finie_ne_se_refait_pas()
    {
        api.Items = [Item(11, "srvmail2", "Serveur", os: "W2k19")];
        api.Replies.Clear();   // le modèle répond 500
        Analysis(502, ["normalize"]);
        Analysis(503, ["normalize"], status: "Terminée");
        using var container = Container();
        container.CreateClient();
        var report = await FinalReport(502);
        Assert.NotNull(report);
        Assert.Equal("En échec", report!.Value.GetProperty("status").GetString());
        Assert.Contains("HTTP 500", report.Value.GetProperty("reason").GetString());
        await Task.Delay(300);
        Assert.DoesNotContain(api.Reports, r => r.Id == 503);
    }
}

/// <summary>CMA-12 dans le conteneur : les réglages de l'Admin relus à chaque analyse (fixture à part : il compte les requêtes au modèle).</summary>
public class AssistedCmdbSettingsTests(FakeCmdbApi api) : IClassFixture<FakeCmdbApi>
{
    [Fact]
    public async Task CMA_12_une_tache_retiree_n_est_pas_traitee_et_les_paires_vont_par_requetes_de_la_taille_reglee()
    {
        object Item(int id, string title) => new
        {
            id, perimeterId = 7, reference = $"CI-{id}", title, description = (string?)null, status = "En service", ciType = "Serveur",
            environment = "Production", operatingSystem = "W2k19", vendor = (string?)null, model = (string?)null, serialNumber = (string?)null,
            ipAddress = (string?)null, macAddress = (string?)null, ownerId = "hery.rakoto", outgoing = Array.Empty<object>(), incoming = Array.Empty<object>(),
        };
        api.Items = [Item(21, "srvpaie1"), Item(22, "SRV-PAIE-01"), Item(23, "Serveur paie prod")];
        api.Declined = [[22, 23]];
        api.Replies["normalize"] = """{ "propositions": [] }""";
        api.Replies["merge"] = """{ "propositions": [ { "conserver": 22, "doublon": 21, "motif": "Même serveur de paie." } ] }""";
        // Normaliser retiré depuis le lancement ; une paire par requête.
        api.CmdbSettings = new { tasks = new[] { "rename", "merge" }, maxItems = 500, pairsPerRequest = 1, stagingHelp = true };
        api.Analyses[601] = new { id = 601, perimeterId = 7, tasks = new[] { "normalize", "merge" }, configurationItemIds = (int[]?)null, status = "En file", itemCount = 3 };
        api.Events.Enqueue(new { id = 1L, type = "analysis.requested", entityType = "cmdb-analysis", entityId = 601, perimeterId = 7, at = DateTime.UtcNow });

        using var container = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Features:Token", FakeInternalApi.Token);
            b.UseSetting("Api:Url", api.Url);
            b.UseSetting("Events:PollMilliseconds", "50");
            b.UseSetting("Events:IdleMilliseconds", "100");
        });
        container.CreateClient();
        JsonElement report = default;
        for (var i = 0; i < 200; i++)
        {
            report = api.Reports.Where(r => r.Id == 601).Select(r => r.Report).LastOrDefault();
            if (report.ValueKind == JsonValueKind.Object && report.GetProperty("status").GetString() is "Terminée" or "En échec") break;
            await Task.Delay(50);
        }
        Assert.Equal("Terminée", report.GetProperty("status").GetString());
        // Deux requêtes de Fusionner, une paire chacune (21-22, 21-23) ; aucune de Normaliser.
        var requests = api.ModelRequests.Select(r => (Task: r.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString(),
            Pairs: JsonDocument.Parse(r.GetProperty("messages")[1].GetProperty("content").GetString()!).RootElement.GetArrayLength())).ToList();
        Assert.Equal([("merge", 1), ("merge", 1)], requests);
        var merge = Assert.Single(api.Proposals, p => p.GetProperty("field").GetString() == "merge");
        Assert.Equal((21, "22"), (merge.GetProperty("targetId").GetInt32(), merge.GetProperty("after").GetString()));
    }
}
