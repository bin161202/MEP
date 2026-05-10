using System;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MEPAuto.Server.Api.Middleware
{
    /// <summary>
    /// Attribute filter check user có license cho feature không.
    /// <code>[Authorize, RequireLicense("helloworld.basic")]</code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class RequireLicenseAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public string Feature { get; }
        public RequireLicenseAttribute(string feature) { Feature = feature; }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new UnauthorizedResult();
                return;
            }
            var licenseSvc = context.HttpContext.RequestServices.GetRequiredService<ILicenseService>();
            if (!await licenseSvc.CanUse(user, Feature))
            {
                context.Result = new ObjectResult(new { error = "license_required", feature = Feature })
                {
                    StatusCode = 403,
                };
            }
        }
    }
}
