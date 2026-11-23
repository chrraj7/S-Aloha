namespace SAloha.Api.Core.Audit;

/// <summary>Ligne du journal d'audit renvoyée par l'API (A2, ADR-0016).</summary>
public record AuditEntryView(long Id, string EntityType, int EntityId, string? Reference, string Action, string? Field,
    string? OldValue, string? NewValue, string? Reason, string Author, string AuthorDisplayName, DateTime At, string Origin)
{
    public static AuditEntryView From(AuditEntry e) => new(e.Id, e.EntityType, e.EntityId, e.Reference, e.Action, e.Field,
        e.OldValue, e.NewValue, e.Reason, e.Author, e.AuthorDisplayName, e.At, e.Origin);
}
