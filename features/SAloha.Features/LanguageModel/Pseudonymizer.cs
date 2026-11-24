using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SAloha.Features.LanguageModel;

/// <summary>
/// OPT-24 : avant tout envoi au modèle, les adresses IP et MAC, les noms d'hôte et FQDN, les numéros de série, les
/// comptes et les adresses de courriel sont remplacés par des jetons stables le temps d'une requête (<c>IP_1</c>,
/// <c>HOTE_3</c>, <c>COMPTE_2</c>), puis rétablis dans la réponse. Les valeurs que l'appelant connaît (nom d'un CI,
/// numéro de série) se déclarent en plus des motifs reconnus. La règle vaut dans le SI comme hors du SI.
/// </summary>
public sealed partial class Pseudonymizer
{
    public const string Ip = "IP", Mac = "MAC", Host = "HOTE", Serial = "SERIE", Account = "COMPTE", Mail = "COURRIEL";

    private readonly Dictionary<string, string> tokenOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> valueOf = [];
    private readonly Dictionary<string, int> counters = [];
    private readonly List<(string Kind, string Value)> declared = [];

    /// <summary>Jetons de cette requête et leur valeur, pour rétablir la réponse et reconnaître un jeton inventé.</summary>
    public IReadOnlyDictionary<string, string> Tokens => valueOf;

    /// <summary>Valeur connue de l'appelant, remplacée partout où elle paraît (la plus longue d'abord).</summary>
    public Pseudonymizer Declare(string kind, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 3) declared.Add((kind, value.Trim()));
        return this;
    }

    private string TokenFor(string kind, string value)
    {
        if (tokenOf.TryGetValue(value, out var token)) return token;
        counters[kind] = counters.GetValueOrDefault(kind) + 1;
        token = $"{kind}_{counters[kind]}";
        tokenOf[value] = token;
        valueOf[token] = value;
        return token;
    }

    // Courriels d'abord (ils contiennent un domaine), puis comptes DOMAINE\nom, MAC, IP, FQDN.
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")] private static partial Regex Mails();
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9-]{1,14}\\[A-Za-z0-9._-]{2,}\b")] private static partial Regex Accounts();
    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")] private static partial Regex Macs();
    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")] private static partial Regex Ipv4();
    // IPv6, formes compressées comprises (fe80::1) : repérage large, puis validée par IPAddress.
    [GeneratedRegex(@"(?<![\w:])[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}(?![\w:])")] private static partial Regex Ipv6();
    [GeneratedRegex(@"\b(?=[A-Za-z0-9-]*[A-Za-z])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+(?:local|lan|intra|corp|internal|[A-Za-z]{2,6})\b")]
    private static partial Regex Fqdns();
    [GeneratedRegex(@"\b(?:IP|MAC|HOTE|SERIE|COMPTE|COURRIEL)_\d+\b")] private static partial Regex TokenPattern();

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (kind, value) in declared.OrderByDescending(d => d.Value.Length))
            text = Regex.Replace(text, Regex.Escape(value), _ => TokenFor(kind, value), RegexOptions.IgnoreCase);
        text = Mails().Replace(text, m => TokenFor(Mail, m.Value));
        text = Accounts().Replace(text, m => TokenFor(Account, m.Value));
        text = Macs().Replace(text, m => TokenFor(Mac, m.Value));
        text = Ipv4().Replace(text, m => TokenFor(Ip, m.Value));
        text = Ipv6().Replace(text, m => m.Value.Any(Uri.IsHexDigit) && IPAddress.TryParse(m.Value, out var ip)
            && ip.AddressFamily == AddressFamily.InterNetworkV6 ? TokenFor(Ip, m.Value) : m.Value);
        text = Fqdns().Replace(text, m => TokenPattern().IsMatch(m.Value) ? m.Value : TokenFor(Host, m.Value));
        return text;
    }

    /// <summary>Rétablit les valeurs d'origine dans une réponse du modèle.</summary>
    public string Restore(string text) => TokenPattern().Replace(text, m => valueOf.GetValueOrDefault(m.Value, m.Value));

    /// <summary>OPT-25 : jetons cités par la réponse que cette requête n'a pas émis (inventés par le modèle).</summary>
    public IReadOnlyList<string> UnknownTokens(string text) =>
        TokenPattern().Matches(text).Select(m => m.Value).Where(t => !valueOf.ContainsKey(t)).Distinct().ToList();
}
