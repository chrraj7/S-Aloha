using System.Text.Json;
using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.AssistedCmdb;

/// <summary>Un actif du sas laissé sans rapprochement par CFG-41, tel que l'API le remet (OPT-13).</summary>
public record StagingAsset(int Id, int PerimeterId, string? Status, string? Hostname, string? IpAddress, string? MacAddress, string? SerialNumber,
    string? Vendor, string? Model, string? DetectedOs, string? DetectedType, string? OpenPorts, string? Sources, bool InDirectory, int? MatchedCiId);
public record StagingJob(int Id, int PerimeterId, bool AssistedCmdb, string? LastStatus);
public record StagingRead(StagingJob Job, List<StagingAsset> Assets);

/// <summary>Un actif et ses CI candidats, du plus proche au moins proche.</summary>
public record StagingSubject(StagingAsset Asset, IReadOnlyList<CiRecord> Candidates);

/// <summary>Une proposition d'aide au sas : sur l'actif, un CI candidat (<c>matchedCiId</c>) ou un type pour la création (<c>ciType</c>).</summary>
public record StagingProposal(int Asset, string Field, string Value, string Reasons);

/// <summary>
/// CMA-08 : aide au sas, en fonctions pures. Pour chaque actif, le conteneur présélectionne lui-même au plus
/// <see cref="MaxCandidates"/> CI candidats du périmètre (serveurs et équipements non retirés, au nom proche du nom d'hôte, ou
/// celui que CFG-41 a trouvé en conflit) ; le modèle choisit l'un d'eux, ou un type pour créer le CI. Noms d'hôte, adresses et
/// numéros de série sont pseudonymisés (CMA-07) : un CI qui porte le nom d'hôte en reçoit le même jeton.
/// </summary>
public static class Staging
{
    public const string Task = "staging";
    public const int AssetsPerRequest = 20, MaxCandidates = 5;
    private static readonly string[] Hardware = ["Serveur", "Réseau", "Stockage", "Poste de travail"];

    private static string? Short(string? host) => host?.Split('.')[0];

    public static List<StagingSubject> Subjects(IReadOnlyList<StagingAsset> assets, IReadOnlyList<CiRecord> items) =>
        assets.OrderBy(a => a.Id).Select(asset =>
        {
            var scored = items.Where(c => Hardware.Contains(c.CiType) && c.Status != "Retiré" && c.PerimeterId == asset.PerimeterId)
                .Select(c => (Ci: c, Score: c.Id == asset.MatchedCiId ? 2
                    : Math.Max(Duplicates.Closeness(Short(asset.Hostname), c.Title), Duplicates.Closeness(asset.Hostname, c.Title))))
                .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Ci.Id).Take(MaxCandidates).Select(x => x.Ci).ToList();
            return new StagingSubject(asset, scored);
        }).ToList();

    public static readonly JsonElement Schema = CmdbTasks.Schema(CmdbTasks.Item(new()
    {
        ["actif"] = CmdbTasks.CiId, ["ci"] = CmdbTasks.CiId,
        ["type"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = CmdbTasks.CiTypes }, ["motif"] = CmdbTasks.Motif,
    }));

    public const string Instructions =
        "Tu aides à rapprocher des équipements découverts sur le réseau des CI d'une CMDB. Chaque actif est donné avec ses CI candidats. "
        + "Si l'un d'eux décrit le même équipement (même nom, même système, même constructeur et modèle), réponds avec son identifiant dans "
        + "« ci » ; sinon, réponds 0 dans « ci » et le type de CI à créer dans « type ». Dans les deux cas, « type » est le type de "
        + "l'équipement. Les noms d'hôte, adresses et numéros de série sont remplacés par des jetons : deux jetons identiques désignent la "
        + "même valeur. Réponds pour chaque actif, par son identifiant « id »." + CmdbTasks.Common;

    /// <summary>CMA-07 : chaque actif et ses candidats, valeurs sensibles déclarées au pseudonymiseur.</summary>
    public static string Input(IReadOnlyList<StagingSubject> subjects, Pseudonymizer pseudo)
    {
        foreach (var s in subjects)
        {
            pseudo.Declare(Pseudonymizer.Host, s.Asset.Hostname).Declare(Pseudonymizer.Host, Short(s.Asset.Hostname))
                .Declare(Pseudonymizer.Ip, s.Asset.IpAddress).Declare(Pseudonymizer.Mac, s.Asset.MacAddress).Declare(Pseudonymizer.Serial, s.Asset.SerialNumber);
            foreach (var c in s.Candidates)
                pseudo.Declare(Pseudonymizer.Ip, c.IpAddress).Declare(Pseudonymizer.Mac, c.MacAddress).Declare(Pseudonymizer.Serial, c.SerialNumber)
                    .Declare(Pseudonymizer.Account, c.OwnerId);
        }
        return CmdbTasks.Serialize(subjects.Select(s => new
        {
            // Le nom court : un CI qui le porte reçoit le même jeton (le domaine n'aide pas à rapprocher).
            id = s.Asset.Id, nom = Short(s.Asset.Hostname), systeme = s.Asset.DetectedOs, constructeur = s.Asset.Vendor, modele = s.Asset.Model,
            typeDetecte = s.Asset.DetectedType, ports = s.Asset.OpenPorts, sources = s.Asset.Sources, annuaire = s.Asset.InDirectory,
            candidats = s.Candidates.Select(c => new
            {
                id = c.Id, nom = c.Title, type = c.CiType, systeme = c.OperatingSystem, constructeur = c.Vendor, modele = c.Model,
            }).ToList(),
        }));
    }

    /// <summary>
    /// CMA-09 : les propositions d'une réponse valide. Écartée et comptée : un actif hors de la requête, un CI qui n'est pas l'un
    /// de ses candidats. Ignorée sans compter : un second avis sur le même actif, un type égal au type détecté sans CI candidat,
    /// le CI que CFG-41 proposait déjà.
    /// </summary>
    public static (List<StagingProposal> Kept, int Rejected) Read(JsonElement output, IReadOnlyList<StagingSubject> subjects)
    {
        var byId = subjects.ToDictionary(s => s.Asset.Id);
        var kept = new List<StagingProposal>();
        var seen = new HashSet<int>();
        var rejected = 0;
        foreach (var p in output.GetProperty("propositions").EnumerateArray())
        {
            var (asset, ci, type) = (p.GetProperty("actif").GetInt32(), p.GetProperty("ci").GetInt32(), p.GetProperty("type").GetString() ?? "");
            if (!byId.TryGetValue(asset, out var subject) || (ci != 0 && subject.Candidates.All(c => c.Id != ci)))
            {
                rejected++;
                continue;
            }
            if (!seen.Add(asset)) continue;
            var reasons = p.GetProperty("motif").GetString()?.Trim() ?? "";
            if (ci != 0 && ci != subject.Asset.MatchedCiId) kept.Add(new StagingProposal(asset, "matchedCiId", ci.ToString(), reasons));
            else if (ci == 0 && type != subject.Asset.DetectedType) kept.Add(new StagingProposal(asset, "ciType", type, reasons));
        }
        return (kept, rejected);
    }
}
