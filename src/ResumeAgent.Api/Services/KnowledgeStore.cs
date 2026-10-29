using System.Text.Json;
using ResumeAgent.Api.Models;

namespace ResumeAgent.Api.Services;

public sealed class KnowledgeStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public KnowledgeStore(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "knowledge.json");
    }

    public Task<KnowledgeBase> ReadAsync(CancellationToken ct = default) =>
        AccessAsync(state => state, false, ct);

    public Task<T> UpdateAsync<T>(Func<KnowledgeBase, T> update, CancellationToken ct = default) =>
        AccessAsync(update, true, ct);

    private async Task<T> AccessAsync<T>(
        Func<KnowledgeBase, T> action, bool save, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            KnowledgeBase state;
            if (File.Exists(path))
            {
                await using var input = File.OpenRead(path);
                state = await JsonSerializer.DeserializeAsync<KnowledgeBase>(input, JsonOptions, ct)
                    ?? throw new InvalidDataException("The private knowledge file is empty.");
            }
            else
            {
                state = new();
            }

            var result = action(state);
            if (save)
            {
                var temporary = path + ".tmp";
                await File.WriteAllTextAsync(
                    temporary, JsonSerializer.Serialize(state, JsonOptions), ct);
                File.Move(temporary, path, true);
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
