using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace AgentDevKit.Adk.Sample;

// Extends BaseProcessor<Activity> directly — no abstract wrapper needed.
internal sealed class FileSpanExporter(TextWriter writer) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        writer.WriteLine($"[{DateTimeOffset.UtcNow:o}] [Trace] {activity.DisplayName} | {activity.Duration.TotalMilliseconds:F1}ms | {activity.Status}");
        foreach (var (key, value) in activity.Tags)
            writer.WriteLine($"  {key}={value}");
        writer.Flush();
    }
}

internal sealed class FileMetricExporter(TextWriter writer) : BaseExporter<Metric>
{
    public override ExportResult Export(in Batch<Metric> batch)
    {
        foreach (var metric in batch)
        {
            writer.WriteLine($"[{DateTimeOffset.UtcNow:o}] [Metric] {metric.Name} | {metric.Description}");
            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                var value = metric.MetricType switch
                {
                    MetricType.LongSum or MetricType.LongSumNonMonotonic => point.GetSumLong().ToString(),
                    MetricType.DoubleSum or MetricType.DoubleSumNonMonotonic => $"{point.GetSumDouble():F2}",
                    MetricType.LongGauge => point.GetGaugeLastValueLong().ToString(),
                    MetricType.DoubleGauge => $"{point.GetGaugeLastValueDouble():F2}",
                    _ => "(histogram)"
                };
                writer.WriteLine($"  value={value}");
            }
        }
        writer.Flush();
        return ExportResult.Success;
    }
}

internal static class TelemetryFileExtensions
{
    public static TracerProviderBuilder AddFileExporter(this TracerProviderBuilder builder, TextWriter writer)
        => builder.AddProcessor(new FileSpanExporter(writer));

    public static MeterProviderBuilder AddFileExporter(this MeterProviderBuilder builder, TextWriter writer)
        => builder.AddReader(new PeriodicExportingMetricReader(new FileMetricExporter(writer), exportIntervalMilliseconds: 10_000));
}
