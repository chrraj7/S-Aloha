using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Data;
using SAloha.Api.Modules.ServiceConfigurationManagement.Discovery;

namespace SAloha.Api.Core.Features;

/// <summary>
/// OPT-20 : le modèle de langage des fonctionnalités optionnelles (aujourd'hui la seule CMDB assistée), réglé par
/// l'Admin ; une seule ligne (<c>Id</c> = 1), comme le SMTP (ADM-60). La clé est chiffrée par le coffre (OPT-23).
/// </summary>
public class LanguageModelSettings
{
    public const string InSi = "Dans le SI", Hosted = "Hébergé hors du SI";
    public static readonly string[] Locations = [InSi, Hosted];

    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    [MaxLength(20)] public string Location { get; set; } = InSi;
    [MaxLength(255)] public string Url { get; set; } = "";
    [MaxLength(100)] public string Model { get; set; } = "";
    public string? EncryptedKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxResponseKb { get; set; } = 64;
    /// <summary>OPT-22 : confirmation explicite qu'un modèle hébergé reçoit des données hors du SI ; null sinon.</summary>
    public DateTime? HostedConfirmedAt { get; set; }
    [MaxLength(200)] public string? HostedConfirmedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(200)] public string? UpdatedBy { get; set; }

    public bool Ready => Enabled && Url != "" && Model != "";
}

/// <summary>Saisie des réglages. <c>Key</c> absente : inchangée ; vide : effacée. <c>ConfirmHosted</c> : OPT-22.</summary>
public record LanguageModelDto(bool Enabled, string? Location, string? Url, string? Model, string? Key, int TimeoutSeconds = 120,
    int MaxResponseKb = 64, bool ConfirmHosted = false);

/// <summary>Les réglages tels que l'interface les voit : jamais la clé, seulement si elle est enregistrée (OPT-23).</summary>
public record LanguageModelView(bool Enabled, string Location, string Url, string Model, bool HasKey, int TimeoutSeconds, int MaxResponseKb,
    bool DataLeavesSi, DateTime? HostedConfirmedAt, string? HostedConfirmedBy, DateTime? UpdatedAt, string? UpdatedBy)
{
    public static LanguageModelView From(LanguageModelSettings s) => new(s.Enabled, s.Location, s.Url, s.Model, s.EncryptedKey is not null,
        s.TimeoutSeconds, s.MaxResponseKb, s.Location == LanguageModelSettings.Hosted, s.HostedConfirmedAt, s.HostedConfirmedBy, s.UpdatedAt, s.UpdatedBy);
}

/// <summary>
/// OPT-27 : trace d'un appel au modèle, remise par le conteneur (ou par l'essai), sans l'invite ni la réponse en clair :
/// l'entrée pseudonymisée n'en laisse que son empreinte SHA-256. Gardée 90 jours (<c>Features:LanguageModelCallRetentionDays</c>).
/// </summary>
public class LanguageModelCall
{
    public const string Valid = "valide", NonConforming = "non conforme", TimedOut = "délai", Failed = "erreur";
    public static readonly string[] Outcomes = [Valid, NonConforming, TimedOut, Failed];
    /// <summary>L'essai de la page (OPT-26) se trace sous ce nom, hors de toute fonctionnalité.</summary>
    public const string EssayFeature = "language-model", EssayTask = "essai";

    public long Id { get; set; }
    public DateTime At { get; set; }
    [MaxLength(50)] public string Feature { get; set; } = "";
    [MaxLength(50)] public string Task { get; set; } = "";
    [MaxLength(100)] public string Model { get; set; } = "";
    [MaxLength(20)] public string Location { get; set; } = "";
    [MaxLength(20)] public string PromptVersion { get; set; } = "";
    [MaxLength(64)] public string InputHash { get; set; } = "";
    public int DurationMs { get; set; }
    [MaxLength(20)] public string Outcome { get; set; } = "";
    public int Attempts { get; set; }
    /// <summary>OPT-25 : réponses écartées (non conformes) pendant l'appel.</summary>
    public int Rejected { get; set; }
    [MaxLength(500)] public string? Error { get; set; }
}

