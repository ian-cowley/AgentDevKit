using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Glacier.AgentDevKit.Adk;

public class OpenAiService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly TextWriter? _debugWriter;

    public OpenAiService(string baseUrl = "https://api.openai.com/v1", string? apiKey = null,
        TimeSpan? timeout = null, TextWriter? debugWriter = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _debugWriter = debugWriter;
        _httpClient = new HttpClient
        {
            Timeout = timeout ?? TimeSpan.FromMinutes(5)
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    public async Task<LlmResponse> GenerateContentAsync(LlmRequest request)
    {
        var url = $"{_baseUrl}/chat/completions";
        var payload = MapToOpenAiRequest(request, stream: false);
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        _debugWriter?.WriteLine($"[DEBUG] Raw API response: {responseJson}");
        try
        {
            var doc = JsonNode.Parse(responseJson);
            return MapFromOpenAiResponse(doc);
        }
        catch (JsonException)
        {
            // Return a response that triggers the agent's retry logic
            return new LlmResponse { 
                FunctionCalls = new List<LlmFunctionCall> { 
                    new LlmFunctionCall { 
                        Name = "error", 
                        Args = JsonNode.Parse("{\"error\": \"The server returned malformed JSON. Please try again.\"}")! 
                    } 
                } 
            };
        }
    }

    public async IAsyncEnumerable<LlmResponse> StreamGenerateContentAsync(LlmRequest request)
    {
        var url = $"{_baseUrl}/chat/completions";
        var payload = MapToOpenAiRequest(request, stream: true);
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
            
            var data = line.Substring(6).Trim();
            if (data == "[DONE]") break;

            yield return TrySafeParseStreamResponse(data);
        }
    }

    private LlmResponse TrySafeParseStreamResponse(string data)
    {
        try
        {
            var doc = JsonNode.Parse(data);
            return MapFromOpenAiStreamResponse(doc);
        }
        catch (JsonException)
        {
            return new LlmResponse
            {
                FunctionCalls = new List<LlmFunctionCall> {
                    new LlmFunctionCall {
                        Name = "error", 
                        Args = JsonNode.Parse("{\"error\": \"The server returned malformed JSON stream. Please try again.\"}")!
                    }
                }
            };
        }
    }

    private object MapToOpenAiRequest(LlmRequest request, bool stream)
    {
        var messages = new List<object>();

        if (request.SystemInstruction != null)
        {
            messages.Add(new { role = "system", content = GetTextContent(request.SystemInstruction) });
        }

        foreach (var msg in request.Contents)
        {
            var role = msg.Role switch
            {
                "user" => "user",
                "model" => "assistant",
                "function" => "tool",
                _ => "user"
            };

            var content = GetTextContent(msg);
            var toolCalls = GetToolCalls(msg);

            if (role == "tool")
            {
                // In OpenAI, function responses must be linked to a tool_call_id
                foreach (var part in msg.Parts.Where(p => p.FunctionResponse != null))
                {
                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = part.FunctionResponse!.Name, // We use name as ID for simplicity if not available
                        content = part.FunctionResponse.Response?.ToJsonString() ?? ""
                    });
                }
            }
            else
            {
                messages.Add(new
                {
                    role = role,
                    content = string.IsNullOrEmpty(content) ? null : content,
                    tool_calls = toolCalls?.Any() == true ? toolCalls : null
                });
            }
        }

        var tools = request.Tools.Any() ? request.Tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.GetParametersSchema()
            }
        }).ToList() : null;

        return new
        {
            model = request.Model,
            messages = messages,
            tools = tools,
            tool_choice = tools != null ? "auto" : null,
            stream = stream
        };
    }

    private string? GetTextContent(LlmContent content)
    {
        var texts = content.Parts.Where(p => p.Text != null).Select(p => p.Text);
        return texts.Any() ? string.Join("\n", texts) : null;
    }

    private List<object>? GetToolCalls(LlmContent content)
    {
        var calls = content.Parts.Where(p => p.FunctionCall != null).Select(p => new
        {
            id = p.FunctionCall!.Name, // Simplification
            type = "function",
            function = new
            {
                name = p.FunctionCall.Name,
                arguments = p.FunctionCall.Args?.ToJsonString() ?? "{}"
            }
        });
        return calls.Any() ? calls.Cast<object>().ToList() : null;
    }

    private LlmResponse MapFromOpenAiResponse(JsonNode? doc)
    {
        var message = doc?["choices"]?[0]?["message"];
        var content = message?["content"]?.ToString();
        var toolCalls = message?["tool_calls"]?.AsArray();

        var response = new LlmResponse { Content = content };

        if (toolCalls != null)
        {
            response.FunctionCalls = new List<LlmFunctionCall>();
            foreach (var call in toolCalls)
            {
                response.FunctionCalls.Add(new LlmFunctionCall
                {
                    Name = call?["function"]?["name"]?.ToString() ?? "",
                    Args = TryExtractJson(call?["function"]?["arguments"]?.ToString() ?? "{}") ?? JsonNode.Parse("{\"error\": \"Malformed tool arguments. Please fix the JSON format.\"}")!
                });
            }
        }

        // Usage
        var usage = doc?["usage"];
        if (usage != null)
        {
            response.Usage = new LlmUsage
            {
                PromptTokens = usage["prompt_tokens"]?.GetValue<int>() ?? 0,
                CompletionTokens = usage["completion_tokens"]?.GetValue<int>() ?? 0
            };
            Telemetry.TokenCounter.Add(response.Usage.TotalTokens);
        }

        return response;
    }

    private LlmResponse MapFromOpenAiStreamResponse(JsonNode? doc)
    {
        var delta = doc?["choices"]?[0]?["delta"];
        var content = delta?["content"]?.ToString();
        var toolCalls = delta?["tool_calls"]?.AsArray();

        var response = new LlmResponse { Content = content };

        if (toolCalls != null)
        {
            response.FunctionCalls = new List<LlmFunctionCall>();
            foreach (var call in toolCalls)
            {
                response.FunctionCalls.Add(new LlmFunctionCall
                {
                    Name = call?["function"]?["name"]?.ToString() ?? "",
                    Args = TryExtractJson(call?["function"]?["arguments"]?.ToString() ?? "{}") ?? JsonNode.Parse("{\"error\": \"Malformed tool arguments. Please fix the JSON format.\"}")!
                });
            }
        }

        return response;
    }

    private static JsonNode? TryExtractJson(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        try
        {
            // Simple approach: find first { and last }
            int start = input.IndexOf('{');
            int end = input.LastIndexOf('}');

            if (start != -1 && end != -1 && end > start)
            {
                string json = input.Substring(start, end - start + 1);
                return JsonNode.Parse(json);
            }

            return JsonNode.Parse(input);
        }
        catch
        {
            return null;
        }
    }
}
