using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SAloha.Api.Core.Data;
using Testcontainers.PostgreSql;

namespace SAloha.Api.Tests;

/// <summary>
/// Mise à niveau d'une base créée par l'ancien EnsureCreated (sans historique
/// de migrations) : l'API démarre, crée les tables manquantes, garde les données (S4).
/// </summary>
public class MigrationTests
{
    private static async Task<(PostgreSqlContainer Db, WebApplicationFactory<Program> Api)> StartOnLegacyDatabase(bool withItilModules)
    {
        var container = new PostgreSqlBuilder().WithImage("pgvector/pgvector:0.8.7-pg18").Build();
        await container.StartAsync();

        // Base « comme avant » : schéma appliqué, puis historique supprimé —
        // exactement ce que laissait EnsureCreated.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(container.GetConnectionString()).Options;
        await using (var db = new AppDbContext(options))
        {
            var target = db.Database.GetMigrations().First(m => m.EndsWith(withItilModules ? "_ItilModules" : "_Initial"));
            await db.GetService<IMigrator>().MigrateAsync(target);
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\"");
            // Ligne écrite en SQL avec les seules colonnes de l'ancien schéma : le
            // modèle EF courant a des colonnes que cette base n'a pas encore.
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Problems\" (\"Reference\", \"Title\", \"Description\", \"Status\", \"Impact\", \"Urgency\", " +
                "\"Priority\", \"Category\", \"AffectedService\", \"CreatedBy\", \"CreatedByDisplayName\", \"CreatedAt\", \"UpdatedAt\") " +
                "VALUES ('PRB-2025-0001', 'Problème d''avant la mise à niveau', '', 'Nouveau', 'Moyen', 'Moyenne', 'P3', '', '', 'legacy', 'legacy', now(), now())");
        }

        var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", container.GetConnectionString())
            .UseSetting("Jwt:Key", ApiFixture.JwtKey)
            .UseSetting("Jwt:Issuer", "S-Aloha"));
        return (container, api);
    }

    [Theory]
    [InlineData(false)]  // installation antérieure aux modules ITIL
    [InlineData(true)]   // installation des modules ITIL avant les migrations
    public async Task Une_base_creee_par_EnsureCreated_est_mise_a_niveau_sans_perte(bool withItilModules)
    {
        var (container, api) = await StartOnLegacyDatabase(withItilModules);
        try
        {
            var client = api.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token("hery.rakoto", "Admin"));

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/incidents")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/incidents",
                new { title = "Après mise à niveau", impact = "Moyen", urgency = "Moyenne" })).StatusCode);
            var problems = await client.GetStringAsync("/api/problems?q=avant la mise");
            Assert.Contains("PRB-2025-0001", problems);

            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(container.GetConnectionString()).Options);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            // PER-10 : l'existant est rattaché à « Organisation », la séquence repart après lui.
            Assert.Equal(1, (await db.Problems.SingleAsync(x => x.Reference == "PRB-2025-0001")).PerimeterId);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/perimeters",
                new { name = "Après reprise" })).StatusCode);
        }
        finally
        {
            await api.DisposeAsync();
            await container.DisposeAsync();
        }
    }
}
