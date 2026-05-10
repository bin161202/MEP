using System;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Contracts;
using MEPAuto.Contracts.DTOs;
using MEPAuto.HelloWorld.Commands;

namespace MEPAuto.HelloWorld.Contracts
{
    /// <summary>
    /// Contract HEADLESS cho feature HelloWorld.
    /// Cho phép luồng nền (AI/CAD-PDF mode) gọi feature mà không cần ribbon click.
    /// Wrap <see cref="HelloWorldCommand.ExecuteHeadless"/> — KHÔNG duplicate logic.
    /// </summary>
    public class HelloWorldContract : IFeatureContract
    {
        public string FeatureName => "HelloWorld";
        public Type InputType => typeof(HelloWorldRequest);

        public object Execute(IFeatureContext ctx, object input)
        {
            if (input is not HelloWorldRequest req)
                throw new ArgumentException(
                    $"HelloWorldContract.Execute expected HelloWorldRequest, got {input?.GetType().Name ?? "null"}",
                    nameof(input));

            return HelloWorldCommand.ExecuteHeadless(ctx, req);
        }
    }
}
