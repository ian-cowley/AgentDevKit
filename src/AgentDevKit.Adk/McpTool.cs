using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentDevKit.Adk;

public class McpTool : ITool
{
    private readonly McpClient _client;
    private readonly McpClientTool _mcpTool;
    public string Name => _mcpTool.Name;
    public string Description => _mcpTool.Description ?? string.Empty;

    public McpTool(McpClient client, McpClientTool mcpTool)
    {
        _client = client;
        _mcpTool = mcpTool;
    }

    public JsonNode GetParametersSchema()
    {
        // In v1.2.0, the schema is accessible via ProtocolTool.InputSchema
        return JsonNode.Parse(_mcpTool.ProtocolTool.InputSchema.ToString())!;
    }

    public async Task<string> ExecuteAsync(string arguments)
    {
        try
        {
            var argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments);
            var result = await _client.CallToolAsync(Name, argsDict);
            
            if (result.IsError == true)
            {
                return $"{{\"status\": \"error\", \"message\": \"Tool reported an error.\"}}";
            }

            var outputs = new List<string>();
            foreach (var content in result.Content)
            {
                if (content is TextContentBlock textBlock)
                {
                    outputs.Add(textBlock.Text);
                }
                // Handle other content types if necessary (e.g. Image, Resource)
            }

            return string.Join("\n", outputs);
        }
        catch (Exception ex)
        {
            return $"{{\"status\": \"error\", \"message\": \"{ex.Message}\"}}";
        }
    }
}
