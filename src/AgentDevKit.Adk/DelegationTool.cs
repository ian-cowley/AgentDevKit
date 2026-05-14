using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace Glacier.AgentDevKit.Adk;

/// <summary>
/// Wraps an IAgent as an ITool, enabling delegation (Manager -> Worker).
/// </summary>
public class DelegationTool : ITool
{
    private readonly IAgent _agent;
    private readonly ILlmService _llmService;

    public string Name => _agent.Name;
    public string Description => _agent.Description;

    public DelegationTool(IAgent agent, ILlmService llmService)
    {
        _agent = agent;
        _llmService = llmService;
    }

    public JsonNode GetParametersSchema()
    {
        return JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "prompt": {
              "type": "string",
              "description": "The specific task or query to delegate to this specialized agent."
            },
            "sessionId": {
              "type": "string",
              "description": "Optional session ID to maintain worker memory."
            }
          },
          "required": ["prompt"]
        }
        """)!;
    }

    public async Task<string> ExecuteAsync(string arguments)
    {
        try
        {
            var args = JsonNode.Parse(arguments);
            var prompt = args?["prompt"]?.ToString() ?? string.Empty;
            var sessionId = args?["sessionId"]?.ToString();
            
            if (string.IsNullOrWhiteSpace(prompt))
                return "Error: No prompt provided for delegation.";

            // Invoke the sub-agent
            var result = await _agent.RunAsync(prompt, _llmService, sessionId);
            
            return result;
        }
        catch (Exception ex)
        {
            return $"{{\"status\": \"error\", \"message\": \"Delegation failed: {ex.Message}\"}}";
        }
    }
}
