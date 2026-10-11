using System.Diagnostics;
using System.Net.Http.Headers;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;

namespace Natee.Infrastructure.Network;

/// <summary>
/// Executes an explicit, user-triggered download and upload bandwidth test over HTTPS
/// against the configured endpoint (defaults to Cloudflare speed.cloudflare.com/__down and /__up).
/// Never runs automatically in the background and never fabricates measurements.
/// </summary>
public sealed class CloudflareSpeedTestService : ISpeedTestService
{
    private readonly HttpClient _httpClient;

    public CloudflareSpeedTestService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Natee", "1.0.0"));
    }

    public async Task<SpeedTestResult> RunSpeedTestAsync(
        NateeSettings settings,
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var overallSw = Stopwatch.StartNew();
        int timeoutSeconds = Math.Clamp(settings.SpeedTestTimeoutSeconds, 5, 120);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var token = timeoutCts.Token;

        string downloadUrl = settings.SpeedTestDownloadUrl;
        string uploadUrl = settings.SpeedTestUploadUrl;
        int uploadPayloadSize = Math.Clamp(settings.SpeedTestUploadBytes, 100_000, 50_000_000);

        try
        {
            // Phase 1: Latency probe to the speed test host
            progress?.Report(new SpeedTestProgress("Measuring endpoint latency...", 5, 0));
            double? latencyMs = null;
            if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var dlUri))
            {
                var probe = await PlatformNetworkMetricsProvider.ProbeHostAsync(dlUri.Host, 2500, token);
                latencyMs = probe.RoundTripTimeMs;
            }

            // Phase 2: Download throughput measurement
            progress?.Report(new SpeedTestProgress("Measuring download bandwidth...", 15, 0));
            long totalBytesDownloaded = 0;
            var dlSw = Stopwatch.StartNew();

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                long expectedBytes = response.Content.Headers.ContentLength ?? 15_000_000;
                await using var stream = await response.Content.ReadAsStreamAsync(token);

                byte[] buffer = new byte[65536];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
                {
                    totalBytesDownloaded += bytesRead;
                    double currentMbps = NetworkClassifier.CalculateThroughputMbps(0, totalBytesDownloaded, dlSw.Elapsed);
                    double pct = 15 + Math.Min(40, (totalBytesDownloaded * 40.0) / Math.Max(1, expectedBytes));
                    progress?.Report(new SpeedTestProgress("Downloading test payload...", Math.Round(pct, 0), currentMbps));
                }
            }
            dlSw.Stop();

            double downloadMbps = NetworkClassifier.CalculateThroughputMbps(0, totalBytesDownloaded, dlSw.Elapsed);

            // Phase 3: Upload throughput measurement (streamed in chunks to avoid LOH allocation)
            progress?.Report(new SpeedTestProgress("Measuring upload bandwidth...", 60, downloadMbps));
            var ulSw = Stopwatch.StartNew();
            await using (var uploadStream = new SyntheticUploadStream(uploadPayloadSize, bytesSent =>
            {
                double currentUlMbps = NetworkClassifier.CalculateThroughputMbps(0, bytesSent, ulSw.Elapsed);
                double pct = 60 + Math.Min(35, (bytesSent * 35.0) / Math.Max(1, uploadPayloadSize));
                progress?.Report(new SpeedTestProgress("Uploading test payload...", Math.Round(pct, 0), currentUlMbps));
            }))
            using (var content = new StreamContent(uploadStream, bufferSize: 65536))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Headers.ContentLength = uploadPayloadSize;
                using var uploadResponse = await _httpClient.PostAsync(uploadUrl, content, token);
                uploadResponse.EnsureSuccessStatusCode();
            }
            ulSw.Stop();

            double uploadMbps = NetworkClassifier.CalculateThroughputMbps(0, uploadPayloadSize, ulSw.Elapsed);
            overallSw.Stop();

            progress?.Report(new SpeedTestProgress("Speed test complete.", 100, uploadMbps));

            return new SpeedTestResult
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                ProviderName = settings.SpeedTestProviderName,
                DownloadEndpoint = downloadUrl,
                UploadEndpoint = uploadUrl,
                DownloadSpeedMbps = downloadMbps,
                UploadSpeedMbps = uploadMbps,
                LatencyMs = latencyMs,
                BytesDownloaded = totalBytesDownloaded,
                BytesUploaded = uploadPayloadSize,
                Duration = overallSw.Elapsed,
                IsSuccess = true,
                ErrorMessage = null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            overallSw.Stop();
            return new SpeedTestResult
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                ProviderName = settings.SpeedTestProviderName,
                DownloadEndpoint = downloadUrl,
                UploadEndpoint = uploadUrl,
                DownloadSpeedMbps = 0,
                UploadSpeedMbps = 0,
                LatencyMs = null,
                BytesDownloaded = 0,
                BytesUploaded = 0,
                Duration = overallSw.Elapsed,
                IsSuccess = false,
                ErrorMessage = $"Speed test timed out after {timeoutSeconds} seconds."
            };
        }
        catch (Exception ex)
        {
            overallSw.Stop();
            return new SpeedTestResult
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                ProviderName = settings.SpeedTestProviderName,
                DownloadEndpoint = downloadUrl,
                UploadEndpoint = uploadUrl,
                DownloadSpeedMbps = 0,
                UploadSpeedMbps = 0,
                LatencyMs = null,
                BytesDownloaded = 0,
                BytesUploaded = 0,
                Duration = overallSw.Elapsed,
                IsSuccess = false,
                ErrorMessage = $"Speed test failed: {ex.Message}"
            };
        }
    }

    private sealed class SyntheticUploadStream : Stream
    {
        private readonly long _totalBytes;
        private readonly Action<long>? _onProgress;
        private readonly byte[] _pattern = new byte[4096];
        private long _position;

        public SyntheticUploadStream(long totalBytes, Action<long>? onProgress)
        {
            _totalBytes = totalBytes;
            _onProgress = onProgress;
            Random.Shared.NextBytes(_pattern);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            long remaining = _totalBytes - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            int toCopy = (int)Math.Min(buffer.Length, remaining);
            int copied = 0;
            while (copied < toCopy)
            {
                int chunk = Math.Min(_pattern.Length, toCopy - copied);
                _pattern.AsSpan(0, chunk).CopyTo(buffer.Slice(copied, chunk));
                copied += chunk;
            }

            _position += toCopy;
            _onProgress?.Invoke(_position);
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
