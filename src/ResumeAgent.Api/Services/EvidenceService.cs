using System.Text.RegularExpressions;
using ResumeAgent.Api.Models;

namespace ResumeAgent.Api.Services;

public static partial class EvidenceService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "you", "your", "our", "are", "will",
        "have", "from", "work", "role", "team", "using", "was", "has", "into", "can", "its"
    };

    [GeneratedRegex(@"[a-z0-9][a-z0-9+#.]*", RegexOptions.IgnoreCase)]
    private static partial Regex Words();

    [GeneratedRegex(
        @"use only my own words|no (?:use of )?(?:ai|artificial intelligence)|(?:ai|generated content).{0,100}disqualif|(?:prohibit|not (?:allowed|permitted)).{0,50}(?:ai.generated|generative ai)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AiRestriction();

    public static bool ProhibitsAi(JobInput job) =>
        job.AiProhibited || AiRestriction().IsMatch(job.Description);

    public static EvidenceMatch[] Match(JobInput job, IEnumerable<EvidenceFact> facts)
    {
        ValidateJob(job);
        var terms = Tokens(job.Description + " " + job.Title);
        return facts.Where(f => f.Approved)
            .Select(f =>
            {
                var hits = Tokens(f.Statement).Intersect(terms).Order().ToArray();
                return new EvidenceMatch(f, hits.Length, hits);
            })
            .OrderByDescending(m => m.Score).ThenBy(m => m.Fact.Id).Take(20).ToArray();
    }

    private static HashSet<string> Tokens(string text) => Words().Matches(text.ToLowerInvariant())
        .Select(m => m.Value.TrimEnd('.')).Where(w => w.Length > 1 && !StopWords.Contains(w))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static void ValidateJob(JobInput job)
    {
        if (string.IsNullOrWhiteSpace(job.Title) || string.IsNullOrWhiteSpace(job.Company)
            || string.IsNullOrWhiteSpace(job.Description))
            throw new InputException("Company, role title, and job description are required.");
        if (job.Title.Length > 200 || job.Company.Length > 200 || job.Description.Length > 24_000)
            throw new InputException("Use a title/company under 200 characters and job text under 24,000.");
    }

    public static EvidenceFact ValidateFact(FactInput input, KnowledgeBase state, Guid? id = null)
    {
        var document = state.Documents.Find(d => d.Id == input.DocumentId)
            ?? throw new InputException("Select an imported source document.");
        if (!document.Excerpts.Any(e => e.Locator == input.Locator))
            throw new InputException("Select a valid source page or paragraph.");
        if (string.IsNullOrWhiteSpace(input.Statement) || input.Statement.Length > 3000)
            throw new InputException("Evidence must contain between 1 and 3,000 characters.");
        return new(id ?? Guid.NewGuid(), document.Id, input.Locator,
            input.Statement.Trim(), input.Approved);
    }
}
