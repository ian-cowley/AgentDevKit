using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentDevKit.Adk;

public class LlmAgent : IAgent
{
    public string Name { get; }
    public string Model { get; }
    public string Description { get; }
    public string Instruction { get; }
    public List<ITool> Tools { get; }
    public List<LlmContent> History { get; } = new();
    
    public IApprovalService? ApprovalService { get; set; }
    public Func<ITool, string, Task<string>>? BeforeToolCall { get; set; }
    public int MaxRetries { get; set; } = 3;

    private int _currentRetryCount = 0;
    private readonly ISessionProvider? _sessionProvider;

    public LlmAgent(string name, string model, string description, string instruction, List<ITool> tools, ISessionProvider? sessionProvider = null)
    {
        Name = name;
        Model = model;
        Description = description;
        Instruction = instruction;
        Tools = tools;
        _sessionProvider = sessionProvider;
    }

    public virtual Task OnBeforeModelInvokeAsync(LlmRequest request) => Task.CompletedTask;
    public virtual Task OnAfterModelInvokeAsync(LlmResponse response) => Task.CompletedTask;
    public virtual Task OnBeforeToolInvokeAsync(ITool tool, string arguments) => Task.CompletedTask;
    public virtual Task OnAfterToolInvokeAsync(ITool tool, string result) => Task.CompletedTask;

