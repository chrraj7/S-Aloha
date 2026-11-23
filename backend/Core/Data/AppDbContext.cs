using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Audit;
using SAloha.Api.Core.Comments;
using SAloha.Api.Core.Notifications;
using SAloha.Api.Core.Links;
using SAloha.Api.Core.Perimeters;
using SAloha.Api.Core.Search;
using SAloha.Api.Modules.ChangeEnablement;
using SAloha.Api.Modules.ComplianceAssessment;
using SAloha.Api.Modules.ContinualImprovement;
using SAloha.Api.Modules.IncidentManagement;
using SAloha.Api.Modules.KnowledgeManagement;
using SAloha.Api.Modules.ProblemManagement;
using SAloha.Api.Modules.ProjectManagement;
using SAloha.Api.Modules.ServiceConfigurationManagement;
using SAloha.Api.Modules.ServiceLevelManagement;
using SAloha.Api.Modules.ServiceRequestManagement;

namespace SAloha.Api.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Socle
    public DbSet<ItemLink> ItemLinks => Set<ItemLink>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<SearchPassage> SearchPassages => Set<SearchPassage>();
    public DbSet<Perimeter> Perimeters => Set<Perimeter>();
    public DbSet<PerimeterAssignment> PerimeterAssignments => Set<PerimeterAssignment>();
    public DbSet<Administration.LastLogin> LastLogins => Set<Administration.LastLogin>();
    public DbSet<Administration.DuplicateDismissal> DuplicateDismissals => Set<Administration.DuplicateDismissal>();
    public DbSet<Platform.MailSettings> MailSettings => Set<Platform.MailSettings>();
    public DbSet<Notifications.MailPreference> MailPreferences => Set<Notifications.MailPreference>();
    public DbSet<Attachments.Attachment> Attachments => Set<Attachments.Attachment>();
    public DbSet<Platform.StorageSettings> StorageSettings => Set<Platform.StorageSettings>();
    public DbSet<Features.OptionalFeature> OptionalFeatures => Set<Features.OptionalFeature>();
    public DbSet<Features.FeatureEvent> FeatureEvents => Set<Features.FeatureEvent>();
    public DbSet<Itil.Category> Categories => Set<Itil.Category>();
    // Gestion des problèmes
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<RcaAnalysis> Analyses => Set<RcaAnalysis>();
    public DbSet<CorrectiveAction> Actions => Set<CorrectiveAction>();
    public DbSet<RaciAssignment> RaciAssignments => Set<RaciAssignment>();
    // Gestion des incidents, des demandes, habilitation des changements
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<Change> Changes => Set<Change>();
    // Configuration des services
    public DbSet<ConfigurationItem> ConfigurationItems => Set<ConfigurationItem>();
    public DbSet<CiRelationship> CiRelationships => Set<CiRelationship>();
    // Découverte automatique et sas de staging (CFG-40 à CFG-47)
    public DbSet<DiscoveredAsset> DiscoveredAssets => Set<DiscoveredAsset>();
    public DbSet<DiscoveryJob> DiscoveryJobs => Set<DiscoveryJob>();
    public DbSet<DiscoveryCredential> DiscoveryCredentials => Set<DiscoveryCredential>();
    public DbSet<EndOfLifeRule> EndOfLifeRules => Set<EndOfLifeRule>();
    public DbSet<DiscoveredSoftware> DiscoveredSoftware => Set<DiscoveredSoftware>();
    public DbSet<SshHostKey> SshHostKeys => Set<SshHostKey>();
    public DbSet<RelationProposal> RelationProposals => Set<RelationProposal>();
    public DbSet<Modules.ServiceConfigurationManagement.Discovery.MacVendor> MacVendors => Set<Modules.ServiceConfigurationManagement.Discovery.MacVendor>();
    // Niveaux de service
    public DbSet<ItService> Services => Set<ItService>();
    public DbSet<ServiceLevelAgreement> Agreements => Set<ServiceLevelAgreement>();
    // Connaissances, amélioration continue
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<KnowledgeArticleVersion> KnowledgeArticleVersions => Set<KnowledgeArticleVersion>();
    public DbSet<KnowledgeFeedback> KnowledgeFeedbacks => Set<KnowledgeFeedback>();
    public DbSet<Improvement> Improvements => Set<Improvement>();
    public DbSet<Modules.RegulatoryNotifications.RegulatoryNotification> RegulatoryNotifications => Set<Modules.RegulatoryNotifications.RegulatoryNotification>();
    // Conformité NIS 2
    public DbSet<SecurityObjective> SecurityObjectives => Set<SecurityObjective>();
    public DbSet<SecurityRequirement> SecurityRequirements => Set<SecurityRequirement>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentResponse> AssessmentResponses => Set<AssessmentResponse>();
    public DbSet<AssessmentEvidence> AssessmentEvidences => Set<AssessmentEvidence>();
    public DbSet<AgreementReview> AgreementReviews => Set<AgreementReview>();
    public DbSet<RequestCatalogItem> RequestCatalogItems => Set<RequestCatalogItem>();
    public DbSet<RequestCatalogTask> RequestCatalogTasks => Set<RequestCatalogTask>();
    public DbSet<RequestTask> RequestTasks => Set<RequestTask>();
    public DbSet<ChangeApproval> ChangeApprovals => Set<ChangeApproval>();
    public DbSet<ChangeFreeze> ChangeFreezes => Set<ChangeFreeze>();
    public DbSet<StandardChangeModel> StandardChangeModels => Set<StandardChangeModel>();
    // Gestion de projet
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectPhase> ProjectPhases => Set<ProjectPhase>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<Deliverable> Deliverables => Set<Deliverable>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<ProjectRisk> ProjectRisks => Set<ProjectRisk>();
    public DbSet<ProjectIssue> ProjectIssues => Set<ProjectIssue>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Problem>().HasIndex(p => p.Reference).IsUnique();
        b.Entity<Problem>().HasMany(p => p.Analyses).WithOne(a => a.Problem!)
            .HasForeignKey(a => a.ProblemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Problem>().HasMany(p => p.Actions).WithOne(a => a.Problem!)
            .HasForeignKey(a => a.ProblemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CorrectiveAction>().HasMany(a => a.Raci).WithOne(r => r.Action!)
            .HasForeignKey(r => r.CorrectiveActionId).OnDelete(DeleteBehavior.Cascade);

        // Référence unique : c'est l'index qui départage deux créations simultanées.
        b.Entity<Incident>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<ServiceRequest>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Change>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<ConfigurationItem>().HasIndex(x => x.Reference).IsUnique();
        // CFG-16 : attributs par type, une colonne jsonb.
        b.Entity<ConfigurationItem>().Property(x => x.Attributes).HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, string>>(
                    (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                    v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)));
        b.Entity<ItService>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<ServiceLevelAgreement>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<KnowledgeArticle>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Improvement>().HasIndex(x => x.Reference).IsUnique();
        // DRG-03 : une déclaration par régime et par incident ; supprimer l'incident emporte ses déclarations.
        b.Entity<Modules.RegulatoryNotifications.RegulatoryNotification>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Modules.RegulatoryNotifications.RegulatoryNotification>().HasIndex(x => new { x.IncidentId, x.Regime }).IsUnique();
        b.Entity<Modules.RegulatoryNotifications.RegulatoryNotification>().HasOne(x => x.Incident).WithMany()
            .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assessment>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Project>().HasIndex(x => x.Reference).IsUnique();
        b.Entity<Project>().Property(x => x.Budget).HasPrecision(14, 2);
        b.Entity<Project>().Property(x => x.ActualCost).HasPrecision(14, 2);
        b.Entity<Project>().Property(x => x.ToleranceCostPercent).HasPrecision(6, 2);
        // Éléments d'un projet : supprimés avec lui ; le score d'un risque est calculé.
        ConfigurePart<ProjectPhase>(b); ConfigurePart<Milestone>(b); ConfigurePart<Deliverable>(b);
        ConfigurePart<Sprint>(b); ConfigurePart<WorkItem>(b); ConfigurePart<ProjectRisk>(b); ConfigurePart<ProjectIssue>(b);
        b.Entity<ProjectRisk>().Ignore(r => r.Score);
        b.Entity<WorkItem>().HasIndex(w => w.SprintId);

        // Le numéro de l'objectif (1 à 20) est son identifiant : pas de séquence.
        b.Entity<SecurityObjective>().Property(o => o.Id).ValueGeneratedNever();
        b.Entity<SecurityObjective>().HasMany(o => o.Requirements).WithOne(r => r.Objective!)
            .HasForeignKey(r => r.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SecurityRequirement>().HasIndex(r => r.Code).IsUnique();
        b.Entity<Assessment>().HasMany(a => a.Responses).WithOne(r => r.Assessment!)
            .HasForeignKey(r => r.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AssessmentResponse>().HasOne(r => r.Requirement).WithMany()
            .HasForeignKey(r => r.RequirementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AssessmentResponse>().HasIndex(r => new { r.AssessmentId, r.RequirementId }).IsUnique();
        // NIS-08 : preuves supprimées avec l'évaluation ; une même preuve une fois par exigence.
        b.Entity<AssessmentEvidence>().HasOne(x => x.Assessment).WithMany()
            .HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AssessmentEvidence>().HasIndex(x => new { x.AssessmentId, x.RequirementId, x.TargetType, x.TargetId }).IsUnique();

        b.Entity<ConfigurationItem>().HasMany(c => c.Outgoing).WithOne(r => r.Source!)
            .HasForeignKey(r => r.SourceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ConfigurationItem>().HasMany(c => c.Incoming).WithOne(r => r.Target!)
            .HasForeignKey(r => r.TargetId).OnDelete(DeleteBehavior.Cascade);
        // Identifiants de rapprochement (CFG-41) et unicités contrôlées par l'API (CFG-43, CFG-44).
        b.Entity<ConfigurationItem>().HasIndex(c => c.SerialNumber);
        b.Entity<ConfigurationItem>().HasIndex(c => c.MacAddress);
        b.Entity<ConfigurationItem>().HasIndex(c => c.AppCode);
        // Sas de staging : un CI supprimé rend ses actifs au rapprochement ; un identifiant
        // utilisé par une tâche ne se supprime pas (l'API le refuse avant, 409).
        b.Entity<DiscoveredAsset>().HasOne(a => a.MatchedCi).WithMany().HasForeignKey(a => a.MatchedCiId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<DiscoveredAsset>().HasOne<DiscoveryJob>().WithMany().HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<DiscoveredAsset>().HasIndex(a => new { a.PerimeterId, a.Status });
        b.Entity<DiscoveredAsset>().Ignore(a => a.Unknown);
        b.Entity<DiscoveryJob>().HasOne(j => j.Credential).WithMany().HasForeignKey(j => j.CredentialId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EndOfLifeRule>().HasIndex(r => r.Product).IsUnique();
        b.Entity<DiscoveredAsset>().HasMany(a => a.Software).WithOne().HasForeignKey(s => s.AssetId).OnDelete(DeleteBehavior.Cascade);
        // REQ-24 : tâches du modèle et de la demande, supprimées avec eux.
        b.Entity<RequestCatalogItem>().HasMany(x => x.Tasks).WithOne().HasForeignKey(t => t.CatalogItemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RequestTask>().HasOne<ServiceRequest>().WithMany().HasForeignKey(t => t.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SshHostKey>().HasIndex(k => new { k.PerimeterId, k.Protocol, k.Host, k.Port }).IsUnique();
        b.Entity<RelationProposal>().HasOne(r => r.SourceAsset).WithMany().HasForeignKey(r => r.SourceAssetId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RelationProposal>().HasOne(r => r.TargetAsset).WithMany().HasForeignKey(r => r.TargetAssetId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RelationProposal>().HasIndex(r => new { r.SourceAssetId, r.TargetAssetId, r.Type }).IsUnique();
        b.Entity<ItService>().HasMany(s => s.Agreements).WithOne(a => a.Service!)
            .HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ServiceLevelAgreement>().Property(a => a.AvailabilityTarget).HasPrecision(5, 2);
        b.Entity<ServiceLevelAgreement>().Property(a => a.ComplianceTarget).HasPrecision(5, 2);
        // SLM-10 : service du catalogue ; supprimer un service garde le libellé recopié.
        b.Entity<Incident>().HasOne<ItService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);
        // KB-15, KB-13 : versions publiées et votes, supprimés avec l'article ; un vote par lecteur.
        b.Entity<KnowledgeArticleVersion>().HasOne<KnowledgeArticle>().WithMany().HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<KnowledgeArticleVersion>().HasIndex(v => new { v.ArticleId, v.Version }).IsUnique();
        b.Entity<KnowledgeFeedback>().HasOne<KnowledgeArticle>().WithMany().HasForeignKey(f => f.ArticleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<KnowledgeFeedback>().HasIndex(f => new { f.ArticleId, f.Username }).IsUnique();
        // INC-16 : incident parent ; supprimer le parent détache ses enfants.
        b.Entity<Incident>().HasOne<Incident>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Problem>().HasOne<ItService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<ServiceRequest>().HasOne<ItService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);
        // REQ-20 : modèle de la demande ; un service supprimé ne supprime pas le modèle.
        b.Entity<ServiceRequest>().HasOne<RequestCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        // CHG-20, CHG-26 : décisions du CAB supprimées avec le changement ; un modèle utilisé ne se supprime pas.
        b.Entity<ChangeApproval>().HasOne<Change>().WithMany().HasForeignKey(x => x.ChangeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChangeApproval>().HasIndex(x => new { x.ChangeId, x.Approver }).IsUnique();
        b.Entity<Change>().HasOne<StandardChangeModel>().WithMany().HasForeignKey(x => x.StandardModelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RequestCatalogItem>().HasOne<ItService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);

        // Index de recherche (ADR-0013) : plein texte « french » généré par
        // PostgreSQL sur le titre et le passage, index GIN.
        // pgvector (ADR-0022) : la similarité des vecteurs est calculée dans PostgreSQL.
        b.HasPostgresExtension("vector");
        b.Entity<SearchPassage>()
            .HasGeneratedTsVectorColumn(p => p.SearchVector, "french", p => new { p.Title, p.Text })
            .HasIndex(p => p.SearchVector).HasMethod("GIN");
        b.Entity<SearchPassage>().HasIndex(p => new { p.SourceType, p.SourceId });

        // Périmètres (PER-01, PER-02, PER-10) : « Organisation » existe toujours et reçoit
        // les enregistrements antérieurs ; chaque enregistrement le référence (suppression
        // d'un périmètre rattaché refusée).
        b.Entity<Perimeter>().HasIndex(p => p.Name).IsUnique();
        b.Entity<Perimeter>().HasData(new Perimeter
        {
            Id = Perimeter.DefaultId, Name = Perimeter.DefaultName,
            Description = "Périmètre par défaut : toute l'organisation.",
            CreatedAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "système"
        });
        b.Entity<PerimeterAssignment>().HasOne(a => a.Perimeter).WithMany()
            .HasForeignKey(a => a.PerimeterId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PerimeterAssignment>().HasIndex(a => new { a.PerimeterId, a.PrincipalType, a.PrincipalId, a.Role }).IsUnique();
        foreach (var type in b.Model.GetEntityTypes().Select(t => t.ClrType)
                     .Where(t => typeof(IHasPerimeter).IsAssignableFrom(t)).ToList())
        {
            b.Entity(type).HasOne(typeof(Perimeter)).WithMany().HasForeignKey(nameof(IHasPerimeter.PerimeterId))
                .OnDelete(DeleteBehavior.Restrict);
            b.Entity(type).HasIndex(nameof(IHasPerimeter.PerimeterId));
        }

        b.Entity<Administration.DuplicateDismissal>().HasIndex(d => new { d.Type, d.FirstId, d.SecondId }).IsUnique();
        // SOC-25 : pièces jointes d'un enregistrement.
        b.Entity<Attachments.Attachment>().HasIndex(a => new { a.TargetType, a.TargetId });
        // ADM-60, ADM-61 : une seule ligne de réglages chacun, saisie par l'Admin.
        b.Entity<Platform.MailSettings>().Property(s => s.Id).ValueGeneratedNever();
        b.Entity<Platform.StorageSettings>().Property(s => s.Id).ValueGeneratedNever();
        b.Entity<Features.FeatureEvent>().HasIndex(e => e.At);   // OPT-12 : purge par date
        b.Entity<Itil.Category>().HasIndex(c => new { c.Practice, c.Name }).IsUnique();
        b.Entity<ItemLink>().HasIndex(l => new { l.FromType, l.FromId });
        b.Entity<ItemLink>().HasIndex(l => new { l.ToType, l.ToId });
        b.Entity<AuditEntry>().HasIndex(a => new { a.EntityType, a.EntityId });
        b.Entity<Comment>().HasIndex(c => new { c.TargetType, c.TargetId });
        b.Entity<Notification>().HasIndex(n => new { n.RecipientType, n.RecipientId, n.CreatedAt });
        b.Entity<Notification>().HasIndex(n => n.CreatedAt);
        b.Entity<NotificationRead>().HasKey(r => new { r.NotificationId, r.User });
        b.Entity<AuditEntry>().HasIndex(a => a.At);
    }

    private static void ConfigurePart<T>(ModelBuilder b) where T : ProjectPart =>
        b.Entity<T>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
}
