using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Glacier.AgentDevKit.Adk;

public class GeminiService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiService(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    public async Task<LlmResponse> GenerateContentAsync(LlmRequest request)
    {
        var modelPath = request.Model.StartsWith("models/") ? request.Model : $"models/{request.Model}";
        var url = $"https://generativelanguage.googleapis.com/v1beta/{modelPath}:generateContent?key={_apiKey}";
        return await SendRequestAsync(url, request);
    }

    public async IAsyncEnumerable<LlmResponse> StreamGenerateContentAsync(LlmRequest request)
    {
        var modelPath = request.Model.StartsWith("models/") ? request.Model : $"models/{request.Model}";
        var url = $"https://generativelanguage.googleapis.com/v1beta/{modelPath}:streamGenerateContent?key={_apiKey}";

        var payload = CreatePayload(request);
        var jsonPayload = JsonSerializer.Serialize(payload);
        var contentPayload = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = contentPayload };
        using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Gemini streaming returns a JSON array of objects. We need to parse each object.
        // For simplicity, we can read the stream and find complete JSON objects if the SDK doesn't do it for us.
        // However, Utf8JsonReader can handle multiple root-level objects if we use it correctly.
        // But Gemini actually returns one big JSON array for streamGenerateContent?
        // Actually, it's often SSE or a JSON array.
        
        // Let's use a simple approach for now: Buffered reading of the JSON array.
        var json = await reader.ReadToEndAsync();
        var doc = JsonNode.Parse(json);
        if (doc is JsonArray array)
        {
            foreach (var item in array)
            {
                yield return ParseResponse(item);
            }
        }
        else if (doc != null)
        {
            yield return ParseResponse(doc);
        }
    }

    private object CreatePayload(LlmRequest request)
    {
        return new
        {
            system_instruction = request.SystemInstruction,
            contents = request.Contents,
            tools = request.Tools.Any() ? new[] { new { function_declarations = request.Tools.Select(t => new { name = t.Name, description = t.Description, parameters = t.GetParametersSchema() }) } } : null
        };
    }

    private async Task<LlmResponse> SendRequestAsync(string url, LlmRequest request)
    {
        var payload = CreatePayload(request);
        var jsonPayload = JsonSerializer.Serialize(payload);
        var contentPayload = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(url, contentPayload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonNode.Parse(json);
        return ParseResponse(doc);
    }

    private LlmResponse ParseResponse(JsonNode? doc)
    {
        var candidate = doc?["candidates"]?[0];
        var content = candidate?["content"];
        var parts = content?["parts"]?.AsArray();

        var llmResponse = new LlmResponse();
        
        if (parts != null)
        {
            foreach (var part in parts)
            {
                if (part?["text"] != null)
                {
                    llmResponse.Content += part["text"]!.ToString();
                }
                
                if (part?["functionCall"] != null)
                {
                    llmResponse.FunctionCalls ??= new List<LlmFunctionCall>();
                    llmResponse.FunctionCalls.Add(new LlmFunctionCall
                    {
                        Name = part["functionCall"]!["name"]!.ToString(),
                        Args = part["functionCall"]!["args"]
                    });
                }
            }
        }

        // --- Usage Telemetry ---
        var usage = doc?["usageMetadata"];
        if (usage != null)
        {
            llmResponse.Usage = new LlmUsage
            {
                PromptTokens = usage["promptTokenCount"]?.GetValue<int>() ?? 0,
                CompletionTokens = usage["candidatesTokenCount"]?.GetValue<int>() ?? 0
            };
            
            if (llmResponse.Usage.TotalTokens > 0)
            {
                Telemetry.TokenCounter.Add(llmResponse.Usage.TotalTokens);
            }
        }

        return llmResponse;
    }
}
