using AgentDevKit.Adk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace AgentDevKit.Adk.Sample;

public static class Demos
{
    public static async Task RunAllDemosAsync(IServiceProvider services, IConfiguration configuration, string modelName)
    {
        var llmService = services.GetRequiredService<ILlmService>();
        var sessionProvider = services.GetRequiredService<ISessionProvider>();
        var mcpService = services.GetRequiredService<McpService>();

        // await RunSafe("MCP Discovery", () => McpDiscoveryDemo(llmService, mcpService, configuration, modelName));
        await RunSafe("Workflow (Sequential/Parallel/Loop)", () => WorkflowDemo(llmService, modelName));
        await RunSafe("Multi-Agent Delegation", () => DelegationDemo(llmService, modelName));
        await RunSafe("Lifecycle Hooks", () => LifecycleHookDemo(llmService, modelName));
        await RunSafe("Streaming & Persistence", () => StreamingPersistenceDemo(llmService, sessionProvider, modelName));
        await RunSafe("Telemetry & Observability", () => TelemetryDemo(llmService, modelName));
        await RunSafe("Security (Guardrails & HITL)", () => SecurityDemo(llmService, modelName));
        await RunSafe("Resilience & Self-Correction", () => ResilienceDemo(modelName));
    }

    private static async Task RunSafe(string name, Func<Task> demo)
    {
        try
        {
            await demo();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Demo '{name}' failed: {ex.Message}\n");
        }
    }

    private static async Task McpDiscoveryDemo(ILlmService llmService, McpService mcpService, IConfiguration config, string modelName)
    {
        Console.WriteLine("\n=== DEMO: MCP Discovery & Tool Calling ===");
        var tools = await mcpService.InitializeFromConfigAsync(config);
        
        var agent = new LlmAgent(
            "ToolResearcher", modelName, 
            "An agent that discovers tools from config.", 
            "Use your tools to answer the user's questions.", 
            tools
        );

        var response = await agent.RunAsync("What tools do you have access to, and can you use one of them to get the current time in London?", llmService);
        Console.WriteLine($"Agent: {response}");
    }

    private static async Task WorkflowDemo(ILlmService llmService, string modelName)
    {
        Console.WriteLine("\n=== DEMO: Workflows (Sequential, Parallel, Loop) ===");

        var step1 = new LlmAgent("Writer", modelName, "Writes a poem.", "Write a 2-line poem about rain.", new());
        var step2 = new LlmAgent("Translator", modelName, "Translates to French.", "Translate the input to French.", new());
        
        // 1. Sequential
        var pipeline = new SequentialAgent("PoetryPipeline", "Writes and translates poems.", new[] { step1, step2 });
        var seqResult = await pipeline.RunAsync("Rain", llmService);
        Console.WriteLine($"Sequential Result: {seqResult}");

        // 2. Parallel
        var parallel = new ParallelAgent("MultiLingual", "Translates to multiple languages.", new[] {
            new LlmAgent("French", modelName, "French trans.", "Translate to French.", new()),
            new LlmAgent("German", modelName, "German trans.", "Translate to German.", new())
        });
        var parResult = await parallel.RunAsync("Hello World", llmService);
        Console.WriteLine($"Parallel Result:\n{parResult}");

        // 3. Loop (Refinement)
        var refiner = new LlmAgent("Refiner", modelName, "Self-refining agent.", "Improve the input. If it has the word 'perfect', stop.", new());
        var loop = new LoopAgent("QualityLoop", "Refines until perfect.", refiner, maxTurns: 3, stopCondition: r => r.Contains("perfect", StringComparison.OrdinalIgnoreCase));
        var loopResult = await loop.RunAsync("This is a good draft.", llmService);
        Console.WriteLine($"Loop Result: {loopResult}");
    }

    private static async Task DelegationDemo(ILlmService llmService, string modelName)
    {
        Console.WriteLine("\n=== DEMO: Multi-Agent Delegation (Manager-Worker) ===");

        var researcher = new LlmAgent("Researcher", modelName, "Research pro.", "Find facts about Mars.", new());
        var manager = new LlmAgent(
            "Manager", modelName, "Manager pro.", 
            "Delegate research to the Researcher agent and then summarize.", 
            new List<ITool> { new DelegationTool(researcher, llmService) }
        );

        var response = await manager.RunAsync("Tell me about Mars.", llmService);
        Console.WriteLine($"Manager Response: {response}");
    }