/// <summary>Trace remise par le conteneur ; mêmes champs que <see cref="LanguageModelCall"/>, sans l'identifiant ni la date.</summary>
public record LanguageModelCallInput(string? Feature, string? Task, string? Model, string? Location, string? PromptVersion, string? InputHash,
    int DurationMs, string? Outcome, int Attempts, int Rejected, string? Error)
{
    /// <summary>Champs bornés ; rend le motif d'un refus, ou null.</summary>
    public string? Invalid(bool essay = false)
    {
        if (!essay && FeatureCatalog.ByKey(Feature)?.Container != FeatureCatalog.FeaturesContainer) return $"Fonctionnalité inconnue : {Feature}.";
        if (!LanguageModelCall.Outcomes.Contains(Outcome)) return $"Issue inconnue : {Outcome}. Valeurs : {string.Join(", ", LanguageModelCall.Outcomes)}.";
        if (string.IsNullOrWhiteSpace(Task) || Task.Length > 50 || (PromptVersion?.Length ?? 0) > 20 || (Model?.Length ?? 0) > 100) return "Tâche, version ou modèle hors bornes.";
        if (InputHash is null || !System.Text.RegularExpressions.Regex.IsMatch(InputHash, "^[0-9a-f]{64}$")) return "Empreinte SHA-256 attendue, en hexadécimal minuscule.";
        if (DurationMs < 0 || Attempts < 0 || Rejected < 0) return "Durée, essais et réponses écartées positifs.";
        return null;
    }

    public LanguageModelCall ToEntity(string feature) => new()
    {
        At = DateTime.UtcNow, Feature = feature, Task = Task!, Model = Model ?? "", Location = Location is { Length: <= 20 } l ? l : "",
        PromptVersion = PromptVersion ?? "", InputHash = InputHash!, DurationMs = DurationMs, Outcome = Outcome!, Attempts = Attempts, Rejected = Rejected,
        Error = Error is null ? null : Error.Length > 500 ? Error[..500] : Error,
    };
}

/// <summary>OPT-26 : résultat de l'essai, sans secret ; <c>Outcome</c> et <c>DurationMs</c> quand le conteneur a répondu.</summary>
public record LanguageModelTestResult(bool Ok, string Message, string? Outcome = null, int? DurationMs = null);

/// <summary>OPT-27 : compteurs tirés des traces, par fonctionnalité et issue, sur une période.</summary>
public record LanguageModelUsageRow(string Feature, string Outcome, int Count, int AverageMs, int Rejected, DateTime Last);
public record LanguageModelUsage(int Days, List<LanguageModelUsageRow> Rows);

/// <summary>
/// OPT-22 : une adresse se résout ; une seule adresse hors des plages privées (RFC 1918, RFC 4193), de bouclage et
/// locales au lien la fait « hors du SI ».
/// </summary>
public static class LanguageModelLocation
{
    public static bool IsInternal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC || ip.IsIPv6LinkLocal;   // fc00::/7 (RFC 4193), fe80::/10
        }
        return false;
    }

    /// <summary>Adresses de l'hôte, et la première qui sort du SI ; message d'erreur si l'hôte ne se résout pas.</summary>
    public static async Task<(IPAddress? Outside, string? Error)> ResolveAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (SocketException) { return (null, $"Adresse introuvable : {uri.Host} ne se résout pas."); }
        if (addresses.Length == 0) return (null, $"Adresse introuvable : {uri.Host} ne se résout pas.");
        return (addresses.FirstOrDefault(a => !IsInternal(a)), null);
    }
}

/// <summary>
/// Administration › Fonctionnalités optionnelles › Modèle de langage (OPT-20, OPT-22, OPT-23). Chaque réglage changé
/// est tracé au journal d'audit, la clé seulement « modifiée » ou « effacée » (OPT-07).
/// </summary>
[ApiController]
[Route("api/admin/settings/language-model")]
[Authorize(Policy = "Admin")]
public class LanguageModelController(AppDbContext db, CredentialVault vault, FeaturesClient features) : ControllerBase
{
    public const string AuditReference = "Modèle de langage";

    [HttpGet]
    public async Task<LanguageModelView> Get() =>
        LanguageModelView.From(await db.LanguageModelSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LanguageModelSettings());

