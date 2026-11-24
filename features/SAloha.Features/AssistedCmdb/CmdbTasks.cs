using System.Text.Json;
using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.AssistedCmdb;

/// <summary>Une proposition tirée d'une réponse du modèle, avant son dépôt (OPT-14) : CI, champ, valeur, motif.</summary>
public record CmdbProposal(int Ci, string Field, string Value, string Reasons);

/// <summary>
/// CMA-02 : les tâches Renommer, Normaliser, Reclasser et Criticité, en fonctions pures. Chacune a ses consignes versionnées
/// (OPT-27), son schéma de sortie (OPT-25) et son champ de CI ; l'entrée ne porte que les champs utiles (CMA-07),
/// pseudonymisés par le connecteur ; chaque proposition de la réponse est vérifiée (CMA-09).
/// </summary>
public static class CmdbTasks
{
    public const string Rename = "rename", Normalize = "normalize", Reclassify = "reclassify", Criticality = "criticality";
    public const string PromptVersion = "1";
    public const int DescriptionMax = 1000;

    public static readonly string[] CiTypes = ["Application", "Serveur", "Base de données", "Réseau", "Stockage", "Poste de travail", "Logiciel", "Autre"];
    public static readonly string[] NormalizedFields = ["operatingSystem", "vendor", "model"];
    /// <summary>CFG-43 : criticités d'une application.</summary>
    public static readonly string[] Criticalities = ["Vitale", "Majeure", "Standard", "Mineure"];

    /// <summary>CMA-02 : la tâche Criticité ne vise que les applications qui n'en ont pas.</summary>
    public static bool NeedsCriticality(CiRecord ci) => ci.CiType == "Application" && string.IsNullOrWhiteSpace(ci.Criticality);