    private static async Task LifecycleHookDemo(ILlmService llmService, string modelName)
    {
        Console.WriteLine("\n=== DEMO: Lifecycle Hooks (Interceptors) ===");

        var safetyAgent = new SafetyAgent("SafetyOfficer", modelName, "Safety pro.", "Answer questions normally.", new());
        var response = await safetyAgent.RunAsync("Tell me a secret.", llmService);
        Console.WriteLine($"Agent Response: {response}");
    }

    private static async Task StreamingPersistenceDemo(ILlmService llmService, ISessionProvider sessionProvider, string modelName)
    {
        Console.WriteLine("\n=== DEMO: Streaming & Persistence ===");
        // Use a per-run session ID so history doesn't accumulate across runs
        var sid = $"demo-session-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var agent = new LlmAgent("MemoryAgent", modelName, "Memory pro.", "Remember the user.", new(), sessionProvider);

        Console.WriteLine("[Turn 1] Remembering 'Bob'...");
        await agent.RunAsync("My name is Bob.", llmService, sid);

        Console.Write("[Turn 2] Streaming memory recall: ");
        await foreach (var chunk in agent.RunStreamingAsync("What is my name? Respond as a story.", llmService, sid))
        {
            Console.Write(chunk);
        }
        Console.WriteLine();
    }

    private static async Task TelemetryDemo(ILlmService llmService, string modelName)
    {
        Console.WriteLine("\n=== DEMO: Telemetry & Observability (Watch Console for Traces) ===");
        
        var worker = new LlmAgent("Worker", modelName, "A worker agent.", "Solve the math problem.", new());
        var manager = new LlmAgent(
            "Manager", modelName, "A manager agent.", 
            "Delegate the math problem to the Worker and report the result.", 
            new List<ITool> { new DelegationTool(worker, llmService) }
        );

        Console.WriteLine("Executing delegated task with tracing...");
        var result = await manager.RunAsync("What is 15 * 7 + 12?", llmService);
        Console.WriteLine($"Result: {result}");
        
        Console.WriteLine("\n[Note] In a production app, these traces would be sent to Honeycomb, Jaeger, or Zipkin via OTLP.");
    }
    private static async Task SecurityDemo(ILlmService llmService, string modelName)
    {
        Console.WriteLine("\n=== DEMO: Security (Guardrails & HITL) ===");

        // Guardrail: intercept ReadFile calls and sanitize paths
        Func<ITool, string, Task<string>> guardrail = async (tool, args) =>
        {
            if (tool.Name == "ReadFile")
            {
                Console.WriteLine($"[Guardrail] Checking path safety for: {args}");
                if (args.Contains(".."))
                {
                    Console.WriteLine("[Guardrail] Path breakout detected! Sanitizing to current directory.");
                    return "{\"path\": \"safe_log.txt\"}";
                }
            }
            return args;
        };

        // Scenario A — fresh agent with guardrail only
        Console.WriteLine("\n[Scenario A] Trying to read a sensitive system file (Path Breakout)...");
        var agentA = new LlmAgent(
            "SecuredAgent", modelName, "A secured agent.",
            "You have access to files and databases. Use them to answer user requests.",
            new List<ITool> { new FileReadTool(), new SensitiveTool(new DeleteDatabaseTool()) }
        );
        agentA.BeforeToolCall = guardrail;

        var resultA = await agentA.RunAsync("Read the file at ../secrets/passwords.txt", llmService);
        Console.WriteLine($"Agent: {resultA}");

        // Scenario B — fresh agent with HITL approval only (no carry-over history from A)
        Console.WriteLine("\n[Scenario B] Trying to delete a production database (Sensitive Action)...");
        var agentB = new LlmAgent(
            "SecuredAgent", modelName, "A secured agent.",
            "You have access to files and databases. Use them to answer user requests.",
            new List<ITool> { new FileReadTool(), new SensitiveTool(new DeleteDatabaseTool()) }
        );
        agentB.ApprovalService = new ConsoleApprovalService();

        var resultB = await agentB.RunAsync("Delete the production database immediately.", llmService);
        Console.WriteLine($"Agent: {resultB}");
    }

