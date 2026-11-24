using System.Text.Json;

namespace SAloha.Features.LanguageModel;

/// <summary>
/// OPT-25 : validation d'une sortie du modèle contre le schéma de la tâche, sous-ensemble de JSON Schema que les tâches
/// emploient : <c>type</c> (object, array, string, number, integer, boolean), <c>required</c>, <c>properties</c>,
/// <c>additionalProperties: false</c>, <c>items</c>, <c>enum</c>, <c>maxItems</c>, <c>maxLength</c>. Rend le premier écart, ou null.
/// </summary>
public static class SchemaCheck
{
    public static string? Error(JsonElement value, JsonElement schema, string path = "$")
    {
        if (schema.TryGetProperty("type", out var type) && !Is(value, type.GetString()))
            return $"{path} : {type.GetString()} attendu, {value.ValueKind} reçu.";
        if (schema.TryGetProperty("enum", out var allowed) && !allowed.EnumerateArray().Any(a => a.ToString() == value.ToString()))
            return $"{path} : valeur hors de la liste.";
        if (value.ValueKind == JsonValueKind.String && schema.TryGetProperty("maxLength", out var maxLength) && value.GetString()!.Length > maxLength.GetInt32())
            return $"{path} : plus de {maxLength.GetInt32()} caractères.";
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                    if (!value.TryGetProperty(name, out _)) return $"{path}.{name} : manquant.";
            var props = schema.TryGetProperty("properties", out var p) ? p : default;
            foreach (var prop in value.EnumerateObject())
            {
                if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(prop.Name, out var sub))
                {
                    if (Error(prop.Value, sub, $"{path}.{prop.Name}") is { } e) return e;
                }
                else if (schema.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.False)
                    return $"{path}.{prop.Name} : propriété inattendue.";
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("maxItems", out var maxItems) && value.GetArrayLength() > maxItems.GetInt32())
                return $"{path} : plus de {maxItems.GetInt32()} éléments.";
            if (schema.TryGetProperty("items", out var items))
            {
                var i = 0;
                foreach (var item in value.EnumerateArray())
                    if (Error(item, items, $"{path}[{i++}]") is { } e) return e;
            }
        }
        return null;
    }

    private static bool Is(JsonElement v, string? type) => type switch
    {
        "object" => v.ValueKind == JsonValueKind.Object,
        "array" => v.ValueKind == JsonValueKind.Array,
        "string" => v.ValueKind == JsonValueKind.String,
        "number" => v.ValueKind == JsonValueKind.Number,
        "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
        "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => v.ValueKind == JsonValueKind.Null,
        _ => true,
    };
}
