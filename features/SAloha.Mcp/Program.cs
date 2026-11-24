namespace SAloha.Mcp;

/// <summary>
/// Conteneur « mcp » : le serveur MCP de S-Aloha (docs/reference/serveur-mcp.md, ADR-0024). Sans instructions de
/// premier niveau : la classe a son espace de noms, à côté du <c>Program</c> du conteneur « features » dans les tests.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        McpHost.Configure(builder);
        var app = builder.Build();
        McpHost.Map(app);
        app.Run();
    }
}
