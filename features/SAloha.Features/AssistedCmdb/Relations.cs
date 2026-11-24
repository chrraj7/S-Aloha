using System.Text.Json;
using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.AssistedCmdb;

/// <summary>Un CI source et les CI de l'analyse que sa description cite : les cibles possibles de ses relations.</summary>
public record RelationSubject(CiRecord Source, IReadOnlyList<CiRecord> Targets);

/// <summary>Une relation tirée d'une réponse du modèle : source, cible, type, motif.</summary>
public record RelationProposal(int Source, int Target, string Type, string Reasons);

/// <summary>
/// CMA-02 : la tâche Relier, en fonctions pures. Les dépendances que la CMDB n'a pas sont souvent écrites dans les
/// descriptions (« dépend de la base BDD-PAIE ») : le conteneur ne soumet que les CI dont la description cite le nom ou la
/// référence d'un autre CI de l'analyse, avec ces seules cibles ; le modèle ne voit jamais toute la CMDB. Une réponse qui
/// relie un CI hors de l'analyse, un CI à lui-même, ou reprend une relation existante est écartée et comptée (CMA-09).
/// </summary>
public static class Relations
{
    public const string Task = "relate";
    /// <summary>CI sources par requête, comme les lots des autres tâches.</summary>
    public const int SourcesPerRequest = 20;
    public static readonly string[] Types = ["Dépend de", "Héberge", "Fait partie de", "Se connecte à"];

    /// <summary>Un nom cité : son nom normalisé (quatre caractères au moins), entier dans la description normalisée, ou sa référence.</summary>
    private static bool Cites(string description, string normalized, CiRecord target)
    {
        var name = Duplicates.Normalize(target.Title);
        return (name.Length >= 4 && normalized.Contains($" {name} "))
            || (target.Reference is { Length: > 0 } reference && description.Contains(reference, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Les CI de l'analyse dont la description cite au moins un autre CI de l'analyse, avec ces cibles.</summary>
    public static List<RelationSubject> Subjects(IReadOnlyList<CiRecord> items) =>
        items.Where(c => !string.IsNullOrWhiteSpace(c.Description)).OrderBy(c => c.Id).Select(source =>
        {
            var normalized = $" {Duplicates.Normalize(source.Description)} ";
            return new RelationSubject(source, items.Where(t => t.Id != source.Id && Cites(source.Description!, normalized, t)).OrderBy(t => t.Id).ToList());
        }).Where(s => s.Targets.Count > 0).ToList();

    public static readonly JsonElement Schema = CmdbTasks.Schema(CmdbTasks.Item(new()
    {
        ["source"] = CmdbTasks.CiId, ["cible"] = CmdbTasks.CiId,
        ["relation"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Types }, ["motif"] = CmdbTasks.Motif,
    }));

    public const string Instructions =
        "Tu repères les relations qui manquent entre des CI d'une CMDB. Chaque CI source est donné avec les CI que sa description "
        + "cite (« cibles »). Propose une relation seulement si la description l'établit, avec son sens : « Dépend de » (la source a "
        + "besoin de la cible pour fonctionner), « Héberge » (la source fait tourner la cible), « Fait partie de » (la source est un "
        + "élément de la cible), « Se connecte à » (la source échange avec la cible). Ne propose ni une relation déjà listée dans "
        + "« relations », ni une relation simplement possible." + CmdbTasks.Common;

    /// <summary>CMA-07 : chaque source décrite par ses champs utiles, pseudonymisés ; ses cibles par leur nom et leur type.</summary>
    public static string Input(IReadOnlyList<RelationSubject> subjects, IReadOnlyDictionary<int, string> titles, Pseudonymizer pseudo) =>
        CmdbTasks.Serialize(subjects.Select(s => new
        {
            ci = CmdbTasks.Describe(s.Source, titles, pseudo),
            cibles = s.Targets.Select(t => new { id = t.Id, nom = t.Title, type = t.CiType }).ToList(),
        }));

    /// <summary>
    /// CMA-09 : les relations d'une réponse valide. Écartée et comptée : une source hors de la requête, une cible hors de
    /// l'analyse, une relation d'un CI vers lui-même ou qui existe déjà. Ignorée sans compter : la même relation proposée deux fois.
    /// </summary>
    public static (List<RelationProposal> Kept, int Rejected) Read(JsonElement output, IReadOnlyList<RelationSubject> subjects,
        IReadOnlySet<int> analysis)
    {
        var sources = subjects.ToDictionary(s => s.Source.Id, s => s.Source);
        var kept = new List<RelationProposal>();
        var seen = new HashSet<(int, int, string)>();
        var rejected = 0;
        foreach (var p in output.GetProperty("propositions").EnumerateArray())
        {
            var (source, target, type) = (p.GetProperty("source").GetInt32(), p.GetProperty("cible").GetInt32(), p.GetProperty("relation").GetString() ?? "");
            if (!sources.TryGetValue(source, out var ci) || !analysis.Contains(target) || target == source || !Types.Contains(type)
                || (ci.Outgoing ?? []).Any(r => r.TargetId == target && r.Type == type))
            {
                rejected++;
                continue;
            }
            if (!seen.Add((source, target, type))) continue;
            kept.Add(new RelationProposal(source, target, type, p.GetProperty("motif").GetString()?.Trim() ?? ""));
        }
        return (kept, rejected);
    }
}
