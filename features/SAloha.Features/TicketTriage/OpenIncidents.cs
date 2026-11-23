using SAloha.Features.Host;

namespace SAloha.Features.TicketTriage;

/// <summary>Réglages de la corrélation (TRI-05 à TRI-07) ; réglables par l'Admin avec TRI-13.</summary>
public record CorrelationSettings(int WindowMinutes = 60, int OutageThreshold = 3, double MinSimilarity = 0.60);

/// <summary>Un incident ouvert de l'index propre du tri (TRI-06) : de quoi le corréler, rien de plus.</summary>
public sealed record OpenEntry(int Id, int PerimeterId, int? ServiceId, IReadOnlyList<int> CiIds, DateTime CreatedAt, string Status,
    string Impact, string Text, float[]? Vector)
{
    public static OpenEntry From(IncidentRead read, float[]? vector) => new(read.Incident.Id, read.Incident.PerimeterId, read.Incident.ServiceId,
        read.ConfigurationItemIds, read.Incident.CreatedAt, read.Incident.Status ?? "", read.Incident.Impact ?? "", TextOf(read.Incident), vector);

    /// <summary>TRI-06 : titre et description, 2 000 caractères au plus.</summary>
    public static string TextOf(IncidentData i)
    {
        var text = $"{i.Title}\n{i.Description}".Trim();
        return text.Length > 2000 ? text[..2000] : text;
    }
}

/// <summary>Panne probable : incidents liés d'un même périmètre, du plus ancien au plus récent, et ce qui les lie.</summary>
public sealed record OutageGroup(int PerimeterId, IReadOnlyList<int> Members, string Basis, int? ServiceId, string Reasons);

/// <summary>
/// Index propre des incidents ouverts (TRI-06) : en mémoire seulement (TRI-14), reconstruit au démarrage depuis
/// l'API et tenu à jour par le fil d'événements ; un incident résolu, clos ou supprimé en sort. Il ne sert
/// jamais la recherche ni les cas similaires (RAG-01) : il ne quitte pas le conteneur.
/// </summary>
public class OpenIncidents
{
    /// <summary>TRI-05 : ouverts au sens de la corrélation.</summary>
    public static readonly string[] Open = ["Nouveau", "En cours", "En attente"];

    private readonly Dictionary<int, OpenEntry> entries = [];
    public bool Built { get; private set; }
    public DateTime? RebuiltAt { get; private set; }
    public int Count => entries.Count;

    public OpenEntry? Get(int id) => entries.GetValueOrDefault(id);
    public void Upsert(OpenEntry e) => entries[e.Id] = e;
    public bool Remove(int id) => entries.Remove(id);
    public IReadOnlyList<OpenEntry> InPerimeter(int perimeterId) => entries.Values.Where(e => e.PerimeterId == perimeterId).ToList();
    public IReadOnlyList<int> Perimeters => entries.Values.Select(e => e.PerimeterId).Distinct().ToList();

    public void Replace(IEnumerable<OpenEntry> all)
    {
        entries.Clear();
        foreach (var e in all) entries[e.Id] = e;
        Built = true;
        RebuiltAt = DateTime.UtcNow;
    }

    /// <summary>
    /// TRI-05, TRI-06 : deux incidents ouverts d'un même périmètre, créés à moins de la fenêtre d'intervalle, sont
    /// liés probables s'ils portent le même service, sont reliés au même CI, ou si leur similarité atteint le seuil.
    /// </summary>
    public static (bool Service, bool Ci, bool Sens) Links(OpenEntry a, OpenEntry b, CorrelationSettings s)
    {
        if (a.Id == b.Id || a.PerimeterId != b.PerimeterId) return default;
        if ((a.CreatedAt - b.CreatedAt).Duration() > TimeSpan.FromMinutes(s.WindowMinutes)) return default;
        var service = a.ServiceId is not null && a.ServiceId == b.ServiceId;
        var ci = a.CiIds.Intersect(b.CiIds).Any();
        var sens = a.Vector is not null && b.Vector is not null && EmbeddingClient.Cosine(a.Vector, b.Vector) >= s.MinSimilarity;
        return (service, ci, sens);
    }

    public static bool Linked(OpenEntry a, OpenEntry b, CorrelationSettings s) => Links(a, b, s) is var l && (l.Service || l.Ci || l.Sens);

    /// <summary>
    /// TRI-07 : les incidents liés de proche en proche forment un groupe ; un groupe d'au moins <c>OutageThreshold</c>
    /// incidents est une panne probable. Jamais deux périmètres (TRI-11) : on ne corrèle que dans un périmètre.
    /// </summary>
    public static List<OutageGroup> Outages(IReadOnlyList<OpenEntry> perimeter, CorrelationSettings s, IReadOnlyDictionary<int, string> serviceTitles)
    {
        var groups = new List<OutageGroup>();
        var seen = new HashSet<int>();
        foreach (var start in perimeter.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id))
        {
            if (!seen.Add(start.Id)) continue;
            var members = new List<OpenEntry> { start };
            var kinds = (Service: 0, Ci: 0, Sens: 0);
            for (var i = 0; i < members.Count; i++)
                foreach (var other in perimeter)
                {
                    var l = Links(members[i], other, s);
                    if (!(l.Service || l.Ci || l.Sens)) continue;
                    if (seen.Add(other.Id)) members.Add(other);
                    kinds = (kinds.Service + (l.Service ? 1 : 0), kinds.Ci + (l.Ci ? 1 : 0), kinds.Sens + (l.Sens ? 1 : 0));
                }
            if (members.Count < s.OutageThreshold) continue;
            var ordered = members.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
            var n = ordered.Count;
            var commonService = ordered.Select(m => m.ServiceId).Distinct().Count() == 1 ? ordered[0].ServiceId : null;
            var commonCi = ordered.Skip(1).Aggregate((IEnumerable<int>)ordered[0].CiIds, (acc, m) => acc.Intersect(m.CiIds)).Any();
            var (basis, service, reasons) =
                commonService is { } sid ? ("service", (int?)sid, $"{n} incidents ouverts sur le service {serviceTitles.GetValueOrDefault(sid, $"n° {sid}")}")
                : commonCi ? ("ci", null, $"{n} incidents ouverts reliés à un même CI")
                : kinds.Sens > 0 && kinds.Service == 0 && kinds.Ci == 0 ? ("sens", null, $"{n} incidents ouverts aux descriptions proches")
                : (kinds.Sens >= Math.Max(kinds.Service, kinds.Ci) ? "sens" : kinds.Ci > kinds.Service ? "ci" : "service", (int?)null,
                   $"{n} incidents ouverts liés par leur service, un CI ou leur description");
            groups.Add(new OutageGroup(start.PerimeterId, ordered.Select(m => m.Id).ToList(), basis, service, reasons));
        }
        return groups;
    }
}
