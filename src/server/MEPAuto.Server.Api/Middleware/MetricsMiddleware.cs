using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MEPAuto.Server.Api.Middleware
{
    /// <summary>
    /// In-memory metrics: total requests, per-endpoint counter, latency p50/p95/p99 trong rolling window.
    /// Cố ý đơn giản — KHÔNG Prometheus format. Đủ cho LEAD debug + monitor qua /metrics.
    /// </summary>
    public class MetricsCollector
    {
        private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(5);
        private const int MaxSamples = 10_000;

        private readonly DateTime _startUtc = DateTime.UtcNow;
        private long _totalRequests;
        private long _totalErrors;
        private readonly ConcurrentDictionary<string, EndpointStats> _byEndpoint = new();
        private readonly ConcurrentQueue<Sample> _samples = new();
        private readonly object _trimLock = new();

        public DateTime StartedAtUtc => _startUtc;
        public TimeSpan Uptime => DateTime.UtcNow - _startUtc;
        public long TotalRequests => System.Threading.Interlocked.Read(ref _totalRequests);
        public long TotalErrors => System.Threading.Interlocked.Read(ref _totalErrors);

        public void Record(string method, string path, int statusCode, long elapsedMs)
        {
            System.Threading.Interlocked.Increment(ref _totalRequests);
            if (statusCode >= 500) System.Threading.Interlocked.Increment(ref _totalErrors);

            var key = $"{method} {NormalizePath(path)}";
            _byEndpoint.AddOrUpdate(key,
                _ => new EndpointStats { Count = 1, ErrorCount = statusCode >= 400 ? 1 : 0, TotalMs = elapsedMs },
                (_, s) => { s.Count++; if (statusCode >= 400) s.ErrorCount++; s.TotalMs += elapsedMs; return s; });

            _samples.Enqueue(new Sample(DateTime.UtcNow, elapsedMs));
            TrimWindow();
        }

        public MetricsSnapshot Snapshot()
        {
            TrimWindow();
            var samples = _samples.Select(s => s.LatencyMs).OrderBy(x => x).ToArray();
            return new MetricsSnapshot
            {
                StartedAtUtc = _startUtc,
                UptimeSeconds = (long)Uptime.TotalSeconds,
                TotalRequests = TotalRequests,
                TotalErrors = TotalErrors,
                Window = new WindowStats
                {
                    Minutes = (int)WindowSize.TotalMinutes,
                    Samples = samples.Length,
                    P50Ms = Percentile(samples, 50),
                    P95Ms = Percentile(samples, 95),
                    P99Ms = Percentile(samples, 99),
                },
                ByEndpoint = _byEndpoint.ToDictionary(
                    kv => kv.Key,
                    kv => new EndpointSnapshot
                    {
                        Count = kv.Value.Count,
                        Errors = kv.Value.ErrorCount,
                        AvgMs = kv.Value.Count == 0 ? 0 : (long)(kv.Value.TotalMs / (double)kv.Value.Count),
                    })
            };
        }

        private void TrimWindow()
        {
            if (!System.Threading.Monitor.TryEnter(_trimLock)) return;
            try
            {
                var cutoff = DateTime.UtcNow - WindowSize;
                while (_samples.TryPeek(out var head) && (head.At < cutoff || _samples.Count > MaxSamples))
                    _samples.TryDequeue(out _);
            }
            finally { System.Threading.Monitor.Exit(_trimLock); }
        }

        private static long Percentile(long[] sorted, int p)
        {
            if (sorted.Length == 0) return 0;
            var idx = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
            return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                if (Guid.TryParse(segments[i], out _) || (segments[i].Length > 8 && segments[i].All(c => char.IsLetterOrDigit(c) || c == '-')))
                    segments[i] = "{id}";
            }
            return "/" + string.Join('/', segments);
        }

        private class EndpointStats { public long Count; public long ErrorCount; public long TotalMs; }
        private record struct Sample(DateTime At, long LatencyMs);

        public class MetricsSnapshot
        {
            public DateTime StartedAtUtc { get; set; }
            public long UptimeSeconds { get; set; }
            public long TotalRequests { get; set; }
            public long TotalErrors { get; set; }
            public WindowStats Window { get; set; } = new();
            public Dictionary<string, EndpointSnapshot> ByEndpoint { get; set; } = new();
        }

        public class WindowStats { public int Minutes { get; set; } public int Samples { get; set; } public long P50Ms { get; set; } public long P95Ms { get; set; } public long P99Ms { get; set; } }
        public class EndpointSnapshot { public long Count { get; set; } public long Errors { get; set; } public long AvgMs { get; set; } }
    }

    public class MetricsMiddleware
    {
        private readonly RequestDelegate _next;
        public MetricsMiddleware(RequestDelegate next) { _next = next; }

        public async Task Invoke(HttpContext ctx, MetricsCollector collector)
        {
            var path = ctx.Request.Path.Value ?? "/";
            if (path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }

            var sw = Stopwatch.StartNew();
            try { await _next(ctx); }
            finally
            {
                sw.Stop();
                collector.Record(ctx.Request.Method, path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        }
    }
}
