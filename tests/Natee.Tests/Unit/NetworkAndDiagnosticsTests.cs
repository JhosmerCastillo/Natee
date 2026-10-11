using Natee.Core.Models;
using Natee.Core.Services;
using Xunit;

namespace Natee.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class NetworkAndDiagnosticsTests
{
    [Fact]
    public void CalculateThroughputMbps_ComputesExpectedRateAndHandlesEdgeCases()
    {
        // 12,500,000 bytes (100,000,000 bits) over 2 seconds = 50.0 Mbps
        double rate = NetworkClassifier.CalculateThroughputMbps(1_000_000, 13_500_000, TimeSpan.FromSeconds(2));
        Assert.Equal(50.0, rate);

        // Counter reset (current < previous) should safely return 0
        Assert.Equal(0.0, NetworkClassifier.CalculateThroughputMbps(5000, 1000, TimeSpan.FromSeconds(2)));

        // Zero or negative interval should safely return 0
        Assert.Equal(0.0, NetworkClassifier.CalculateThroughputMbps(1000, 5000, TimeSpan.Zero));
    }

    [Fact]
    public void CalculateProbeMetrics_ComputesAccurateLatencyAndPacketLoss()
    {
        var samples = new List<ProbeSample>
        {
            new("1.1.1.1", true, 12.0),
            new("8.8.8.8", true, 24.0),
            new("9.9.9.9", false, null, "Timeout"),
            new("1.0.0.1", true, 18.0)
        };

        var (avgLatency, lossPercent) = NetworkClassifier.CalculateProbeMetrics(samples);

        Assert.NotNull(avgLatency);
        Assert.Equal(18.0, avgLatency!.Value);
        Assert.Equal(25.0, lossPercent);
    }

    [Fact]
    public void CalculateProbeMetrics_WhenAllFail_ReturnsNullLatencyAnd100PercentLoss()
    {
        var samples = new List<ProbeSample>
        {
            new("1.1.1.1", false, null, "Timeout"),
            new("8.8.8.8", false, null, "Timeout")
        };

        var (avgLatency, lossPercent) = NetworkClassifier.CalculateProbeMetrics(samples);

        Assert.Null(avgLatency);
        Assert.Equal(100.0, lossPercent);
    }

    [Fact]
    public void EvaluateConnectivity_SingleFailedTargetDoesNotMarkConnectionOffline()
    {
        // 2 of 3 targets succeed; 1 target drops ICMP. Must NOT be classified as Failed (Offline).
        var samples = new List<ProbeSample>
        {
            new("1.1.1.1", true, 15.0),
            new("8.8.8.8", true, 19.0),
            new("9.9.9.9", false, null, "ICMP blocked")
        };

        var (status, summary) = NetworkClassifier.EvaluateConnectivity(
            hasActiveInterface: true,
            hasLocalIp: true,
            samples: samples,
            latencyThresholdMs: 120.0,
            packetLossThresholdPercent: 40.0);

        Assert.Equal(HealthStatus.Healthy, status);
        Assert.Contains("2/3", summary);
    }

    [Fact]
    public void EvaluateConnectivity_DistinguishesFailedRecoveryDegradedAndInconclusiveStates()
    {
        // 1. Offline when all targets fail
        var allFailed = new List<ProbeSample>
        {
            new("1.1.1.1", false, null),
            new("8.8.8.8", false, null),
            new("9.9.9.9", false, null)
        };
        var (failedStatus, _) = NetworkClassifier.EvaluateConnectivity(true, true, allFailed);
        Assert.Equal(HealthStatus.Failed, failedStatus);

        // 2. Recovery when probes succeed
        var allHealthy = new List<ProbeSample>
        {
            new("1.1.1.1", true, 14.0),
            new("8.8.8.8", true, 16.0),
            new("9.9.9.9", true, 18.0)
        };
        var (recoveredStatus, _) = NetworkClassifier.EvaluateConnectivity(true, true, allHealthy);
        Assert.Equal(HealthStatus.Healthy, recoveredStatus);

        // 3. Degraded when latency exceeds threshold
        var highLatency = new List<ProbeSample>
        {
            new("1.1.1.1", true, 210.0),
            new("8.8.8.8", true, 230.0)
        };
        var (degradedStatus, _) = NetworkClassifier.EvaluateConnectivity(true, true, highLatency, latencyThresholdMs: 120.0);
        Assert.Equal(HealthStatus.Degraded, degradedStatus);

        // 4. Inconclusive when no probes have been collected yet
        var (inconclusiveStatus, _) = NetworkClassifier.EvaluateConnectivity(true, true, Array.Empty<ProbeSample>());
        Assert.Equal(HealthStatus.Inconclusive, inconclusiveStatus);
    }

    [Fact]
    public void BuildDiagnosticSummary_DistinguishesDnsFailureFromGatewayAndInternetFailures()
    {
        // Case A: IP connectivity works, but DNS resolution failed
        var dnsFailureChecks = new List<DiagnosticCheckItem>
        {
            new() { Id = "interface", Status = HealthStatus.Healthy },
            new() { Id = "ip_config", Status = HealthStatus.Healthy },
            new() { Id = "gateway", Status = HealthStatus.Healthy },
            new() { Id = "dns_config", Status = HealthStatus.Healthy },
            new() { Id = "dns_resolution", Status = HealthStatus.Failed },
            new() { Id = "internet_reachability", Status = HealthStatus.Healthy },
            new() { Id = "latency", Status = HealthStatus.Healthy },
            new() { Id = "packet_loss", Status = HealthStatus.Healthy }
        };

        var dnsSummary = NetworkClassifier.BuildDiagnosticSummary(dnsFailureChecks);
        Assert.Equal(HealthStatus.Failed, dnsSummary.OverallStatus);
        Assert.Contains("DNS Resolution Failure", dnsSummary.Title);

        // Case B: All checks inconclusive -> Inconclusive overall
        var inconclusiveChecks = new List<DiagnosticCheckItem>
        {
            new() { Id = "gateway", Status = HealthStatus.Inconclusive },
            new() { Id = "route_trace", Status = HealthStatus.Unsupported }
        };

        var incSummary = NetworkClassifier.BuildDiagnosticSummary(inconclusiveChecks);
        Assert.Equal(HealthStatus.Inconclusive, incSummary.OverallStatus);
    }
}
