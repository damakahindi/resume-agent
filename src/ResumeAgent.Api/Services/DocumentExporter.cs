using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ResumeAgent.Api.Models;

namespace ResumeAgent.Api.Services;

public static class DocumentExporter
{
    public static string Cv(ApplicationDraft application)
    {
        var lines = new List<string>
        {
            application.Profile.Name,
            application.Profile.Headline,
            application.Profile.Contact,
            application.Profile.Location,
            "",
            "PROFESSIONAL SUMMARY",
            application.Draft.Summary
        };
        if (!string.IsNullOrWhiteSpace(application.Profile.Skills))
            lines.AddRange(["", "SKILLS", application.Profile.Skills]);
        if (!string.IsNullOrWhiteSpace(application.Profile.Experience))
            lines.AddRange(["", "WORK HISTORY", application.Profile.Experience]);
        lines.AddRange(["", "SELECTED RELEVANT ACHIEVEMENTS"]);
        lines.AddRange(application.Draft.Bullets.Select(b => "- " + b.Text));
        if (!string.IsNullOrWhiteSpace(application.Profile.Education))
            lines.AddRange(["", "EDUCATION", application.Profile.Education]);
        return string.Join("\n", lines);
    }

    public static byte[] Docx(string text)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var document = new XDocument(new XElement(w + "document",
                new XAttribute(XNamespace.Xmlns + "w", w),
                new XElement(w + "body", text.Replace("\r\n", "\n").Split('\n').Select(line =>
                    new XElement(w + "p", new XElement(w + "r",
                        new XElement(w + "rPr",
                            new XElement(w + "rFonts", new XAttribute(w + "ascii", "Calibri"),
                                new XAttribute(w + "hAnsi", "Calibri")),
                            new XElement(w + "sz", new XAttribute(w + "val", "22"))),
                        new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line)))))));
            Write(zip, "word/document.xml", document.ToString());
        }
        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
