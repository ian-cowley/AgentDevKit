using Glacier.AgentDevKit.Adk;
using Glacier.AgentDevKit.Adk.Sample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// Resolve connection settings — prefer CLI args, fall back to defaults
var llmBaseUrl = args.Length > 0 ? args[0] : "http://192.168.1.200:1234/v1";
var modelName  = args.Length > 1 ? args[1] : "google/gemma-4-26b-a4b";

// Setup Configuration
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
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
    .Configure<McpSettings>(configuration.GetSection("McpServers"))
    .AddSingleton<ILlmService>(sp => new OpenAiService(llmBaseUrl, timeout: TimeSpan.FromMinutes(5), debugWriter: telemetryWriter))
    .AddSingleton<ISessionProvider, SqliteSessionProvider>()
    .AddSingleton<McpService>()
    .AddLogging(builder => builder.AddConsole())
    .BuildServiceProvider();

// Run All Demos
Console.WriteLine("=== Glacier.AgentDevKit.Adk COMPREHENSIVE DEMO SUITE (LOCAL LLM: GEMMA-4) ===");
await Demos.RunAllDemosAsync(services, configuration, modelName);

// Cleanup — flush telemetry before exit so the final metric collection lands in the file
var mcpService = services.GetRequiredService<McpService>();
await mcpService.DisposeAsync();
meterProvider.Dispose();
tracerProvider.Dispose();
await telemetryWriter.DisposeAsync();
Environment.Exit(0);
