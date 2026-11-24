using System.Text.Json;

namespace SAloha.Features.LanguageModel;

/// <summary>
/// OPT-26 : l'essai de la page « Modèle de langage », une requête courte qui passe par le même chemin qu'une tâche :
/// pseudonymisation, appel, schéma, jetons. Le modèle doit recopier le jeton du nom d'hôte reçu ; rétabli, il redonne
/// le nom d'origine. Tracé par l'API sous <c>language-model</c> / <c>essai</c> (OPT-27).
/// </summary>
public static class LanguageModelEssay
{
    public const string Feature = "language-model", Task = "essai", PromptVersion = "1", Host = "srv-essai.exemple.local";

    public static readonly JsonElement Schema = JsonDocument.Parse("""
        { "type": "object", "required": ["etat", "hote"], "additionalProperties": false,
          "properties": { "etat": { "type": "string", "enum": ["pret"] }, "hote": { "type": "string", "maxLength": 100 } } }
        """).RootElement;

    public static LanguageModelTask Build() => new(Feature, Task, PromptVersion,
        "Essai de connexion de S-Aloha. Réponds « pret » dans « etat », et recopie dans « hote » le nom d'hôte du message, tel quel.",
        $"Essai de connexion depuis {Host}.", Schema,
        output => output.GetProperty("hote").GetString() == Host ? null : "Le modèle n'a pas recopié le jeton du nom d'hôte (OPT-24).");
}
