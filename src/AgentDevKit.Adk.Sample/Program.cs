using Glacier.AgentDevKit.Adk;
using Glacier.AgentDevKit.Adk.Sample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Resolve connection settings — prefer CLI args, then environment variables, then localhost defaults
var llmBaseUrl = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("ADK_LLM_BASE_URL") ?? "http://localhost:11434/v1";
var modelName = args.Length > 1
    ? args[1]
    : Environment.GetEnvironmentVariable("ADK_LLM_MODEL") ?? "google/gemma-4-26b-a4b";
var llmApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

// Setup Configuration (template + optional local override)
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.template.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

// Setup Telemetry — traces and metrics go to a file; one summary line on the console
var telemetryLogPath = Path.GetFullPath("telemetry.log");
var telemetryWriter = new StreamWriter(telemetryLogPath, append: false) { AutoFlush = true };

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("AgentSdk.Demo"))
    .AddSource("Glacier.AgentDevKit.Adk")
    .AddFileExporter(telemetryWriter)
    .Build();

var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("AgentSdk.Demo"))
    .AddMeter("Glacier.AgentDevKit.Adk")
    .AddFileExporter(telemetryWriter)
    .Build();

Console.WriteLine($"[Telemetry] Traces and metrics → {telemetryLogPath}");
Console.WriteLine($"[LLM]       {llmBaseUrl}  model={modelName}");

var services = new ServiceCollection()
    .AddSingleton<IConfiguration>(configuration)
    .AddSingleton<ILlmService>(_ => new OpenAiService(llmBaseUrl, apiKey: llmApiKey, timeout: TimeSpan.FromMinutes(5), debugWriter: telemetryWriter))
    .AddSingleton<ISessionProvider, MemorySessionProvider>()
    .AddSingleton<McpService>()
    .AddLogging(builder => builder.AddConsole())
    .BuildServiceProvider();

Console.WriteLine("=== Glacier.AgentDevKit.Adk COMPREHENSIVE DEMO SUITE ===");
await Demos.RunAllDemosAsync(services, configuration, modelName);

// Cleanup — flush telemetry before exit so the final metric collection lands in the file
var mcpService = services.GetRequiredService<McpService>();
await mcpService.DisposeAsync();
meterProvider.Dispose();
tracerProvider.Dispose();
await telemetryWriter.DisposeAsync();
