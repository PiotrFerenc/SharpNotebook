using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace SharpNotebook.Services;

public interface IAiCodeGenerator
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
}

/// <summary>Turns a plain-English prompt into C# script code via the official OpenAI SDK's ChatClient.
/// Reads the key from the OPENAI_API_KEY environment variable (picked up automatically by
/// IConfiguration's environment-variables provider on both Web and Desktop) — never from the .sharpnb
/// file or app source.</summary>
public sealed class OpenAiCodeGenerator(IConfiguration configuration) : IAiCodeGenerator
{
    private const string Model = "gpt-3.5-turbo";

    private const string SystemPrompt =
        "You are a C# code generator for a Jupyter-style REPL that runs cells via Roslyn Scripting. " +
        "Output ONLY raw C# statements/expressions implementing the user's request. " +
        "No markdown code fences, no explanations, no surrounding text.";

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var apiKey = configuration["OPENAI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        ChatClient client = new(Model, apiKey);
        List<ChatMessage> messages = [new SystemChatMessage(SystemPrompt), new UserChatMessage(prompt)];

        var completion = await client.CompleteChatAsync(messages, cancellationToken: ct);
        var text = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "";
        return StripCodeFence(text.Trim());
    }

    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```"))
            return text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0)
            return text;

        text = text[(firstNewline + 1)..];
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0)
            text = text[..lastFence];

        return text.Trim();
    }
}
