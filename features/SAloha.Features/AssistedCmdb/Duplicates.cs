using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SAloha.Features.Host;
using SAloha.Features.LanguageModel;

namespace SAloha.Features.AssistedCmdb;

/// <summary>Une paire candidate, identifiants dans l'ordre croissant, et la proximité de leurs noms (de 0 à 1).</summary>
public record CandidatePair(int A, int B, double Score);

/// <summary>Une fusion tirée d'une réponse du modèle : le doublon (cible de la proposition), le CI à conserver, le motif.</summary>
public record MergeProposal(int Duplicate, int Keep, string Reasons);

/// <summary>
/// CMA-06 : doublons candidats, en fonctions pures. Le conteneur présélectionne lui-même les paires (même environnement, types
/// compatibles, noms proches une fois normalisés, paires rejetées exclues) ; le modèle ne juge que ces paires, par requêtes de
/// <see cref="PairsPerRequest"/> au plus (moins si l'Admin l'a réglé, CMA-12), et ne voit jamais toute la CMDB. Les CI de
/// l'analyse sont déjà ceux d'un seul périmètre (CMA-07).
/// </summary>
public static partial class Duplicates
{
    public const string Task = "merge";
    public const int PairsPerRequest = 50;
    /// <summary>Proximité minimale des noms compactés (distance d'édition rapportée à la longueur).</summary>
    public const double MinSimilarity = 0.8;

    /// <summary>Mots trop communs pour rapprocher deux noms à eux seuls.</summary>
    private static readonly HashSet<string> Generic =
    [
        "serveur", "server", "prod", "production", "preprod", "recette", "test", "tests", "developpement", "virtuel", "virtual", "machine",
        "base", "donnees", "data", "database", "appli", "application", "poste", "laptop", "portable", "switch", "routeur", "router", "stockage",
        "storage", "logiciel", "site", "siege", "linux", "windows", "ubuntu", "debian", "host", "hote", "node", "noeud", "cluster", "principal",
        "primaire", "secondaire", "backup", "sauvegarde", "interne", "externe",
    ];

    /// <summary>Types qui désignent souvent la même chose, saisie par deux sources ; « Autre » se rapproche de tout type.</summary>
    private static readonly HashSet<string> Software = ["Application", "Logiciel"];

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    /// <summary>Nom comparé : minuscules, sans accents, lettres et chiffres séparés d'une seule espace (comme ADM-40).</summary>
    public static string Normalize(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Compatible(string? a, string? b) =>
        a == b || a == "Autre" || b == "Autre" || (Software.Contains(a ?? "") && Software.Contains(b ?? ""));

    /// <summary>Nombres d'un nom, sans zéros de tête : « 01 » et « 1 » se valent.</summary>
    private static string[] Numbers(string normalized) =>
        Digits().Matches(normalized).Select(m => m.Value.TrimStart('0') is { Length: > 0 } n ? n : "0").Order().ToArray();

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return 1 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }

    /// <summary>
    /// Deux noms proches : leurs numéros, s'ils en ont tous deux, sont les mêmes (SRV-PAIE-01 et SRV-PAIE-02 sont deux serveurs) ;
    /// et leurs formes compactées sont voisines, ou un mot distinctif de l'un (4 lettres au moins, pas un mot commun) figure dans
    /// l'autre. Proximité : celle des formes compactées, 0,6 au moins quand un mot distinctif est partagé ; 0 : pas proches.
    /// </summary>
    public static double Closeness(string? a, string? b)
    {
        var (na, nb) = (Normalize(a), Normalize(b));
        if (na == "" || nb == "") return 0;
        var (numbersA, numbersB) = (Numbers(na), Numbers(nb));
        if (numbersA.Length > 0 && numbersB.Length > 0 && !numbersA.SequenceEqual(numbersB)) return 0;
        var (ca, cb) = (na.Replace(" ", ""), nb.Replace(" ", ""));
        var similarity = Similarity(ca, cb);
        if (similarity >= MinSimilarity) return similarity;
        static IEnumerable<string> Words(string n) =>
            n.Split(' ').Select(w => new string(w.Where(char.IsLetter).ToArray())).Where(w => w.Length >= 4 && !Generic.Contains(w));
        var shared = Words(na).Any(cb.Contains) || Words(nb).Any(ca.Contains);
        return shared ? Math.Max(similarity, 0.6) : 0;
    }

