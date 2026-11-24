using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UglyToad.PdfPig;

namespace SAloha.Api.Core.Search;

/// <summary>
/// RAG-12 (décision du 2026-11-09, précisée le 2026-11-24) : texte d'une pièce jointe pour l'index.
/// Texte brut (txt, log, csv, json, eml), PDF à couche texte (PdfPig), Office Open XML (docx, xlsx,
/// pptx) et OpenDocument (odt, ods, odp), ces deux derniers lus comme archives zip de XML, sans
/// bibliothèque. Ni OCR des images ou des PDF numérisés, ni doc, xls, ppt, msg ou zip.
/// </summary>
public static partial class AttachmentText
{
    /// <summary>Plafond par fichier : au-delà, le texte est tronqué.</summary>
    public const int MaxChars = 100_000;

    // Une archive piégée (bombe de décompression, XML démesuré) ne coûte que ces plafonds :
    // le XML se lit en flux, sans DTD ni résolution d'entité, et s'arrête à MaxXmlChars.
    private const long MaxXmlChars = 20_000_000;
    private const int MaxParts = 500;

    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string OdfText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string OdfOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    private static readonly HashSet<string> Plain = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".log", ".csv", ".json", ".eml" };

    /// <summary>Parties lues d'une archive OOXML : le texte est dans les éléments <c>t</c>, un paragraphe se clôt par <c>p</c> (ou <c>si</c>).</summary>
    private record Ooxml(Func<string, bool> Part, string Ns, string Paragraph);

    private static readonly Dictionary<string, Ooxml> OfficeOpen = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = new(p => p == "word/document.xml", Word, "p"),
        [".xlsx"] = new(p => p == "xl/sharedStrings.xml" || (p.StartsWith("xl/worksheets/sheet") && p.EndsWith(".xml")), Sheet, "si"),
        [".pptx"] = new(p => p.StartsWith("ppt/slides/slide") && p.EndsWith(".xml"), Drawing, "p"),
    };

    private static readonly HashSet<string> OpenDocument = new(StringComparer.OrdinalIgnoreCase) { ".odt", ".ods", ".odp" };

    /// <summary>Le format de ce fichier est-il lu ?</summary>
    public static bool Supported(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return Plain.Contains(ext) || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
               || OfficeOpen.ContainsKey(ext) || OpenDocument.Contains(ext);
    }

    /// <summary>
    /// Texte du fichier, au plus <see cref="MaxChars"/> caractères, ou null si le format n'est pas lu.
    /// Un fichier illisible lève une exception : l'appelant la journalise et passe (RAG-03).
    /// </summary>
    public static async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (!Supported(fileName)) return null;
        var ext = Path.GetExtension(fileName);
        if (Plain.Contains(ext)) return await ReadPlainAsync(content);
        // PdfPig et ZipArchive lisent en accès direct ; le flux du stockage objet ne le permet pas.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var text = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? ReadPdf(buffer)
            : OfficeOpen.TryGetValue(ext, out var spec) ? ReadOoxml(buffer, spec)
            : ReadOpenDocument(buffer);
        return Clip(text);
    }

    private static async Task<string> ReadPlainAsync(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var chars = new char[MaxChars];
        var n = await reader.ReadBlockAsync(chars, 0, MaxChars);
        return new string(chars, 0, n);
    }

    private static string ReadPdf(Stream content)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(content);
        foreach (var page in pdf.GetPages())
        {
            sb.Append(string.Join(' ', page.GetWords().Select(w => w.Text))).Append("\n\n");
            if (sb.Length >= MaxChars) break;
        }
        return sb.ToString();
    }

    private static string ReadOoxml(Stream content, Ooxml spec)
    {
        var sb = new StringBuilder();
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        var parts = zip.Entries.Where(e => spec.Part(e.FullName)).OrderBy(e => PartNumber(e.FullName)).ThenBy(e => e.FullName).Take(MaxParts);
        foreach (var part in parts)
        {
            using var stream = part.Open();
            using var xml = XmlReader.Create(stream, XmlSettings);
            while (sb.Length < MaxChars && xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "t" && xml.NamespaceURI == spec.Ns && !xml.IsEmptyElement)
                    sb.Append(xml.ReadElementContentAsString());
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == spec.Paragraph && xml.NamespaceURI == spec.Ns)
                    sb.Append("\n\n");
            }
            if (sb.Length >= MaxChars) break;
        }
        return sb.ToString();
    }

    private static string ReadOpenDocument(Stream content)
    {
        var sb = new StringBuilder();
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        var part = zip.GetEntry("content.xml");
        if (part is null) return "";
        using var stream = part.Open();
        using var xml = XmlReader.Create(stream, XmlSettings);
        var inBody = false;
        while (sb.Length < MaxChars && xml.Read())
        {
            switch (xml.NodeType)
            {
                case XmlNodeType.Element when xml.LocalName == "body" && xml.NamespaceURI == OdfOffice:
                    inBody = true; break;
                case XmlNodeType.EndElement when xml.LocalName == "body" && xml.NamespaceURI == OdfOffice:
                    inBody = false; break;
                case XmlNodeType.Element when inBody && xml.NamespaceURI == OdfText:
                    if (xml.LocalName == "s") sb.Append(' ');
                    else if (xml.LocalName == "tab") sb.Append('\t');
                    else if (xml.LocalName == "line-break") sb.Append('\n');
                    break;
                case XmlNodeType.EndElement when inBody && xml.NamespaceURI == OdfText && xml.LocalName is "p" or "h":
                    sb.Append("\n\n"); break;
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace when inBody:
                    sb.Append(xml.Value); break;
            }
        }
        return sb.ToString();
    }

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlChars,
        IgnoreComments = true, IgnoreProcessingInstructions = true
    };

    /// <summary>Les diapositives et les feuilles dans leur ordre : slide2 avant slide10.</summary>
    private static int PartNumber(string name) => Digits().Match(Path.GetFileName(name)) is { Success: true } m ? int.Parse(m.Value) : 0;

    private static string Clip(string text) => text.Length <= MaxChars ? text : text[..MaxChars];

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
