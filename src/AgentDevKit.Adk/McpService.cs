using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Glacier.Agent.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Glacier.AgentDevKit.Adk;

public class McpService : IAsyncDisposable
{
    private readonly ILogger<McpService>? _logger;
    private readonly List<McpClient> _clients = new();
    private readonly List<Process> _processes = new();

    public McpService(ILogger<McpService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers an existing McpClient directly (e.g. for testing with InMemoryMcpTransport).
    /// </summary>
    public async Task<List<ITool>> RegisterClientAsync(McpClient client)
    {
        _clients.Add(client);
        var tools = new List<ITool>();
        var mcpTools = await client.ListToolsAsync();
        foreach (var tool in mcpTools)
        {
            _logger?.LogInformation("  Found tool: {ToolName}", tool.Name);
            tools.Add(new McpTool(client, tool));
        }
        return tools;
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

                var psi = new ProcessStartInfo
                {
                    FileName = server.Value.Command,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var arg in server.Value.Args)
                {
                    psi.ArgumentList.Add(arg);
                }

                foreach (var env in server.Value.Env)
                {
                    psi.Environment[env.Key] = env.Value;
                }

                var process = Process.Start(psi);
                if (process == null)
                {
                    _logger?.LogError("Failed to start process for server: {ServerName}", server.Key);
                    continue;
                }

                _processes.Add(process);

                var transport = new StreamMcpTransport(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
                var client = new McpClient(transport);
                await client.InitializeAsync("AgentDevKit.Adk", "1.0.5");

                _clients.Add(client);

                // Discover all tools on this server
                var mcpTools = await client.ListToolsAsync();
                foreach (var tool in mcpTools)
                {
                    _logger?.LogInformation("  Found tool: {ToolName}", tool.Name);
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
            try
            {
                await client.DisposeAsync();
            }
            catch
            {
            }
        }
        _clients.Clear();

        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
                process.Dispose();
            }
            catch
            {
            }
        }
        _processes.Clear();
    }
}
