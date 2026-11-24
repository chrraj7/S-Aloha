using System.Text.Json;
using System.Text.Json.Nodes;

namespace SAloha.Mcp;

/// <summary>
/// Mise en forme d'une réponse de l'API pour le client MCP. MCP-07 : dans chaque objet, les valeurs texte saisies
/// par des utilisateurs (titres, descriptions, commentaires, motifs, noms) passent sous la clé <c>userEntered</c>,
/// que les consignes du serveur désignent comme des données ; seuls les champs de structure (référence, statut, type,
/// dates, identifiants, valeurs d'une liste fixe) restent à côté. Un champ inconnu est traité comme saisi.
/// MCP-08 : listes coupées à <see cref="McpLimits.MaxResults"/> éléments, textes à <see cref="McpLimits.MaxTextLength"/>
/// caractères ; <see cref="Truncated"/> le dit.
/// </summary>
public sealed class Shaper(McpLimits limits)
{
    public const string UserEntered = "userEntered";

    private static readonly HashSet<string> Structural = new(StringComparer.OrdinalIgnoreCase)
    {
        "reference", "status", "type", "targetType", "ownerType", "visibility", "priority", "impact", "urgency",
        "criticality", "environment", "ciType", "role", "channel", "relation", "field", "feature", "basis", "state",
        "username", "author", "createdBy", "updatedBy", "category", "changeType", "risk",
    };

    /// <summary>Vrai dès qu'une liste ou un texte a été coupé.</summary>
    public bool Truncated { get; private set; }

    public JsonNode? Shape(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => ShapeObject(o),
        JsonArray a => ShapeArray(a),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => JsonValue.Create(Cut(v.GetValue<string>())),
        _ => node.DeepClone(),
    };

    /// <summary>Champ de structure : jamais saisi librement (dates, identifiants, valeurs d'une liste fixe).</summary>
    public static bool IsStructural(string key) =>
        Structural.Contains(key) || key.EndsWith("At", StringComparison.Ordinal) || key.EndsWith("Date", StringComparison.Ordinal)
        || key.EndsWith("Id", StringComparison.Ordinal);

    private JsonObject ShapeObject(JsonObject source)
    {
        var shaped = new JsonObject();
        var entered = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (value is JsonValue v && v.GetValueKind() == JsonValueKind.String && !IsStructural(key))
                entered[key] = JsonValue.Create(Cut(v.GetValue<string>()));
            else
                shaped[key] = Shape(value);
        }
        if (entered.Count > 0) shaped[UserEntered] = entered;
        return shaped;
    }

    private JsonArray ShapeArray(JsonArray source)
    {
        if (source.Count > limits.MaxResults) Truncated = true;
        return new JsonArray(source.Take(Math.Max(1, limits.MaxResults)).Select(Shape).ToArray());
    }

    private string Cut(string text)
    {
        var max = Math.Max(1, limits.MaxTextLength);
        if (text.Length <= max) return text;
        Truncated = true;
        // Une paire de substitution ne se coupe pas en deux.
        if (char.IsHighSurrogate(text[max - 1])) max--;
        return text[..max] + "…";
    }
}
