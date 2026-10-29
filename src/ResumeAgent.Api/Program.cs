using System.Net;
using System.Text;
using ResumeAgent.Api.Clients;
using ResumeAgent.Api.Models;
using ResumeAgent.Api.Services;
using ResumeAgent.Api.Workflows;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5283");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 11 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(
    options => options.MultipartBodyLengthLimit = DocumentImporter.MaxBytes + 1024 * 1024);

var dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumeAgent");
var endpoint = OllamaClient.ValidateEndpoint(
    builder.Configuration["Ollama:Endpoint"] ?? "http://127.0.0.1:11434/");
var modelName = builder.Configuration["Ollama:Model"] ?? "qwen2.5:7b";
builder.Services.AddSingleton(new KnowledgeStore(dataDirectory));
builder.Services.AddSingleton<DocumentImporter>();
builder.Services.AddHttpClient("Ollama", http =>
{
    http.BaseAddress = endpoint;
    http.Timeout = TimeSpan.FromMinutes(8);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseProxy = false
});
builder.Services.AddTransient<ReviewerWorkflow>();
builder.Services.AddTransient(sp => new OllamaClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama"), modelName));

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    var host = context.Request.Host.Host;
    if (host is not ("127.0.0.1" or "localhost" or "[::1]" or "::1"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    if (context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (context.Request.Headers["X-Resume-Agent"] != "local"
            || (origin.Length > 0 && origin is not ("http://127.0.0.1:5283"
                or "http://localhost:5283" or "http://127.0.0.1:5173" or "http://localhost:5173")))
        {
            context.Response.StatusCode = 403;
            return;
        }
    }
    try
    {
        await next();
    }
    catch (InputException ex)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (ModelException ex)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
    {
        // Avoid logging document contents, prompts, model output, or private paths.
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "The local operation failed. Your existing knowledge file was not intentionally replaced."
        });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (OllamaClient client, CancellationToken ct) =>
{
    var models = await client.GetModelsAsync(ct);
    return Results.Ok(new
    {
        model = client.Model,
        ready = models.Any(m => m == client.Model || m == client.Model + ":latest"),
        installedModels = models,
        storage = "LocalApplicationData/ResumeAgent",
        localOnly = true
    });
});
app.MapGet("/api/knowledge", async (KnowledgeStore store, CancellationToken ct) =>
    Results.Ok(await store.ReadAsync(ct)));
app.MapPut("/api/profile", async (CareerProfile profile, KnowledgeStore store, CancellationToken ct) =>
{
    if (profile.Name is null || profile.Headline is null || profile.Contact is null
        || profile.Location is null || profile.Education is null || profile.Experience is null
        || profile.Skills is null)
        throw new InputException("Profile fields must be text, not null.");
    if (profile.Name.Length > 200 || profile.Headline.Length > 300 || profile.Contact.Length > 500
        || profile.Location.Length > 200 || profile.Education.Length > 3000
        || profile.Experience.Length > 6000 || profile.Skills.Length > 2000)
        throw new InputException("A profile field is too long.");
    await store.UpdateAsync(state => state.Profile = profile, ct);
    return Results.Ok(profile);
});
app.MapPost("/api/documents", async (
    HttpRequest request, DocumentImporter importer, KnowledgeStore store, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        throw new InputException("Upload a document using multipart form data.");
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file") ?? throw new InputException("Select a file.");
    if (file.Length > DocumentImporter.MaxBytes)
        throw new InputException("The document exceeds 10 MB.");
    using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer, ct);
    SourceDocument document;
    try
    {
        document = importer.Import(file.FileName, buffer.ToArray());
    }
    catch (InputException) { throw; }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        throw new InputException("Cannot extract this document. Check its format, encryption, or corruption.");
    }
    await store.UpdateAsync(state => { state.Documents.Add(document); return document; }, ct);
    return Results.Ok(document);
});
app.MapPost("/api/facts", async (FactInput input, KnowledgeStore store, CancellationToken ct) =>
    Results.Ok(await store.UpdateAsync(state =>
    {
        var fact = EvidenceService.ValidateFact(input, state);
        state.Facts.Add(fact);
        return fact;
    }, ct)));
app.MapPut("/api/facts/{id:guid}", async (
    Guid id, FactInput input, KnowledgeStore store, CancellationToken ct) =>
    Results.Ok(await store.UpdateAsync(state =>
    {
        var index = state.Facts.FindIndex(f => f.Id == id);
        if (index < 0) throw new InputException("Evidence fact not found.");
        return state.Facts[index] = EvidenceService.ValidateFact(input, state, id);
    }, ct)));
app.MapPost("/api/match", async (JobInput job, KnowledgeStore store, CancellationToken ct) =>
{
    var state = await store.ReadAsync(ct);
    return Results.Ok(new
    {
        aiProhibited = EvidenceService.ProhibitsAi(job),
        matches = EvidenceService.Match(job, state.Facts)
    });
});
app.MapPost("/api/applications", async (
    GenerateInput input, ReviewerWorkflow workflow, KnowledgeStore store, CancellationToken ct) =>
{
    var snapshot = await store.ReadAsync(ct);
    var draft = await workflow.GenerateAsync(input, snapshot, ct);
    await store.UpdateAsync(state =>
    {
        if (state.Profile != snapshot.Profile || draft.Evidence.Any(f =>
            !state.Facts.Any(current => current == f && current.Approved)))
            throw new InputException("Your profile or selected evidence changed during drafting. Generate again.");
        state.Applications.Add(draft);
        return draft;
    }, ct);
    return Results.Ok(draft);
});
app.MapPost("/api/applications/{id:guid}/approve", async (
    Guid id, ApprovalInput input, KnowledgeStore store, CancellationToken ct) =>
{
    if (!input.Confirmed)
        throw new InputException("Confirm that you personally verified the entire draft.");
    return Results.Ok(await store.UpdateAsync(state =>
    {
        var index = state.Applications.FindIndex(a => a.Id == id);
        if (index < 0) throw new InputException("Application not found.");
        var draft = state.Applications[index];
        if (draft.Findings.Any(f => f.Severity == "error"))
            throw new InputException("This draft has reviewer errors. Fix the source evidence and regenerate.");
        return state.Applications[index] = draft with { ApprovedAt = DateTimeOffset.UtcNow };
    }, ct));
});
app.MapGet("/api/applications/{id:guid}/export/{kind}/{format}", async (
    Guid id, string kind, string format, KnowledgeStore store, CancellationToken ct) =>
{
    var state = await store.ReadAsync(ct);
    var draft = state.Applications.Find(a => a.Id == id)
        ?? throw new InputException("Application not found.");
    if (draft.ApprovedAt is null)
        throw new InputException("Personally review and approve the draft before export.");
    var text = kind switch
    {
        "cv" => DocumentExporter.Cv(draft),
        "cover-letter" => draft.Draft.CoverLetter,
        _ => throw new InputException("Choose cv or cover-letter.")
    };
    return format switch
    {
        "md" => Results.File(Encoding.UTF8.GetBytes(text), "text/markdown", $"{kind}.md"),
        "docx" => Results.File(DocumentExporter.Docx(text),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{kind}.docx"),
        _ => throw new InputException("Choose Markdown or DOCX.")
    };
});

app.MapFallback(() => Results.NotFound(new
{
    error = "Build the React app first (see README), or use its development server."
}));
app.Run();

public sealed record ApprovalInput(bool Confirmed);
public partial class Program;
