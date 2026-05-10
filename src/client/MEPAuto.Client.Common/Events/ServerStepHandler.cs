using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Contracts;
using Newtonsoft.Json;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// ExternalEvent handler — nhận lệnh từ luồng nền (JobPollerService) đẩy xuống, marshal sang Revit UI thread,
    /// resolve Contract qua FeatureName, execute, callback kết quả.
    /// Dùng chung cho cả AI mode và CAD/PDF mode (cả 2 đều "server đẩy step xuống client").
    /// </summary>
    /// <remarks>
    /// Đăng ký 1 lần ở Shell (RevitApp.OnStartup) qua <see cref="ServerStepDispatcher.Bind"/>.
    /// Luồng nền gọi <c>ServerStepDispatcher.Dispatch(stepRequest)</c> để enqueue + raise event.
    /// </remarks>
    public class ServerStepHandler : IExternalEventHandler
    {
        private readonly IContractRegistry _registry;
        private readonly Func<UIApplication, IFeatureContext> _contextFactory;
        private readonly ConcurrentQueue<StepRequest> _queue = new ConcurrentQueue<StepRequest>();

        public ServerStepHandler(IContractRegistry registry, Func<UIApplication, IFeatureContext> contextFactory)
        {
            _registry = registry;
            _contextFactory = contextFactory;
        }

        /// <summary>Luồng nền call (qua dispatcher facade) để đẩy step vào queue.</summary>
        public void Enqueue(StepRequest req) => _queue.Enqueue(req);

        public void Execute(UIApplication app)
        {
            // Drain queue — xử lý mọi step đang chờ. Mỗi step độc lập try/catch để 1 fail không block các step sau.
            while (_queue.TryDequeue(out var req))
            {
                try
                {
                    var ctx = _contextFactory(app);
                    var contract = _registry.Resolve(req.FeatureName);
                    object? input = null;
                    if (!string.IsNullOrEmpty(req.InputJson))
                    {
                        input = JsonConvert.DeserializeObject(req.InputJson, contract.InputType);
                    }
                    if (input == null)
                    {
                        req.OnComplete?.Invoke(null, new InvalidOperationException(
                            $"Step '{req.FeatureName}' input deserialize null từ JSON: {req.InputJson}"));
                        continue;
                    }
                    var output = contract.Execute(ctx, input);
                    req.OnComplete?.Invoke(output, null);
                }
                catch (Exception ex)
                {
                    req.OnComplete?.Invoke(null, ex);
                }
            }
        }

        public string GetName() => "MEPAuto.ServerStep";
    }
}