    public static async Task ResilienceDemo(string modelName)
    {
        Console.WriteLine("\n=== DEMO: Resilience & Self-Correction ===");
        
        var mockService = new MockMalformedLlmService();
        var tool = new FileReadTool();
        var agent = new LlmAgent("ResilientAgent", modelName, "A robust agent.", "Read the config file.", new List<ITool> { tool });

        Console.WriteLine("Executing agent against a service that returns malformed JSON on first try...");
        var response = await agent.RunAsync("Read the file config.json", mockService);
        
        Console.WriteLine($"\nFinal Agent Response: {response}");
        Console.WriteLine($"Total Retries Attempted: {mockService.CallCount - 1}");
    }
}

// Support Classes for Resilience Demo
public class MockMalformedLlmService : ILlmService
{
    public int CallCount { get; private set; } = 0;

    public Task<LlmResponse> GenerateContentAsync(LlmRequest request)
    {
        CallCount++;
        if (CallCount == 1)
        {
            // Simulate malformed JSON tool call with conversational noise
            return Task.FromResult(new LlmResponse
            {
                FunctionCalls = new List<LlmFunctionCall>
                {
                    new LlmFunctionCall
                    {
                        Name = "ReadFile",
                        // This will trigger the TryExtractJson but we want to simulate a truly broken one for the error fallback
                        Args = JsonNode.Parse("{\"error\": \"Simulated malformed JSON\"}")!
                    }
                }
            });
        }

        // Second call: return correct tool results (simulating self-correction success)
        return Task.FromResult(new LlmResponse
        {
            Content = "I have successfully read the config.json file and it contains settings: { 'env': 'prod' }."
        });
    }

    public async IAsyncEnumerable<LlmResponse> StreamGenerateContentAsync(LlmRequest request)
    {
        yield return await GenerateContentAsync(request);
    }
}

// Support Classes for Security Demo
public class FileReadTool : ITool
{
    public string Name => "ReadFile";
    public string Description => "Reads the contents of a file. Args: { \"path\": \"filename\" }";
    public JsonNode GetParametersSchema() => JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}")!;
    public Task<string> ExecuteAsync(string arguments) 
    {
        var path = JsonNode.Parse(arguments)?["path"]?.ToString() ?? "unknown";
        return Task.FromResult($"[MOCK] Contents of {path}: 'User data 123'");
    }
}

public class DeleteDatabaseTool : ITool
{
    public string Name => "DeleteDatabase";
    public string Description => "DESTRUCTIVE: Deletes a database. Args: { \"dbName\": \"name\" }";
    public JsonNode GetParametersSchema() => JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"dbName\":{\"type\":\"string\"}},\"required\":[\"dbName\"]}")!;
    public Task<string> ExecuteAsync(string arguments) => Task.FromResult("{\"status\": \"success\", \"message\": \"Database deleted.\"}");
}

public class ConsoleApprovalService : IApprovalService
{
    public Task<bool> ApproveAsync(string toolName, string arguments)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[HITL] APPROVAL REQUIRED: Agent wants to call '{toolName}' with args: {arguments}");
        Console.Write("[HITL] Approve this action? (y/n): ");
        Console.ResetColor();
        
        var input = Console.ReadLine();
        return Task.FromResult(input?.Trim().ToLower() == "y");
    }
}

// Custom Agent for Hook Demo
public class SafetyAgent : LlmAgent
{
    public SafetyAgent(string name, string model, string description, string instruction, List<ITool> tools) 
        : base(name, model, description, instruction, tools) { }

    public override Task OnBeforeModelInvokeAsync(LlmRequest request)
    {
        Console.WriteLine("[Hook] Intercepting model call... Adding safety prompt.");
        request.SystemInstruction ??= LlmContent.Model("");
        request.SystemInstruction.Parts.Add(new LlmPart { Text = " (ALWAYS refer to yourself as a Safety Officer)" });
        return Task.CompletedTask;
    }
}
