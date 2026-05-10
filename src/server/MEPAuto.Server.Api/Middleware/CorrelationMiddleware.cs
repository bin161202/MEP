using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace MEPAuto.Server.Api.Middleware
{
    /// <summary>
    /// Gắn RequestId (header X-Request-Id từ client hoặc TraceIdentifier) + UserId (JWT sub claim)
    /// vào Serilog LogContext → mọi log trong scope request có 2 field này.
    /// Phải đặt SAU UseAuthentication() để claims đã được resolve.
    /// </summary>
    public class CorrelationMiddleware
    {
        private const string RequestIdHeader = "X-Request-Id";
        private readonly RequestDelegate _next;

        public CorrelationMiddleware(RequestDelegate next) { _next = next; }

        public async Task Invoke(HttpContext ctx)
        {
            var requestId = ctx.Request.Headers.TryGetValue(RequestIdHeader, out var v) && !string.IsNullOrEmpty(v.ToString())
                ? v.ToString()
                : ctx.TraceIdentifier;

            var userId = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? ctx.User?.FindFirst("sub")?.Value
                ?? "anonymous";

            ctx.Response.Headers[RequestIdHeader] = requestId;

            using (LogContext.PushProperty("RequestId", requestId))
            using (LogContext.PushProperty("UserId", userId))
            {
                await _next(ctx);
            }
        }
    }
}
