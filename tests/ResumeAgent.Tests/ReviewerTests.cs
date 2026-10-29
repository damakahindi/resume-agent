using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ResumeAgent.Api.Clients;
using ResumeAgent.Api.Models;
using ResumeAgent.Api.Services;
using ResumeAgent.Api.Workflows;

namespace ResumeAgent.Tests;

public sealed class ReviewerTests
{
    private static readonly JobInput Job = new("Example Co", "React Engineer",
        "Build accessible React and TypeScript interfaces with REST APIs.");

    [Fact]
    public void MatchingExcludesUnapprovedFactsAndRanksRelevantEvidence()
    {
        var best = Fact("Built accessible React interfaces using TypeScript.");
        var unrelated = Fact("Organized office events.");
        var privateFact = Fact("React confidential work.", approved: false);
        var matches = EvidenceService.Match(Job, [unrelated, privateFact, best]);
        Assert.Equal(best.Id, matches[0].Fact.Id);
        Assert.DoesNotContain(matches, m => m.Fact.Id == privateFact.Id);
        Assert.Contains("react", matches[0].MatchedTerms);
    }

    [Theory]
    [InlineData("During this application I agree to use only my own words.")]
    [InlineData("The use of AI or other generated content will disqualify my application.")]
    [InlineData("No AI-generated applications.")]
    public void DetectsEmployerAiRestrictions(string text)
    {
        Assert.True(EvidenceService.ProhibitsAi(Job with { Description = text }));
    }

    [Fact]
    public void FactRequiresRealSourceLocator()
    {
        var id = Guid.NewGuid();
        var state = new KnowledgeBase
        {
            Documents = [new(id, "synthetic.txt", DateTimeOffset.UtcNow, [new("Paragraph 1", "Source")])]
        };
        Assert.Throws<InputException>(() => EvidenceService.ValidateFact(
            new(id, "Page 99", "Supported fact", true), state));
    }

    [Fact]
    public void ReviewerDetectsUnknownCitationsAndInventedNumbers()
    {
        var fact = Fact("Improved response time by 20%.");
        var draft = new ModelDraft("Improved performance by 90%.",
            [new("Built a service.", [Guid.NewGuid()])], "Interested in this role.");
        var findings = ReviewerWorkflow.CheckEvidence(draft, [fact], new("Test Candidate"));
        Assert.Contains(findings, f => f.Message.Contains("citation"));
        Assert.Contains(findings, f => f.Message.Contains("90%"));
    }

