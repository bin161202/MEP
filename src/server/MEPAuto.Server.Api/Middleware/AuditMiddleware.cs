using System.Diagnostics;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MEPAuto.Server.Api.Middleware
{
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        public AuditMiddleware(RequestDelegate next) { _next = next; }

        public async Task Invoke(HttpContext ctx, IAuditLogger audit)
        {
            var sw = Stopwatch.StartNew();
            await _next(ctx);
            sw.Stop();

            if (ctx.User?.Identity?.IsAuthenticated == true)
            {
                var status = ctx.Response.StatusCode < 400 ? "ok" : "fail";
                await audit.Log(
                    ctx.User,
                    $"{ctx.Request.Method} {ctx.Request.Path}",
                    new { ctx.Response.StatusCode },
                    status,
                    (int)sw.ElapsedMilliseconds);
            }
        }
    }
}
