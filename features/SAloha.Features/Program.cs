using SAloha.Features.Host;

// Conteneur « features » des fonctionnalités optionnelles (docs/reference/fonctionnalites-optionnelles.md).
var builder = WebApplication.CreateBuilder(args);
ContainerHost.Configure(builder);
var app = builder.Build();
ContainerHost.Map(app);
app.Run();

/// <summary>Point d'entrée, visible des tests (WebApplicationFactory).</summary>
public partial class Program;
