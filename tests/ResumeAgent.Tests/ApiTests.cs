using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResumeAgent.Api.Models;
using ResumeAgent.Api.Services;

namespace ResumeAgent.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task BuiltFrontendAndAssetsAreServedBeforeUnmatchedRoutes()
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Synthetic frontend", await page.Content.ReadAsStringAsync());
        var asset = await client.GetAsync("/smoke.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing")).StatusCode);
    }

    [Fact]
    public async Task MutationsRequireCustomHeaderAndRejectForeignOrigin()
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();
        var noHeader = await client.PutAsJsonAsync("/api/profile", new CareerProfile("Synthetic"));
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);

        client.DefaultRequestHeaders.Add("X-Resume-Agent", "local");
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        var foreign = await client.PutAsJsonAsync("/api/profile", new CareerProfile("Synthetic"));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task RejectsNonLocalHostForPrivateReads()
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "untrusted.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/knowledge")).StatusCode);
    }

    [Fact]
    public async Task ImportStoresLocalTextWithoutApprovingEvidence()
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Resume-Agent", "local");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Synthetic React project evidence."), "file", "synthetic.txt");
        var response = await client.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await client.GetFromJsonAsync<KnowledgeBase>("/api/knowledge");
        Assert.Single(state!.Documents);
        Assert.Empty(state.Facts);
        Assert.Contains("Synthetic React", state.Documents[0].Excerpts[0].Text);
    }

    [Fact]
    public async Task ExportRequiresExplicitApprovalAndProducesDocx()
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Resume-Agent", "local");
        var draft = Draft([]);
        await factory.Services.GetRequiredService<KnowledgeStore>()
            .UpdateAsync(state => { state.Applications.Add(draft); return true; });
        var path = $"/api/applications/{draft.Id}/export/cv/docx";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/applications/{draft.Id}/approve",
                new { confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/applications/{draft.Id}/approve",
                new { confirmed = true })).StatusCode);
        var exported = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            exported.Content.Headers.ContentType!.MediaType);
        var parsed = new DocumentImporter().Import("synthetic.docx",
            await exported.Content.ReadAsByteArrayAsync());
        Assert.Contains(parsed.Excerpts, e => e.Text == "Synthetic Candidate");
    }

    [Fact]
    public async Task ReviewErrorsBlockApproval()
    {
        using var factory = new LocalFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Resume-Agent", "local");
        var draft = Draft([new("error", "Unsupported claim.")]);
        await factory.Services.GetRequiredService<KnowledgeStore>()
            .UpdateAsync(state => { state.Applications.Add(draft); return true; });
        var response = await client.PostAsJsonAsync($"/api/applications/{draft.Id}/approve",
            new { confirmed = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static ApplicationDraft Draft(ReviewFinding[] findings) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, new("Example Co", "Engineer", "Synthetic job"),
            new("Synthetic Candidate"), [],
            new("Builds interfaces.", [new("React development.", [])], "Interested in the role."),
            findings, "synthetic-model");

    private sealed class LocalFactory : WebApplicationFactory<Program>
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(), "resume-agent-api-test-" + Guid.NewGuid());

        public LocalFactory()
        {
            Directory.CreateDirectory(Path.Combine(directory, "wwwroot"));
            File.WriteAllText(Path.Combine(directory, "wwwroot", "index.html"), "Synthetic frontend");
            File.WriteAllText(Path.Combine(directory, "wwwroot", "smoke.js"), "console.log('synthetic');");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(Path.Combine(directory, "wwwroot"));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<KnowledgeStore>();
                services.AddSingleton(new KnowledgeStore(directory));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            var file = Path.Combine(directory, "knowledge.json");
            if (File.Exists(file)) File.Delete(file);
            var webRoot = Path.Combine(directory, "wwwroot");
            if (Directory.Exists(webRoot))
            {
                File.Delete(Path.Combine(webRoot, "index.html"));
                File.Delete(Path.Combine(webRoot, "smoke.js"));
                Directory.Delete(webRoot);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
