using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Platform;

namespace SAloha.Api.Core.Search;

/// <summary>Met à jour les passages d'un enregistrement (ou de tous) et leurs vecteurs.</summary>
public class SearchIndexer(AppDbContext db, IEmbeddingClient embeddings, ObjectStorage storage, ILogger<SearchIndexer> logger)
{
    /// <summary>
    /// Réindexe un enregistrement : ses anciens passages sont retirés, puis
    /// recréés s'il est (encore) éligible. Sans service d'embeddings, les
    /// passages sont créés sans vecteur : ils restent trouvables par la
    /// recherche lexicale (RAG-02).
    /// </summary>
    public async Task ReindexAsync(string type, int id, CancellationToken ct = default)
    {
        var old = await db.SearchPassages.Where(p => p.SourceType == type && p.SourceId == id).ToListAsync(ct);
        db.SearchPassages.RemoveRange(old);

        var doc = await SearchSources.IndexableAsync(db, type, id);
        var passages = new List<SearchPassage>();
        if (doc is not null)
        {
            var chunks = Chunker.Split(doc.Text);
            if (chunks.Count == 0) chunks.Add(doc.Title);
            chunks.AddRange(await AttachmentChunksAsync(type, id, ct));
            // Le titre accompagne chaque passage dans le vecteur : un passage
            // isolé (« Relancer le service ») n'a de sens qu'avec son sujet.
            var vectors = await embeddings.EmbedAsync(chunks.Select(c => doc.Title + "\n" + c).ToList(), TimeSpan.FromSeconds(120), ct);
            passages = chunks.Select((c, i) => new SearchPassage
            {
                SourceType = type, SourceId = id, Reference = doc.Reference, Title = Truncate(doc.Title, 200),
                Status = doc.Status, Ordinal = i, Text = c,
                Embedding = vectors?[i], EmbeddingModel = vectors is null ? null : embeddings.Model
            }).ToList();
            db.SearchPassages.AddRange(passages);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// RAG-12 : passages du texte des pièces jointes de l'enregistrement, chacun préfixé du nom du
    /// fichier ; ils suivent ses statuts (RAG-01) et ses droits (RAG-13). Stockage coupé ou fichier
    /// illisible : la pièce est passée, l'enregistrement reste indexé (RAG-03).
    /// </summary>
    private async Task<List<string>> AttachmentChunksAsync(string type, int id, CancellationToken ct)
    {
        var files = (await db.Attachments.AsNoTracking().Where(a => a.TargetType == type && a.TargetId == id)
            .OrderBy(a => a.Id).ToListAsync(ct)).Where(a => AttachmentText.Supported(a.FileName)).ToList();
        var chunks = new List<string>();
        if (files.Count == 0) return chunks;
        var settings = await storage.SettingsAsync();
        if (!ObjectStorage.Ready(settings)) return chunks;
        foreach (var file in files)
        {
            try
            {
                await using var content = await storage.GetAsync(settings!, file.StorageKey, ct);
                var text = await AttachmentText.ExtractAsync(file.FileName, content, ct);
                chunks.AddRange(Chunker.Split(text).Select(c => $"Pièce jointe : {file.FileName}\n{c}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Pièce jointe {Id} de {Type} {Record} non indexée : {Error}", file.Id, type, id, ex.Message.Split('\n')[0]);
            }
        }
        return chunks;
    }

    /// <summary>Reconstruit tout l'index (démarrage sur un index vide, demande d'un administrateur).</summary>
    public async Task ReindexAllAsync(CancellationToken ct = default)
    {
        await db.SearchPassages.ExecuteDeleteAsync(ct);
        var count = 0;
        foreach (var type in SearchSources.Types)
            foreach (var id in await SearchSources.IdsAsync(db, type))
            {
                await ReindexAsync(type, id, ct);
                count++;
            }
        logger.LogInformation("Index de recherche reconstruit : {Count} enregistrement(s) examiné(s).", count);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
