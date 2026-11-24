using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml;
using SAloha.Api.Core.Search;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SAloha.Api.Tests;

/// <summary>RAG-12 : le texte des pièces jointes entre dans la recherche (formats, plafond, statuts, retrait).</summary>
[Collection("api")]
public class AttachmentSearchTests(ApiFixture api, FakeS3Server s3) : IClassFixture<FakeS3Server>
{
    private const string Ooxml = "http://schemas.openxmlformats.org/";
    private const string Odf = "urn:oasis:names:tc:opendocument:xmlns:";

    private static byte[] Zip(params (string Name, string Xml)[] parts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, xml) in parts)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(xml);
            }
        return ms.ToArray();
    }

    private static Task<string?> Text(string name, byte[] bytes) => AttachmentText.ExtractAsync(name, new MemoryStream(bytes));

    [Fact]
    public async Task RAG_12_texte_extrait_des_formats_admis()
    {
        var docx = Zip(("word/document.xml", $"""<w:document xmlns:w="{Ooxml}wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Purger le cache</w:t></w:r><w:r><w:t xml:space="preserve"> du proxy</w:t></w:r></w:p><w:p><w:r><w:t>Relancer</w:t></w:r></w:p></w:body></w:document>"""));
        Assert.Equal("Purger le cache du proxy\n\nRelancer", (await Text("procedure.docx", docx))!.Trim());

        var xlsx = Zip(("xl/sharedStrings.xml", $"""<sst xmlns="{Ooxml}spreadsheetml/2006/main"><si><t>Serveur</t></si><si><t>srv-paie-01</t></si></sst>"""),
            ("xl/styles.xml", $"""<styleSheet xmlns="{Ooxml}spreadsheetml/2006/main"><t>ignoré</t></styleSheet>"""));
        var sheet = await Text("inventaire.xlsx", xlsx);
        Assert.Contains("srv-paie-01", sheet);
        Assert.DoesNotContain("ignoré", sheet);

        // Diapositives dans leur ordre : slide2 avant slide10.
        string Slide(string t) => $"""<p:sld xmlns:p="{Ooxml}presentationml/2006/main" xmlns:a="{Ooxml}drawingml/2006/main"><a:p><a:r><a:t>{t}</a:t></a:r></a:p></p:sld>""";
        var deck = await Text("revue.pptx", Zip(("ppt/slides/slide10.xml", Slide("Dixième")), ("ppt/slides/slide2.xml", Slide("Deuxième"))));
        Assert.True(deck!.IndexOf("Deuxième", StringComparison.Ordinal) < deck.IndexOf("Dixième", StringComparison.Ordinal));

        var odt = Zip(("content.xml", $"""<office:document-content xmlns:office="{Odf}office:1.0" xmlns:text="{Odf}text:1.0"><office:automatic-styles><text:p>style</text:p></office:automatic-styles><office:body><office:text><text:h>Procédure</text:h><text:p>Vider<text:s/>la<text:tab/>file</text:p></office:text></office:body></office:document-content>"""));
        Assert.Equal("Procédure\n\nVider la\tfile", (await Text("procedure.odt", odt))!.Trim());

        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Redemarrer le service de paie", 12, new PdfPoint(25, 700), font);
        Assert.Contains("Redemarrer le service de paie", await Text("consigne.pdf", builder.Build()));

        Assert.Equal("Journal 🔧 du 12/11", await Text("journal.log", Encoding.UTF8.GetBytes("Journal 🔧 du 12/11")));
        // 100 000 caractères au plus par fichier.
        Assert.Equal(AttachmentText.MaxChars, (await Text("gros.csv", Encoding.UTF8.GetBytes(new string('x', 150_000))))!.Length);
        // Ni image, ni format binaire ancien.
        Assert.Null(await Text("capture.png", [0x89, 0x50, 0x4E, 0x47]));
        Assert.Null(await Text("ancien.doc", [0xD0, 0xCF, 0x11, 0xE0]));
        // Une DTD (entités en cascade, entité externe) est refusée, pas développée.
        var piege = Zip(("content.xml", """<!DOCTYPE x [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]><x>&b;</x>"""));
        await Assert.ThrowsAsync<XmlException>(() => Text("piege.odt", piege));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"HTTP {(int)res.StatusCode} : {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();
    private static string Ref(JsonElement e) => e.GetProperty("reference").GetString()!;

    private Task<HttpResponseMessage> Storage(bool enabled) => api.Client("Admin", "admin.rag12").PutAsJsonAsync("/api/admin/settings/storage", new
    {
        enabled, endpoint = s3.Url, region = "garage", bucket = FakeS3Server.Bucket, accessKey = FakeS3Server.AccessKey, secretKey = "cle-secrete-s3", pathStyle = true, maxFileMb = 1
    });

    private static Task<HttpResponseMessage> Upload(HttpClient c, int incident, string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent { { new StringContent("incident"), "type" }, { new StringContent(incident.ToString()), "id" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        return c.PostAsync("/api/attachments", form);
    }

    private async Task<bool> Finds(string query, string reference)
    {
        var hits = await Json(await api.Client("User", "lecteur.rag12").GetAsync($"/api/search?q={Uri.EscapeDataString(query)}"));
        return hits.EnumerateArray().Any(h => h.GetProperty("reference").GetString() == reference);
    }

    /// <summary>L'indexation se fait en tâche de fond : on attend que la recherche voie (ou ne voie plus) la cible.</summary>
    private async Task Eventually(string query, string reference, bool found)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await Finds(query, reference) == found) return;
            await Task.Delay(200);
        }
        throw new Xunit.Sdk.XunitException($"« {query} » {(found ? "ne trouve pas" : "trouve encore")} {reference}");
    }

    [Fact]
    public async Task RAG_12_une_piece_jointe_d_un_incident_resolu_est_trouvee_jusqu_a_son_retrait()
    {
        var agent = api.Client("User", "agent.rag12");
        object Incident(string title, string? status) => new
        {
            title, description = "Poste bloqué", impact = "Faible", urgency = "Faible", status,
            ownerType = "User", ownerId = "agent.rag12", ownerDisplayName = "Agent",
            resolution = status is null ? null : "Procédure jointe appliquée", resolutionCode = status is null ? null : "Correctif appliqué"
        };
        var solved = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Proxy saturé RAG-12", null)));
        await Json(await agent.PutAsJsonAsync($"/api/incidents/{Id(solved)}", Incident("Proxy saturé RAG-12", "Résolu")));
        var open = await Json(await agent.PostAsJsonAsync("/api/incidents", Incident("Proxy lent RAG-12", null)));
        await Json(await Storage(true));
        try
        {
            var odt = Zip(("content.xml", $"""<office:document-content xmlns:office="{Odf}office:1.0" xmlns:text="{Odf}text:1.0"><office:body><office:text><text:p>Purger le cache XQ93V du proxy</text:p></office:text></office:body></office:document-content>"""));
            var file = await Json(await Upload(agent, Id(solved), "procedure.odt", odt));
            await Json(await Upload(agent, Id(open), "piste.txt", Encoding.UTF8.GetBytes("Piste non vérifiée WM52K")));

            await Eventually("XQ93V", Ref(solved), found: true);
            // RAG-01 : la pièce jointe d'un incident en cours n'est pas indexée.
            Assert.False(await Finds("WM52K", Ref(open)));

            // Le retrait de la pièce jointe la retire de l'index ; l'incident reste trouvable par son texte.
            await agent.DeleteAsync($"/api/attachments/{Id(file)}");
            await Eventually("XQ93V", Ref(solved), found: false);
            Assert.True(await Finds("Procédure jointe appliquée", Ref(solved)));
        }
        finally { await Json(await Storage(false)); }
    }
}
