using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ResumeAgent.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ResumeAgent.Api.Services;

public sealed class DocumentImporter
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private const int MaxCharacters = 180_000;

    public SourceDocument Import(string fileName, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            throw new InputException("Upload a non-empty document of at most 10 MB.");

        var excerpts = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => ReadPdf(bytes),
            ".docx" => ReadDocx(bytes),
            ".txt" or ".md" => ReadText(bytes),
            _ => throw new InputException("Supported formats: PDF, DOCX, TXT, and Markdown.")
        };
        if (excerpts.Count == 0)
            throw new InputException(
                "No selectable text was found. Scanned PDFs need local OCR before import.");
        if (excerpts.Sum(x => x.Text.Length) > MaxCharacters)
            throw new InputException("Document text exceeds 180,000 characters. Split the document.");

        return new(Guid.NewGuid(), Path.GetFileName(fileName),
            DateTimeOffset.UtcNow, excerpts);
    }

    private static List<SourceExcerpt> ReadPdf(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        if (pdf.NumberOfPages > 100)
            throw new InputException("PDFs must contain at most 100 pages.");
        var excerpts = new List<SourceExcerpt>();
        var length = 0;
        foreach (var page in pdf.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page).Trim();
            length += text.Length;
            if (length > MaxCharacters)
                throw new InputException("Document text is too large. Split the PDF.");
            if (text.Length > 0)
                excerpts.Add(new($"Page {page.Number}", text));
        }
        return excerpts;
    }

    private static List<SourceExcerpt> ReadDocx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InputException("This DOCX has no document body.");
        if (entry.Length > 2 * 1024 * 1024)
            throw new InputException("The uncompressed DOCX body is too large.");
        using var body = entry.Open();
        using var reader = XmlReader.Create(body, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2 * 1024 * 1024
        });
        var xml = XDocument.Load(reader);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return xml.Descendants(w + "p")
            .Select((p, index) => new SourceExcerpt($"Paragraph {index + 1}",
                string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim()))
            .Where(p => p.Text.Length > 0).ToList();
    }

    private static List<SourceExcerpt> ReadText(byte[] bytes)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes);
        return text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => new SourceExcerpt($"Paragraph {i + 1}", p.Trim()))
            .Where(p => p.Text.Length > 0).ToList();
    }
}