    public async Task<string> RunAsync(string prompt, ILlmService llmService, string? sessionId = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity($"Run {Name}");
        activity?.SetTag("agent.name", Name);
        activity?.SetTag("agent.model", Model);
        activity?.SetTag("session.id", sessionId);

        Telemetry.AgentRunsCounter.Add(1, new KeyValuePair<string, object?>("agent", Name));

        await LoadHistoryAsync(sessionId);
        
        var userMessage = LlmContent.User(prompt);
        History.Add(userMessage);
        _currentRetryCount = 0;
        await SaveMessageAsync(sessionId, userMessage);

        while (true)
        {
            var request = CreateRequest();
            await OnBeforeModelInvokeAsync(request);
            
            var response = await llmService.GenerateContentAsync(request);
            
            await OnAfterModelInvokeAsync(response);

            if (response.FunctionCalls?.Any() == true)
            {
                var hasError = response.FunctionCalls.Any(c => c.Args?["error"] != null);
                if (hasError && _currentRetryCount < MaxRetries)
                {
                    _currentRetryCount++;
                    var errorMsg = response.FunctionCalls.First(c => c.Args?["error"] != null).Args?["error"]?.ToString();
                    var feedback = LlmContent.User($"System: Your last tool call was malformed. Error: {errorMsg}. Please try again and provide ONLY valid JSON arguments.");
                    History.Add(feedback);
                    await SaveMessageAsync(sessionId, feedback);
                    continue;
                }

                await HandleToolCallsAsync(response.FunctionCalls, sessionId);
                continue;
            }

            if (!string.IsNullOrEmpty(response.Content))
            {
                var modelMessage = LlmContent.Model(response.Content);
                History.Add(modelMessage);
                await SaveMessageAsync(sessionId, modelMessage);
                return response.Content;
            }

            break;
        }

        return "No response generated.";
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(string prompt, ILlmService llmService, string? sessionId = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity($"Stream {Name}");
        activity?.SetTag("agent.name", Name);
        activity?.SetTag("session.id", sessionId);

        Telemetry.AgentRunsCounter.Add(1, new KeyValuePair<string, object?>("agent", Name));

        await LoadHistoryAsync(sessionId);
        
        var userMessage = LlmContent.User(prompt);
        History.Add(userMessage);
        _currentRetryCount = 0;
        await SaveMessageAsync(sessionId, userMessage);

        while (true)
        {
            var request = CreateRequest();
            await OnBeforeModelInvokeAsync(request);

            var fullContent = "";
            List<LlmFunctionCall>? toolCalls = null;

            await foreach (var chunk in llmService.StreamGenerateContentAsync(request))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    fullContent += chunk.Content;
                    yield return chunk.Content;
                }
                
                if (chunk.FunctionCalls?.Any() == true)
                {
                    toolCalls ??= new List<LlmFunctionCall>();
                    toolCalls.AddRange(chunk.FunctionCalls);
                }
            }

            await OnAfterModelInvokeAsync(new LlmResponse { Content = fullContent, FunctionCalls = toolCalls });

            if (toolCalls?.Any() == true)
            {
                var hasError = toolCalls.Any(c => c.Args?["error"] != null);
                if (hasError && _currentRetryCount < MaxRetries)
                {
                    _currentRetryCount++;
                    var errorMsg = toolCalls.First(c => c.Args?["error"] != null).Args?["error"]?.ToString();
                    var feedback = LlmContent.User($"System: Your last tool call was malformed. Error: {errorMsg}. Please try again and provide ONLY valid JSON arguments.");
                    History.Add(feedback);
                    await SaveMessageAsync(sessionId, feedback);
                    continue;
                }

                await HandleToolCallsAsync(toolCalls, sessionId);
                continue;
            }

            if (!string.IsNullOrEmpty(fullContent))
            {
                var modelMessage = LlmContent.Model(fullContent);
                History.Add(modelMessage);
                await SaveMessageAsync(sessionId, modelMessage);
            }

            break;
        }
    }

    private LlmRequest CreateRequest()
    {
        return new LlmRequest
        {
            Model = Model,
            SystemInstruction = LlmContent.Model(Instruction),
            Tools = Tools,
            Contents = History
        };
    }

    private async Task HandleToolCallsAsync(List<LlmFunctionCall> toolCalls, string? sessionId)
    {
        using var toolActivity = Telemetry.ActivitySource.StartActivity($"ToolCalls {Name}");
        
        var responseParts = new List<LlmPart>();
        var toolResultParts = new List<LlmPart>();

        foreach (var call in toolCalls)
        {
            responseParts.Add(new LlmPart { FunctionCall = call });
            
            var tool = Tools.FirstOrDefault(t => t.Name == call.Name);
            string? result = null;
            if (tool != null)
            {
                using var callActivity = Telemetry.ActivitySource.StartActivity($"Invoke {tool.Name}");
                Telemetry.ToolExecutionCounter.Add(1, new KeyValuePair<string, object?>("tool", tool.Name));

                try
                {
                    var arguments = call.Args?.ToJsonString() ?? "{}";
                    
                    // 1. Interceptor/Guardrail
                    if (BeforeToolCall != null)
                    {
                        arguments = await BeforeToolCall(tool, arguments);
                    }

                    // 2. HITL Approval
                    if (tool is SensitiveTool && ApprovalService != null)
                    {
                        var approved = await ApprovalService.ApproveAsync(tool.Name, arguments);
                        if (!approved)
                        {
                            result = "{\"status\": \"error\", \"message\": \"Action rejected by user.\"}";
                        }
                    }

                    if (result == null)
                    {
                        await OnBeforeToolInvokeAsync(tool, arguments);
                        result = await tool.ExecuteAsync(arguments);
                        await OnAfterToolInvokeAsync(tool, result);
                    }
                    
                    callActivity?.SetTag("status", "success");
                }
                catch (Exception ex)
                {
                    callActivity?.SetTag("status", "error");
                    callActivity?.SetTag("error", ex.Message);
                    result = $"{{\"status\": \"error\", \"message\": \"{ex.Message}\"}}";
                }
                
                toolResultParts.Add(new LlmPart 
                { 
                    FunctionResponse = new LlmFunctionResponse 
                    { 
                        Name = call.Name, 
                        Response = TryParseOrWrap(result) 
                    } 
                });
            }
        }

        var modelTurn = new LlmContent { Role = "model", Parts = responseParts };
        var functionTurn = new LlmContent { Role = "function", Parts = toolResultParts };

        History.Add(modelTurn);
        History.Add(functionTurn);
        
        await SaveMessageAsync(sessionId, modelTurn);
        await SaveMessageAsync(sessionId, functionTurn);
    }

    private async Task LoadHistoryAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || _sessionProvider == null) return;
        if (History.Any()) return; // Already loaded or manually set

        var history = await _sessionProvider.GetHistoryAsync(sessionId);
        History.AddRange(history);
    }

    private async Task SaveMessageAsync(string? sessionId, LlmContent message)
    {
        if (string.IsNullOrEmpty(sessionId) || _sessionProvider == null) return;
        await _sessionProvider.SaveMessageAsync(sessionId, message);
    }

    private static JsonNode TryParseOrWrap(string? result)
    {
        if (!string.IsNullOrEmpty(result))
        {
            try { return JsonNode.Parse(result)!; }
            catch { /* not JSON – fall through to wrap */ }
        }
        return JsonNode.Parse($"{{\"result\": {JsonSerializer.Serialize(result ?? "")}}}")!;
    }
}
