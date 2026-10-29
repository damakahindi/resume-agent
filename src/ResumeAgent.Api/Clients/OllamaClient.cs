using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResumeAgent.Api.Models;

namespace ResumeAgent.Api.Clients;

public sealed class OllamaClient(HttpClient http, string model)
{
    public string Model { get; } = ValidateModel(model);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ValidateModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200
            || name.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a local Ollama model, not a cloud model.");
        return name;
    }

    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Ollama must use a loopback-only base URL.");
        return uri;
    }

    public async Task<string[]> GetModelsAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await http.GetAsync("api/tags", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return json.RootElement.GetProperty("models").EnumerateArray()
                .Where(m => !HasRemoteHost(m))
                .Select(m => m.GetProperty("name").GetString()!).ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async Task<T> CompleteAsync<T>(
        string instruction, object input, object schema, CancellationToken ct)
    {
        try
        {
            using var metadata = await http.PostAsJsonAsync("api/show", new { model = Model }, ct);
            if (!metadata.IsSuccessStatusCode)
                throw new ModelException("The selected local model is not installed. Run ollama pull first.");
            using var details = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync(ct));
            if (HasRemoteHost(details.RootElement))
                throw new ModelException("Cloud-backed Ollama models are disabled. Choose a local model.");
            using var response = await http.PostAsJsonAsync("api/chat", new
            {
                model = Model,
                stream = false,
                format = schema,
                options = new { temperature = 0.1, num_ctx = 16384, num_predict = 4096 },
                messages = new[]
                {
                    new { role = "system", content = instruction },
                    new { role = "user", content = JsonSerializer.Serialize(input, JsonOptions) }
                }
            }, ct);
            if (!response.IsSuccessStatusCode)
                throw new ModelException(
                    "Ollama could not run the configured model. Check that it is installed locally.");
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var content = payload.RootElement.GetProperty("message").GetProperty("content").GetString();
            return JsonSerializer.Deserialize<T>(content ?? "", JsonOptions)
                ?? throw new ModelException("The local model returned an empty response.");
        }
        catch (HttpRequestException)
        {
            throw new ModelException(
                "Ollama is unavailable. Start Ollama and install the configured model; see README.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ModelException("The local model timed out. Try fewer evidence facts or a smaller model.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelException("The model returned invalid structured output. Try again.");
        }
    }

    private static bool HasRemoteHost(JsonElement element) =>
        (element.TryGetProperty("remote_host", out var host)
            && host.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(host.GetString()))
        || (element.TryGetProperty("remote_model", out var remote)
            && remote.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(remote.GetString()));
}
