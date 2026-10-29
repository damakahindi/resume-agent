namespace ResumeAgent.Api.Models;

public sealed record CareerProfile(
    string Name = "",
    string Headline = "",
    string Contact = "",
    string Location = "",
    string Education = "",
    string Experience = "",
    string Skills = "");

public sealed record SourceDocument(
    Guid Id, string Name, DateTimeOffset ImportedAt, IReadOnlyList<SourceExcerpt> Excerpts);

public sealed record SourceExcerpt(string Locator, string Text);

public sealed record EvidenceFact(
    Guid Id, Guid DocumentId, string Locator, string Statement, bool Approved);

public sealed record FactInput(Guid DocumentId, string Locator, string Statement, bool Approved);

public sealed record JobInput(
    string Company, string Title, string Description, bool AiProhibited = false);

public sealed record GenerateInput(JobInput Job, Guid[] FactIds);

public sealed record EvidenceMatch(EvidenceFact Fact, int Score, string[] MatchedTerms);

public sealed record DraftBullet(string Text, Guid[] EvidenceIds);

public sealed record ModelDraft(string Summary, DraftBullet[] Bullets, string CoverLetter);

public sealed record ReviewFinding(string Severity, string Message);

public sealed record ModelReview(ReviewFinding[] Findings);

public sealed record ApplicationDraft(
    Guid Id,
    DateTimeOffset CreatedAt,
    JobInput Job,
    CareerProfile Profile,
    EvidenceFact[] Evidence,
    ModelDraft Draft,
    ReviewFinding[] Findings,
    string Model,
    DateTimeOffset? ApprovedAt = null);

public sealed class KnowledgeBase
{
    public CareerProfile Profile { get; set; } = new();
    public List<SourceDocument> Documents { get; set; } = [];
    public List<EvidenceFact> Facts { get; set; } = [];
    public List<ApplicationDraft> Applications { get; set; } = [];
}

public sealed class InputException(string message) : Exception(message);
public sealed class ModelException(string message) : Exception(message);
