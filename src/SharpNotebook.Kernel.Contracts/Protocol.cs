using System.Text.Json;

namespace SharpNotebook.Kernel.Contracts;

public static class Protocol
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(KernelRequest request) => JsonSerializer.Serialize(request, JsonOptions);
    public static string Serialize(KernelEvent evt) => JsonSerializer.Serialize(evt, JsonOptions);

    public static KernelRequest? DeserializeRequest(string line) =>
        JsonSerializer.Deserialize<KernelRequest>(line, JsonOptions);

    public static KernelEvent? DeserializeEvent(string line) =>
        JsonSerializer.Deserialize<KernelEvent>(line, JsonOptions);
}