    [HttpPut]
    public async Task<IActionResult> Put(LanguageModelDto dto)
    {
        var location = dto.Location ?? LanguageModelSettings.InSi;
        if (!LanguageModelSettings.Locations.Contains(location))
            return BadRequest(new { message = $"Emplacement inconnu : « {location} ». Valeurs : {string.Join(", ", LanguageModelSettings.Locations)}." });
        var url = dto.Url?.Trim().TrimEnd('/') ?? "";
        var model = dto.Model?.Trim() ?? "";
        if (dto.TimeoutSeconds is < 10 or > 300) return BadRequest(new { message = "Délai d'une requête : de 10 à 300 secondes." });
        if (dto.MaxResponseKb is < 1 or > 1024) return BadRequest(new { message = "Taille maximale d'une réponse : de 1 à 1 024 Ko." });
        if (dto.Enabled && (url == "" || model == "")) return BadRequest(new { message = "Adresse et nom du modèle requis pour l'activer." });
        if (url != "")
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Query != "" || uri.UserInfo != ""
                || !uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.Ordinal))
                return BadRequest(new { message = "Adresse invalide : l'API du modèle en http(s), terminée par /v1 (par exemple http://modele.interne:11434/v1)." });
            // OPT-22 : l'emplacement déclaré doit couvrir l'adresse résolue.
            var (outside, error) = await LanguageModelLocation.ResolveAsync(uri, HttpContext.RequestAborted);
            if (error is not null) return BadRequest(new { message = error });
            if (outside is not null && location == LanguageModelSettings.InSi)
                return BadRequest(new { message = $"{uri.Host} se résout hors du SI ({outside}) : déclarer le modèle « {LanguageModelSettings.Hosted} »." });
        }
        if (!string.IsNullOrEmpty(dto.Key) && !vault.Configured) return BadRequest(new { message = CredentialVault.NotConfigured });

        var s = await db.LanguageModelSettings.FirstOrDefaultAsync();
        if (s is null) db.LanguageModelSettings.Add(s = new LanguageModelSettings());
        var hosted = location == LanguageModelSettings.Hosted;
        // OPT-22 : déclarer un modèle hébergé demande une confirmation explicite, à chaque passage vers « hébergé ».
        if (hosted && s.Location != LanguageModelSettings.Hosted && !dto.ConfirmHosted)
            return BadRequest(new { message = "Confirmer que les données envoyées au modèle quittent le SI (et l'inscrire au registre des traitements)." });
        var key = dto.Key is null ? s.EncryptedKey : dto.Key == "" ? null : vault.Protect(dto.Key);
        var me = User.Identity?.Name ?? "";
        var display = User.FindFirst("displayName")?.Value ?? me;
        string Yes(bool v) => v ? "oui" : "non";
        foreach (var (field, old, @new) in new (string, string?, string?)[]
        {
            ("enabled", Yes(s.Enabled), Yes(dto.Enabled)), ("location", s.Location, location), ("url", s.Url, url), ("model", s.Model, model),
            ("timeoutSeconds", s.TimeoutSeconds.ToString(), dto.TimeoutSeconds.ToString()), ("maxResponseKb", s.MaxResponseKb.ToString(), dto.MaxResponseKb.ToString()),
            ("key", null, s.EncryptedKey == key ? null : key is null ? "effacée" : "modifiée"),
        }.Where(c => c.Item2 != c.Item3))
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = FeaturesController.AuditType, EntityId = 1, Reference = AuditReference, Action = AuditEntry.Modification,
                Field = field, OldValue = old, NewValue = @new, Author = me, AuthorDisplayName = display, At = DateTime.UtcNow,
            });
        if (hosted && s.Location != LanguageModelSettings.Hosted) (s.HostedConfirmedAt, s.HostedConfirmedBy) = (DateTime.UtcNow, display);
        if (!hosted) (s.HostedConfirmedAt, s.HostedConfirmedBy) = (null, null);
        (s.Enabled, s.Location, s.Url, s.Model, s.EncryptedKey, s.TimeoutSeconds, s.MaxResponseKb, s.UpdatedAt, s.UpdatedBy) =
            (dto.Enabled, location, url, model, key, dto.TimeoutSeconds, dto.MaxResponseKb, DateTime.UtcNow, display);
        await db.SaveChangesAsync();
        return Ok(LanguageModelView.From(s));
    }

    /// <summary>Réponse du conteneur à l'essai : la trace de l'appel (OPT-27).</summary>
    private record EssayReply(LanguageModelCallInput Call);

    /// <summary>
    /// OPT-26 : essai avec les réglages enregistrés, même désactivés, la CMDB assistée coupée comprise : une requête courte
    /// passe par le conteneur <c>features</c> (<c>POST /language-model/test</c>), qui pseudonymise, appelle et valide comme
    /// pour une tâche. Le résultat se lit sans secret, comme ADM-63 ; l'essai est tracé (OPT-27).
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test()
    {
        var s = await db.LanguageModelSettings.AsNoTracking().FirstOrDefaultAsync();
        if (s is null || s.Url == "" || s.Model == "") return BadRequest(new { message = "Enregistrer d'abord l'adresse et le nom du modèle." });
        if (s.EncryptedKey is not null && !vault.Configured) return BadRequest(new { message = CredentialVault.NotConfigured });
        var settings = new { s.Enabled, s.Location, s.Url, s.Model, Key = s.EncryptedKey is null ? null : vault.Unprotect(s.EncryptedKey), s.TimeoutSeconds, s.MaxResponseKb };
        // Deux essais au plus (OPT-25), chacun borné par le délai réglé.
        var (reply, failure) = await features.EssayAsync<EssayReply>("/language-model/test", settings,
            TimeSpan.FromSeconds(s.TimeoutSeconds * 2 + 10), HttpContext.RequestAborted);
        if (reply?.Call is not { } call || call.Invalid(essay: true) is not null)
            return Ok(new LanguageModelTestResult(false, $"Conteneur features injoignable ou hors contrat : {failure ?? "réponse illisible"}."));
        db.LanguageModelCalls.Add(call.ToEntity(LanguageModelCall.EssayFeature));
        await db.SaveChangesAsync();
        var message = call.Outcome switch
        {
            LanguageModelCall.Valid => $"Modèle {s.Model} joignable : réponse JSON valide en {call.DurationMs} ms.",
            LanguageModelCall.NonConforming => $"Modèle joignable, mais sa réponse n'est pas conforme au schéma de l'essai : {call.Error}",
            LanguageModelCall.TimedOut => $"Pas de réponse du modèle en {s.TimeoutSeconds} s.",
            _ => $"Échec : {call.Error}",
        };
        return Ok(new LanguageModelTestResult(call.Outcome == LanguageModelCall.Valid, message, call.Outcome, call.DurationMs));
    }

    /// <summary>OPT-27 : compteurs des appels, par fonctionnalité et issue, sur les <paramref name="days"/> derniers jours (1 à 90).</summary>
    [HttpGet("usage")]
    public async Task<LanguageModelUsage> Usage(int days = 30)
    {
        days = Math.Clamp(days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);
        var rows = await db.LanguageModelCalls.AsNoTracking().Where(c => c.At >= since)
            .GroupBy(c => new { c.Feature, c.Outcome })
            .Select(g => new LanguageModelUsageRow(g.Key.Feature, g.Key.Outcome, g.Count(), (int)g.Average(c => c.DurationMs), g.Sum(c => c.Rejected), g.Max(c => c.At)))
            .ToListAsync();
        return new LanguageModelUsage(days, rows.OrderBy(r => r.Feature).ThenBy(r => Array.IndexOf(LanguageModelCall.Outcomes, r.Outcome)).ToList());
    }
}

