using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Glacier.Agent.Mcp;

namespace Glacier.AgentDevKit.Adk;

public class McpTool : ITool
{
    private readonly McpClient _client;
    private readonly Glacier.Agent.Mcp.McpTool _mcpTool;

    public string Name => _mcpTool.Name;
    public string Description => _mcpTool.Description ?? string.Empty;

    public McpTool(McpClient client, Glacier.Agent.Mcp.McpTool mcpTool)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _mcpTool = mcpTool ?? throw new ArgumentNullException(nameof(mcpTool));
    }

    public JsonNode GetParametersSchema()
    {
        if (_mcpTool.InputSchema is JsonNode node)
        {
            return node;
        }

        if (_mcpTool.InputSchema is JsonElement element)
        {
            return JsonNode.Parse(element.GetRawText()) ?? new JsonObject();
        }

        string json = JsonSerializer.Serialize(_mcpTool.InputSchema);
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    public async Task<string> ExecuteAsync(string arguments)
    {
        try
        {
            var result = await _client.CallToolAsync(Name, arguments);

            if (result.IsError)
            {
                return $"{{\"status\": \"error\", \"message\": \"Tool reported an error.\"}}";
            }

            var outputs = new List<string>();
            foreach (var content in result.Content)
            {
                if (content.Text != null)
                {
                    outputs.Add(content.Text);
                }
            }

            return string.Join("\n", outputs);
        }
        catch (Exception ex)
        {
            return $"{{\"status\": \"error\", \"message\": \"{ex.Message}\"}}";
        }
    }
}
