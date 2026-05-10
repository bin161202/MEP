using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.HelloWorld.Domain;

namespace MEPAuto.Server.HelloWorld.Application
{
    public class HelloWorldService
    {
        private readonly IAuditLogger _audit;
        public HelloWorldService(IAuditLogger audit) { _audit = audit; }

        public async Task<HelloWorldResponse> Execute(HelloWorldRequest req, ClaimsPrincipal user)
        {
            var msg = HelloWorldGreeting.Build(req.Snapshot.UserName);
            var jobId = Guid.NewGuid().ToString("N");
            await _audit.Log(user, "helloworld.execute", new { jobId, req.Snapshot.UserName });
            return new HelloWorldResponse { Message = msg, JobId = jobId };
        }

        public async Task RecordResult(HelloWorldResultRequest req, ClaimsPrincipal user)
        {
            await _audit.Log(user, "helloworld.result", new { req.JobId, req.Success },
                req.Success ? "ok" : "fail");
        }
    }
}