    [Fact]
    public void ReviewerAcceptsKnownCitationAndSupportedMetric()
    {
        var fact = Fact("Improved response time by 20%.");
        var draft = new ModelDraft("Builds reliable software.",
            [new(fact.Statement, [fact.Id])], "Interested in this role.");
        Assert.Empty(ReviewerWorkflow.CheckEvidence(draft, [fact], new("Test Candidate")));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://127.0.0.1.example.com/")]
    [InlineData("http://127.0.0.1:11434/cloud")]
    [InlineData("http://user:password@localhost:11434/")]
    [InlineData("file:///private")]
    public void RejectsNonLocalModelEndpoints(string endpoint)
    {
        Assert.Throws<InvalidOperationException>(() => OllamaClient.ValidateEndpoint(endpoint));
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/")]
    [InlineData("http://localhost:11434/")]
    [InlineData("http://[::1]:11434/")]
    public void AcceptsLoopbackModelEndpoints(string endpoint)
    {
        Assert.True(OllamaClient.ValidateEndpoint(endpoint).IsAbsoluteUri);
    }

    [Theory]
    [InlineData("gpt-oss:120b-cloud")]
    [InlineData("qwen3:cloud")]
    public void RejectsCloudBackedModelNames(string name)
    {
        Assert.Throws<InvalidOperationException>(() => OllamaClient.ValidateModel(name));
    }

    [Fact]
    public void DocxRoundTripsEscapedTextWithoutLosingParagraphs()
    {
        var source = "Test Candidate\nC# & React <engineer>\nBuilt a useful service.";
        var bytes = DocumentExporter.Docx(source);
        var document = new DocumentImporter().Import("synthetic.docx", bytes);
        Assert.Equal(source.Split('\n'), document.Excerpts.Select(e => e.Text));
    }

    [Fact]
    public void ImportRejectsOversizedAndUnsupportedFiles()
    {
        var importer = new DocumentImporter();
        Assert.Throws<InputException>(() => importer.Import("script.exe", [1]));
        Assert.Throws<InputException>(() =>
            importer.Import("large.txt", new byte[DocumentImporter.MaxBytes + 1]));
    }

    [Fact]
    public void DocxImportRejectsDtd()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("""<!DOCTYPE x [<!ENTITY xxe SYSTEM "file:///private">]><x>&xxe;</x>""");
        Assert.Throws<System.Xml.XmlException>(() =>
            new DocumentImporter().Import("synthetic.docx", buffer.ToArray()));
    }

    [Fact]
    public async Task StorePersistsConcurrentUpdatesWithoutLostFacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "resume-agent-test-" + Guid.NewGuid());
        try
        {
            var store = new KnowledgeStore(directory);
            await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.UpdateAsync(state =>
            {
                state.Facts.Add(Fact($"Synthetic fact {i}"));
                return true;
            })));
            var reloaded = await new KnowledgeStore(directory).ReadAsync();
            Assert.Equal(12, reloaded.Facts.Count);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "knowledge.json"));
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task WorkflowRejectsUnapprovedEvidenceBeforeCallingModel()
    {
        var handler = new FakeOllamaHandler([]);
        var workflow = Workflow(handler);
        var fact = Fact("React work.", approved: false);
        var state = new KnowledgeBase { Profile = new("Test Candidate"), Facts = [fact] };
        await Assert.ThrowsAsync<InputException>(() => workflow.GenerateAsync(
            new(Job, [fact.Id]), state, default));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task WorkflowDoesNotSendRawSourceTextAndPerformsSeparateReview()
    {
        var fact = Fact("Built accessible React interfaces.");
        var draft = new ModelDraft("Frontend engineer.",
            [new(fact.Statement, [fact.Id])], "I am interested in the React role at Example Co.");
        var handler = new FakeOllamaHandler([draft, new ModelReview([])]);
        var state = new KnowledgeBase
        {
            Profile = new("Test Candidate"),
            Facts = [fact],
            Documents = [new(fact.DocumentId, "private.txt", DateTimeOffset.UtcNow,
                [new("Paragraph 1", "RAW_SOURCE_SENTINEL_DO_NOT_TRANSMIT")])]
        };
        var result = await Workflow(handler).GenerateAsync(new(Job, [fact.Id]), state, default);
        Assert.Equal(2, handler.CallCount);
        Assert.All(handler.Requests, body => Assert.DoesNotContain("RAW_SOURCE_SENTINEL", body));
        Assert.Contains(result.Findings, f => f.Message.Contains("Human review"));
        Assert.Null(result.ApprovedAt);
    }

    [Fact]
    public async Task WorkflowRejectsAiProhibitedRoleBeforeModelCall()
    {
        var handler = new FakeOllamaHandler([]);
        await Assert.ThrowsAsync<InputException>(() => Workflow(handler).GenerateAsync(
            new(Job with { AiProhibited = true }, []), new(), default));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task WorkflowRejectsMalformedDraftWithoutSaving()
    {
        var fact = Fact("Built React interfaces.");
        var handler = new FakeOllamaHandler([new { summary = "Incomplete" }]);
        await Assert.ThrowsAsync<ModelException>(() => Workflow(handler).GenerateAsync(
            new(Job, [fact.Id]), new() { Profile = new("Test Candidate"), Facts = [fact] }, default));
    }

    private static EvidenceFact Fact(string statement, bool approved = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Paragraph 1", statement, approved);

    private static ReviewerWorkflow Workflow(FakeOllamaHandler handler) =>
        new(new(new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:11434/") }, "test-model"));

    private sealed class FakeOllamaHandler(object[] outputs) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (request.RequestUri!.AbsolutePath == "/api/show")
                return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            var output = outputs[CallCount++];
            var content = JsonSerializer.Serialize(output,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    message = new { content }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