    /// <summary>
    /// Paires à soumettre parmi les CI de l'analyse : même environnement, types compatibles, noms proches ; une paire rejetée
    /// (CMA-06) n'y figure plus. Les plus proches d'abord.
    /// </summary>
    public static List<CandidatePair> Candidates(IReadOnlyList<CiRecord> items, IReadOnlySet<(int, int)> declined)
    {
        var pairs = new List<CandidatePair>();
        var ordered = items.OrderBy(c => c.Id).ToList();
        for (var i = 0; i < ordered.Count; i++)
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var (a, b) = (ordered[i], ordered[j]);
                if (a.Environment != b.Environment || !Compatible(a.CiType, b.CiType) || declined.Contains((a.Id, b.Id))) continue;
                if (Closeness(a.Title, b.Title) is var score and > 0) pairs.Add(new CandidatePair(a.Id, b.Id, score));
            }
        return pairs.OrderByDescending(p => p.Score).ThenBy(p => p.A).ThenBy(p => p.B).ToList();
    }

    public static readonly JsonElement Schema = CmdbTasks.Schema(CmdbTasks.Item(new()
    {
        ["conserver"] = CmdbTasks.CiId, ["doublon"] = CmdbTasks.CiId, ["motif"] = CmdbTasks.Motif,
    }));

    public const string Instructions =
        "Tu repères les doublons d'une CMDB : deux fiches qui décrivent le même élément (le même serveur, la même application, saisis "
        + "deux fois, par exemple par l'import et par la découverte). Tu reçois des paires de CI. Pour chaque paire qui est un doublon, "
        + "indique le CI à conserver (« conserver » : le plus complet, ou celui dont le nom suit la convention) et celui à retirer "
        + "(« doublon »). Ne réponds rien pour une paire de CI distincts, même aux noms proches (deux nœuds d'une grappe, une base et "
        + "l'application qui l'utilise)." + CmdbTasks.Common;

    /// <summary>CMA-07 : les paires d'une requête, chaque CI décrit par ses seuls champs utiles, pseudonymisés.</summary>
    public static string Input(IReadOnlyList<CandidatePair> pairs, IReadOnlyDictionary<int, CiRecord> items, IReadOnlyDictionary<int, string> titles,
        Pseudonymizer pseudo) =>
        CmdbTasks.Serialize(pairs.Select(p => new
        {
            a = CmdbTasks.Describe(items[p.A], titles, pseudo), b = CmdbTasks.Describe(items[p.B], titles, pseudo),
        }));

    /// <summary>
    /// CMA-09 : les fusions d'une réponse valide. Écartée et comptée : une paire qui n'a pas été soumise, ou un CI conservé égal au
    /// doublon. Ignoré sans compter : un second avis sur la même paire.
    /// </summary>
    public static (List<MergeProposal> Kept, int Rejected) Read(JsonElement output, IReadOnlyList<CandidatePair> pairs)
    {
        var submitted = pairs.Select(p => (p.A, p.B)).ToHashSet();
        var kept = new List<MergeProposal>();
        var seen = new HashSet<(int, int)>();
        var rejected = 0;
        foreach (var p in output.GetProperty("propositions").EnumerateArray())
        {
            var (keep, duplicate) = (p.GetProperty("conserver").GetInt32(), p.GetProperty("doublon").GetInt32());
            var pair = (Math.Min(keep, duplicate), Math.Max(keep, duplicate));
            if (keep == duplicate || !submitted.Contains(pair))
            {
                rejected++;
                continue;
            }
            if (!seen.Add(pair)) continue;
            kept.Add(new MergeProposal(duplicate, keep, p.GetProperty("motif").GetString()?.Trim() ?? ""));
        }
        return (kept, rejected);
    }
}