/// <summary>
/// OPT-23 : le conteneur <c>features</c> lit les réglages, clé comprise, sur le port interne, une fonctionnalité qui s'en
/// sert activée (403 sinon). Il ne l'écrit nulle part. Les réglages enregistrés mais désactivés se lisent aussi : l'essai
/// (OPT-26) s'en sert.
/// </summary>
[ApiController]
[Route("internal/language-model")]
[Authorize(Policy = ContainerTokenHandler.Policy)]
[InternalFeature(FeatureCatalog.AssistedCmdb)]
public class InternalLanguageModelController(AppDbContext db, CredentialVault vault) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var s = await db.LanguageModelSettings.AsNoTracking().FirstOrDefaultAsync() ?? new LanguageModelSettings();
        return Ok(new { s.Enabled, s.Location, s.Url, s.Model, Key = s.EncryptedKey is null ? null : vault.Unprotect(s.EncryptedKey), s.TimeoutSeconds, s.MaxResponseKb });
    }

    /// <summary>OPT-27 : trace d'un appel, remise par le conteneur après chaque tâche ; 400 hors bornes.</summary>
    [HttpPost("calls")]
    public async Task<IActionResult> Trace(LanguageModelCallInput call)
    {
        if (call.Invalid() is { } refused) return BadRequest(new { message = refused });
        var entity = call.ToEntity(call.Feature!);
        db.LanguageModelCalls.Add(entity);
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { id = entity.Id });
    }
}