    internal static JsonElement Schema(object item) => JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object", ["required"] = new[] { "propositions" }, ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object> { ["propositions"] = new Dictionary<string, object> { ["type"] = "array", ["maxItems"] = 60, ["items"] = item } },
    });

    internal static Dictionary<string, object> Item(Dictionary<string, object> properties) => new()
    {
        ["type"] = "object", ["required"] = properties.Keys.ToArray(), ["additionalProperties"] = false, ["properties"] = properties,
    };

    internal static readonly Dictionary<string, object> Motif = new() { ["type"] = "string", ["maxLength"] = 300 };
    internal static readonly Dictionary<string, object> CiId = new() { ["type"] = "integer" };

    public static readonly JsonElement RenameSchema = Schema(Item(new()
    {
        ["ci"] = CiId, ["nom"] = new Dictionary<string, object> { ["type"] = "string", ["maxLength"] = 200 }, ["motif"] = Motif,
    }));

    public static readonly JsonElement NormalizeSchema = Schema(Item(new()
    {
        ["ci"] = CiId, ["champ"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = NormalizedFields },
        ["valeur"] = new Dictionary<string, object> { ["type"] = "string", ["maxLength"] = 150 }, ["motif"] = Motif,
    }));

    public static readonly JsonElement ReclassifySchema = Schema(Item(new()
    {
        ["ci"] = CiId, ["type"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = CiTypes }, ["motif"] = Motif,
    }));

    public static readonly JsonElement CriticalitySchema = Schema(Item(new()
    {
        ["ci"] = CiId, ["criticite"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Criticalities }, ["motif"] = Motif,
    }));

    internal const string Common =
        " Les CI sont donnés en JSON, chacun avec son identifiant « id » ; réponds par leurs identifiants. N'invente aucune information "
        + "absente des données. Le motif dit en une phrase courte, en français, ce qui justifie la proposition.";

    public static string Instructions(string task, IReadOnlyList<NamingRule> naming) => task switch
    {
        Rename => "Tu proposes de renommer des CI d'une CMDB selon la convention de nommage de leur type, donnée ci-dessous. Ne propose un nom "
            + "que pour un CI dont le nom ne suit pas la convention ; le nouveau nom suit la convention et garde ce que l'ancien dit du CI "
            + "(rôle, site, numéro)." + Common + "\n\nConventions :\n"
            + string.Join('\n', naming.Select(n => $"- {n.CiType} : {n.Description}" + (n.Examples is { Count: > 0 } ex ? $" (exemples : {string.Join(", ", ex)})" : ""))),
        Normalize => "Tu normalises l'écriture du système d'exploitation (operatingSystem), du constructeur (vendor) et du modèle (model) des CI "
            + "d'une CMDB. Pour chaque champ écrit de façon abrégée ou non standard (par exemple « Win2019 std » pour « Windows Server 2019 "
            + "Standard »), propose la forme normalisée. Ne propose rien pour un champ vide ou déjà bien écrit." + Common,
        Reclassify => $"Tu vérifies le type des CI d'une CMDB. Types permis : {string.Join(", ", CiTypes)}. Pour chaque CI dont le type est « Autre » "
            + "ou manifestement faux au vu de son nom, de son système et de sa description, propose le bon type. Ne propose rien quand le type "
            + "actuel convient." + Common,
        Criticality => "Tu proposes la criticité d'applications d'une CMDB qui n'en ont pas, parmi : Vitale (arrêt de l'activité), Majeure "
            + "(activité fortement dégradée), Standard (gêne contournable), Mineure (confort). Juge d'après le nom, la description et les "
            + "relations (une application dont dépendent beaucoup d'autres, ou qui sert la paie, la facturation, la production, est plus "
            + "critique). Ne propose rien si les données ne permettent pas d'en juger." + Common,
        _ => throw new ArgumentException($"Tâche inconnue : {task}", nameof(task)),
    };

    public static JsonElement SchemaOf(string task) => task switch
    {
        Rename => RenameSchema, Normalize => NormalizeSchema, Reclassify => ReclassifySchema, Criticality => CriticalitySchema,
        _ => throw new ArgumentException($"Tâche inconnue : {task}", nameof(task)),
    };

    /// <summary>Champ du CI que la tâche propose de changer (OPT-14).</summary>
    public static string FieldOf(string task, string? champ = null) => task switch
    {
        Rename => "title", Reclassify => "ciType", Criticality => "criticality", Normalize => champ ?? "", _ => "",
    };

    /// <summary>
    /// CMA-07 : les seuls champs utiles des CI d'un lot (nom, type, environnement, système, constructeur, modèle, description
    /// bornée, relations existantes). Ni adresse, ni numéro de série, ni compte : ils ne sont pas envoyés, et s'ils paraissent
    /// dans une description, le pseudonymiseur les masque (ils lui sont déclarés).
    /// </summary>
    public static string Input(IReadOnlyList<CiRecord> batch, IReadOnlyDictionary<int, string> titles, Pseudonymizer pseudo) =>
        Serialize(batch.Select(ci => Describe(ci, titles, pseudo)));

    /// <summary>Un CI tel qu'il part au modèle (CMA-07) ; ses valeurs sensibles sont déclarées au pseudonymiseur.</summary>
    public static object Describe(CiRecord ci, IReadOnlyDictionary<int, string> titles, Pseudonymizer pseudo)
    {
        pseudo.Declare(Pseudonymizer.Ip, ci.IpAddress).Declare(Pseudonymizer.Mac, ci.MacAddress).Declare(Pseudonymizer.Serial, ci.SerialNumber)
            .Declare(Pseudonymizer.Account, ci.OwnerId);
        string Name(int id) => titles.GetValueOrDefault(id, $"CI {id}");
        return new
        {
            id = ci.Id, nom = ci.Title, type = ci.CiType, environnement = ci.Environment, systeme = ci.OperatingSystem, constructeur = ci.Vendor,
            modele = ci.Model,
            description = ci.Description is { Length: > DescriptionMax } d ? d[..DescriptionMax] : ci.Description,
            relations = (ci.Outgoing ?? []).Select(r => $"{r.Type} {Name(r.TargetId)}")
                .Concat((ci.Incoming ?? []).Select(r => $"{Name(r.SourceId)} {r.Type} ce CI")).ToList(),
        };
    }

    public static string Serialize(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>
    /// CMA-09 : les propositions d'une réponse valide. Écartée et comptée : un CI hors du lot, une valeur vide, un renommage
    /// d'un type sans convention, une criticité pour un CI qui n'est pas une application sans criticité. Ignorée sans compter :
    /// une valeur égale à l'actuelle, un second avis sur le même champ.
    /// </summary>
    public static (List<CmdbProposal> Kept, int Rejected) Read(string task, JsonElement output, IReadOnlyDictionary<int, CiRecord> batch,
        IReadOnlySet<string> namedTypes)
    {
        var kept = new List<CmdbProposal>();
        var rejected = 0;
        var seen = new HashSet<(int, string)>();
        foreach (var p in output.GetProperty("propositions").EnumerateArray())
        {
            var id = p.GetProperty("ci").GetInt32();
            var (field, value) = task switch
            {
                Rename => ("title", p.GetProperty("nom").GetString()),
                Reclassify => ("ciType", p.GetProperty("type").GetString()),
                Criticality => ("criticality", p.GetProperty("criticite").GetString()),
                _ => (p.GetProperty("champ").GetString()!, p.GetProperty("valeur").GetString()),
            };
            value = value?.Trim();
            if (!batch.TryGetValue(id, out var ci) || string.IsNullOrEmpty(value) || (task == Rename && !namedTypes.Contains(ci.CiType ?? ""))
                || (task == Criticality && !NeedsCriticality(ci)))
            {
                rejected++;
                continue;
            }
            var current = field switch
            {
                "title" => ci.Title, "ciType" => ci.CiType, "operatingSystem" => ci.OperatingSystem, "vendor" => ci.Vendor, "model" => ci.Model,
                "criticality" => ci.Criticality, _ => null,
            };
            if (value == current?.Trim() || !seen.Add((id, field))) continue;
            kept.Add(new CmdbProposal(id, field, value, p.GetProperty("motif").GetString()?.Trim() ?? ""));
        }
        return (kept, rejected);
    }
}
