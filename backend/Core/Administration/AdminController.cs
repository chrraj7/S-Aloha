using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Auth;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Search;
using SAloha.Api.Modules.ServiceConfigurationManagement;
using SAloha.Api.Modules.ServiceConfigurationManagement.Discovery;

namespace SAloha.Api.Core.Administration;

/// <summary>Administration (SOC-26, Admin) : santé de la plateforme et journal d'audit global.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public class AdminController(AppDbContext db, LdapService directory, IDirectoryRoster roster, IEmbeddingClient embeddings, CredentialVault vault,
    Heartbeats heartbeats, IConfiguration config, IHostEnvironment env) : ControllerBase
{
    public const string Healthy = "OK", Degraded = "Dégradé", Down = "Hors service";
    public record Component(string Name, string State, string Message, double DurationMs);

    private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private static async Task<Component> CheckAsync(string name, Func<Task<(string State, string Message)>> check)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var (state, message) = await check();
            return new Component(name, state, message, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            // Le message d'une exception d'infrastructure ne contient ni secret ni chaîne de connexion
            // (Npgsql et le client LDAP les masquent) ; on n'en garde que la première ligne.
            return new Component(name, Down, ex.Message.Split('\n')[0], Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>ADM-20, ADM-21 : état des composants et version déployée.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var components = new List<Component>
        {
            await CheckAsync("Base de données", async () =>
            {
                if (!await db.Database.CanConnectAsync()) return (Down, "Connexion impossible.");
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                return pending.Count == 0 ? (Healthy, "Connectée, schéma à jour.")
                    : (Degraded, $"{pending.Count} migration(s) en attente : {string.Join(", ", pending)}.");
            }),
            await CheckAsync("Annuaire", async () =>
            {
                await directory.ProbeAsync();
                return (Healthy, "Compte de service accepté.");
            }),
            await CheckAsync("Embeddings (recherche sémantique)", async () =>
                !embeddings.Enabled ? (Healthy, "Non configuré : recherche par les mots seulement.")
                : await embeddings.EmbedAsync(["test"], TimeSpan.FromSeconds(5)) is not null ? (Healthy, $"Modèle {embeddings.Model} joignable.")
                : (Degraded, $"Modèle {embeddings.Model} injoignable : recherche par les mots seulement.")),
            await CheckAsync("Coffre de la découverte", async () =>
                vault.Configured ? (Healthy, "Clé configurée.")
                : await db.DiscoveryCredentials.AnyAsync() ? (Down, "Clé absente alors que des identifiants sont enregistrés : ils sont illisibles.")
                : (Degraded, "Clé non configurée : aucun identifiant ne peut être enregistré.")),
            // ADM-60, ADM-61 : réglés dans la console ; l'essai de connexion est sur la page Messagerie et stockage.
            await CheckAsync("Messagerie (SMTP)", async () => await db.MailSettings.AsNoTracking().FirstOrDefaultAsync() switch
            {
                { Enabled: true, EncryptedPassword: not null } when !vault.Configured => (Down, "Clé du coffre absente : le mot de passe SMTP est illisible."),
                { Enabled: true } m when Platform.MailSender.Ready(m) => (Healthy, $"Activée : {m.Host}:{m.Port}, expéditeur {m.FromAddress}."),
                _ => (Healthy, "Non activée : notifications dans l'application seulement."),
            }),
            await CheckAsync("Stockage des pièces jointes (S3)", async () => await db.StorageSettings.AsNoTracking().FirstOrDefaultAsync() switch
            {
                { Enabled: true } s when Platform.ObjectStorage.Ready(s) && !vault.Configured => (Down, "Clé du coffre absente : la clé secrète est illisible."),
                { Enabled: true } s when Platform.ObjectStorage.Ready(s) => (Healthy, $"Activé : {s.Endpoint}, bucket {s.Bucket}."),
                _ => (Healthy, "Non activé : pas de pièces jointes."),
            }),
            // O1 : rattrapage des échéances SLA, une fois après le démarrage.
            heartbeats.Last(Modules.IncidentManagement.SlaBackfill.Task) switch
            {
                null => new Component("Rattrapage des échéances SLA", Healthy, "En cours depuis le démarrage.", 0),
                { Ok: false } b => new Component("Rattrapage des échéances SLA", Degraded, $"En échec : {b.Message}", 0),
                var b => new Component("Rattrapage des échéances SLA", Healthy, $"Fait le {b.At:dd/MM/yyyy HH:mm} UTC : {b.Message}", 0),
            },
            Background("Notifications datées", "notifications", TimeSpan.FromMinutes(3 * config.GetValue("Notifications:IntervalMinutes", 15)),
                config.GetValue("Notifications:IntervalMinutes", 15) <= 0),
            Background("Planificateur de la découverte", "discovery", TimeSpan.FromMinutes(3 * config.GetValue("Discovery:IntervalMinutes", 5)),
                config.GetValue("Discovery:IntervalMinutes", 5) <= 0),
            await CheckAsync("Tâches de découverte", async () =>
            {
                var failed = await db.DiscoveryJobs.AsNoTracking().Where(j => j.IsActive && j.LastStatus == DiscoveryJob.Failed)
                    .Select(j => j.Name).ToListAsync();
                return failed.Count == 0 ? (Healthy, "Aucune tâche active en échec.")
                    : (Degraded, $"En échec : {string.Join(", ", failed.Select(n => $"« {n} »"))}.");
            }),
        };
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var (version, commit) = informational.Split('+') is var parts ? (parts[0], parts.Length > 1 ? parts[1] : null) : ("", null);
        return Ok(new
        {
            State = components.Any(c => c.State == Down) ? Down : components.Any(c => c.State == Degraded) ? Degraded : Healthy,
            Components = components,
            Version = version, Commit = commit is { Length: > 12 } ? commit[..12] : commit,
            StartedAt, Environment = env.EnvironmentName, CheckedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Tâche de fond : dernier passage récent, ou désactivée par la configuration.</summary>
    private Component Background(string name, string task, TimeSpan late, bool disabled)
    {
        if (disabled) return new Component(name, Healthy, "Désactivée par la configuration.", 0);
        var beat = heartbeats.Last(task);
        if (beat is null)
            return DateTime.UtcNow - StartedAt > late
                ? new Component(name, Degraded, "Aucun passage depuis le démarrage.", 0)
                : new Component(name, Healthy, "Pas encore passée depuis le démarrage.", 0);
        var when = $"dernier passage le {beat.At:dd/MM/yyyy HH:mm} UTC";
        if (!beat.Ok) return new Component(name, Degraded, $"En échec ({when}) : {beat.Message}", 0);
        return DateTime.UtcNow - beat.At > late
            ? new Component(name, Degraded, $"En retard ({when}).", 0)
            : new Component(name, Healthy, $"À l'heure ({when}).", 0);
    }

    /// <summary>
    /// ADM-10, ADM-11 : membres des groupes AD de rôle, avec leur rôle effectif par périmètre
    /// (même calcul que les droits, PER-03), leur dernière connexion (ADM-12), et les affectations
    /// de comptes absents des groupes de rôle (« sans accès »).
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? q, [FromQuery] string? role)
    {
        List<RosterEntry> members;
        try { members = await roster.RoleMembersAsync(); }
        catch (DirectoryUnavailableException)
        {
            // ADM-13 : pas de liste partielle.
            return StatusCode(503, new { message = "Annuaire injoignable : la liste des utilisateurs est indisponible pour le moment." });
        }
        var perimeters = await db.Perimeters.AsNoTracking().Where(p => p.DeletedAt == null).ToDictionaryAsync(p => p.Id, p => p.Name);
        var assignments = await db.PerimeterAssignments.AsNoTracking().Where(a => perimeters.Keys.Contains(a.PerimeterId)).ToListAsync();
        var logins = await db.LastLogins.AsNoTracking().ToDictionaryAsync(l => l.Username, l => l.At);
        var users = new List<object>();
        foreach (var m in members)
        {
            if (!string.IsNullOrEmpty(role) && m.Role != role) continue;
            if (!string.IsNullOrWhiteSpace(q) && !m.Username.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                && !m.DisplayName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            var key = m.Username.ToLowerInvariant();
            var groups = m.Groups.Select(g => g.ToLowerInvariant()).ToList();
            var effective = m.Role == "Admin" ? null
                : await Perimeters.PerimeterAccess.RolesAsync(db, key, groups, m.Role == "Manager" ? "Manager" : "User");
            users.Add(new
            {
                m.Username, m.DisplayName, m.Role, m.RoleGroups,
                // Admin : portée globale, jamais affecté (PER-03).
                Perimeters = effective?.Where(p => perimeters.ContainsKey(p.Key)).Select(p => new
                {
                    Id = p.Key, Name = perimeters[p.Key], Role = p.Value,
                    Via = assignments.Where(a => a.PerimeterId == p.Key && (a.PrincipalType == "User"
                            ? a.PrincipalId.ToLower() == key : groups.Contains(a.PrincipalId.ToLower())))
                        .Select(a => a.PrincipalType == "User" ? "affectation directe" : $"groupe {a.PrincipalDisplayName}").Distinct().ToList()
                }).OrderBy(p => p.Name).ToList(),
                LastLoginAt = logins.TryGetValue(key, out var at) ? at : (DateTime?)null
            });
        }
        var known = members.Select(m => m.Username.ToLowerInvariant()).ToHashSet();
        var orphans = assignments.Where(a => a.PrincipalType == "User" && !known.Contains(a.PrincipalId.ToLower()))
            .Select(a => new { a.Id, Perimeter = perimeters[a.PerimeterId], a.PrincipalId, a.PrincipalDisplayName, a.Role }).ToList();
        return Ok(new
        {
            Mapping = new[] { "Admin", "Manager", "User" }.Select(r => new { Role = r, Group = config[$"Ldap:Groups:{r}"] }),
            Users = users, WithoutAccess = orphans
        });
    }

    /// <summary>ADM-40 : doublons probables, avec ce qui serait reporté (liens, commentaires).</summary>
    [HttpGet("duplicates")]
    public async Task<IActionResult> Duplicates([FromServices] Duplicates duplicates)
    {
        var pairs = await duplicates.FindAsync();
        async Task<object> Describe(string type, Duplicates.Side s) => new
        {
            s.Id, s.Reference, s.Title, s.Status, s.CreatedAt,
            Links = await db.ItemLinks.CountAsync(l => (l.FromType == type && l.FromId == s.Id) || (l.ToType == type && l.ToId == s.Id)),
            Comments = await db.Comments.CountAsync(c => c.TargetType == type && c.TargetId == s.Id),
        };
        var result = new List<object>();
        foreach (var p in pairs) result.Add(new { p.Type, First = await Describe(p.Type, p.First), Second = await Describe(p.Type, p.Second) });
        return Ok(result);
    }

    public record MergeDto(string Type, int KeepId, int RemoveId);
    public record DismissDto(string Type, int FirstId, int SecondId);

    /// <summary>ADM-41 : fusion du doublon dans l'enregistrement conservé.</summary>
    [HttpPost("duplicates/merge")]
    public async Task<IActionResult> Merge(MergeDto dto, [FromServices] Duplicates duplicates)
    {
        if (!Administration.Duplicates.Supports(dto.Type)) return BadRequest(new { message = $"Pratique non prise en charge : « {dto.Type} »." });
        var me = User.Identity?.Name ?? "";
        var result = await duplicates.MergeAsync(dto.Type, dto.KeepId, dto.RemoveId, me, User.FindFirst("displayName")?.Value ?? me);
        return result.Error is not null ? BadRequest(new { message = result.Error }) : Ok(new { result.LinksMoved, result.CommentsMoved });
    }

    /// <summary>ADM-42 : « pas un doublon », la paire n'est plus proposée.</summary>
    [HttpPost("duplicates/dismiss")]
    public async Task<IActionResult> Dismiss(DismissDto dto, [FromServices] Duplicates duplicates)
    {
        if (!Administration.Duplicates.Supports(dto.Type)) return BadRequest(new { message = $"Pratique non prise en charge : « {dto.Type} »." });
        await duplicates.DismissAsync(dto.Type, dto.FirstId, dto.SecondId, User.Identity?.Name ?? "");
        return NoContent();
    }

    public record AuditQuery(string? Author, string? Type, string? Action, string? Reference, DateTime? From, DateTime? To);

    private IQueryable<AuditEntry> Filter(AuditQuery f)
    {
        var to = (Records.Dates.Utc(f.To)?.Date ?? DateTime.UtcNow.Date).AddDays(1);
        var from = Records.Dates.Utc(f.From)?.Date ?? to.AddDays(-30);
        var q = db.AuditEntries.AsNoTracking().Where(a => a.At >= from && a.At < to);
        if (!string.IsNullOrWhiteSpace(f.Author))
        {
            var key = f.Author.Trim().ToLower();
            q = q.Where(a => a.Author.ToLower().Contains(key) || a.AuthorDisplayName.ToLower().Contains(key));
        }
        if (!string.IsNullOrWhiteSpace(f.Type)) q = q.Where(a => a.EntityType == f.Type);
        if (!string.IsNullOrWhiteSpace(f.Action)) q = q.Where(a => a.Action == f.Action);
        if (!string.IsNullOrWhiteSpace(f.Reference))
        {
            var key = f.Reference.Trim().ToUpper();
            q = q.Where(a => a.Reference.Contains(key));
        }
        return q.OrderByDescending(a => a.At).ThenByDescending(a => a.Id);
    }

    public const int PageSize = 100, ExportLimit = 10_000;

    /// <summary>ADM-30 : journal d'audit global, filtré, par pages de 100.</summary>
    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] AuditQuery f, [FromQuery] int page = 1)
    {
        var q = Filter(f);
        page = Math.Max(1, page);
        return Ok(new
        {
            Total = await q.CountAsync(), Page = page, PageSize,
            Items = await q.Skip((page - 1) * PageSize).Take(PageSize).Select(a => AuditEntryView.From(a)).ToListAsync()
        });
    }

    /// <summary>ADM-31 : export CSV des lignes filtrées (10 000 au plus), ouvert tel quel par Excel.</summary>
    [HttpGet("audit/export")]
    public async Task<IActionResult> AuditExport([FromQuery] AuditQuery f)
    {
        var rows = await Filter(f).Take(ExportLimit).ToListAsync();
        // Un texte qui commence par = + - @, tabulation ou retour chariot serait évalué comme une formule
        // par le tableur (OWASP « CSV Injection ») : l'apostrophe le garde en texte. Un nombre (« -3,5 ») reste un nombre.
        static string Cell(string? v)
        {
            if (v is null) return "";
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0]) && !Regex.IsMatch(v, @"^[+-]?\d+([.,]\d+)?$")) v = "'" + v;
            return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        }
        var csv = new StringBuilder("Date;Auteur;Type;Référence;Action;Champ;Ancienne valeur;Nouvelle valeur;Motif\r\n");
        foreach (var a in rows)
            csv.Append(string.Join(';', new[]
            {
                a.At.ToString("dd/MM/yyyy HH:mm:ss"), Cell(a.AuthorDisplayName), a.EntityType, Cell(a.Reference), Cell(a.Action),
                Cell(a.Field), Cell(a.OldValue), Cell(a.NewValue), Cell(a.Reason)
            })).Append("\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"s-aloha-journal-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
