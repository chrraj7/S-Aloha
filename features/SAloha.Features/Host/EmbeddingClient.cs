using System.Net.Http.Json;

namespace SAloha.Features.Host;

/// <summary>
/// Service d'embeddings du compose (<c>Embeddings:Url</c>, modèle <c>Embeddings:Model</c>, bge-m3 par défaut),
/// le même que celui de la recherche (ADR-0013) : <c>POST {adresse}/api/embed</c>. Absent, en panne ou trop
/// lent, il rend null, journalisé, et le tri se passe du sens (TRI-06 : TRI-05 seule joue).
/// </summary>
public class EmbeddingClient(HttpClient http, IConfiguration config, ILogger<EmbeddingClient> log)
{
    private string? Url => config["Embeddings:Url"]?.Trim().TrimEnd('/');
    public bool Enabled => !string.IsNullOrEmpty(Url);
    public string Model => config["Embeddings:Model"] is { Length: > 0 } m ? m : "bge-m3";

    private record EmbedResponse(float[][]? Embeddings);

    public async Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (!Enabled || texts.Count == 0) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            using var res = await http.PostAsJsonAsync($"{Url}/api/embed", new { model = Model, input = texts }, timeout.Token);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<EmbedResponse>(timeout.Token);
            if (body?.Embeddings is not { } vectors || vectors.Length != texts.Count) return null;
            return vectors.Select(Normalize).ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Service d'embeddings indisponible : {Error} ; corrélation par les règles seules.", ex.Message.Split('\n')[0]);
            return null;
        }
    }

    public static float[] Normalize(float[] v)
    {
        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }

    /// <summary>Similarité cosinus de deux vecteurs normalisés.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0;
        for (var i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot;
    }
}
