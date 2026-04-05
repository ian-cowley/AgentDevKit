using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentDevKit.Adk;

public class McpService : IAsyncDisposable
{
    private readonly ILogger<McpService>? _logger;
    private readonly List<McpClient> _clients = new();

    public McpService(ILogger<McpService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<List<ITool>> InitializeFromConfigAsync(IConfiguration configuration)
    {
        var settings = configuration.Get<McpSettings>();
        var allTools = new List<ITool>();

        if (settings == null || settings.McpServers == null) return allTools;

        foreach (var server in settings.McpServers)
        {
            try
            {
                _logger?.LogInformation("Initializing MCP server: {ServerName}", server.Key);
                
                var options = new StdioClientTransportOptions
                {
                    Command = server.Value.Command,
                    Arguments = server.Value.Args.ToArray(),
                    EnvironmentVariables = server.Value.Env.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value)
                };

                var transport = new StdioClientTransport(options);
                var client = await McpClient.CreateAsync(transport);
                
                _clients.Add(client);

                // Discover all tools on this server
                var mcpTools = await client.ListToolsAsync();
                foreach (var tool in mcpTools)
                {
                    _logger?.LogInformation("  Found tool: {ToolName}", tool.Name);
                    // Map McpTool to our ITool interface
                    allTools.Add(new McpTool(client, tool));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize MCP server: {ServerName}", server.Key);
            }
        }

        return allTools;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }
}
