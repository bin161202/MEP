using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MEPAuto.Client.Common.Contracts;

namespace MEPAuto.Client.Shell.Contracts
{
    /// <summary>
    /// Implementation của <see cref="IContractRegistry"/> bằng reflection scan.
    /// Quét mọi DLL <c>MEPAuto.*.dll</c> trong thư mục cài → tìm class implement <see cref="IFeatureContract"/>
    /// → tạo instance bằng <see cref="Activator.CreateInstance"/> → lưu vào dictionary theo
    /// <see cref="IFeatureContract.FeatureName"/>.
    /// </summary>
    /// <remarks>
    /// Pattern giống <see cref="Ribbon.RibbonBuilder.ScanManifests"/> — mỗi feature mới tự được nhặt khi DLL có mặt,
    /// KHÔNG cần đăng ký thủ công.
    /// </remarks>
    public class ContractRegistry : IContractRegistry
    {
        private readonly Dictionary<string, IFeatureContract> _map;

        public ContractRegistry()
        {
            _map = new Dictionary<string, IFeatureContract>(StringComparer.OrdinalIgnoreCase);
            ScanAndRegister();
        }

        public IFeatureContract Resolve(string featureName)
        {
            if (!_map.TryGetValue(featureName, out var contract))
                throw new InvalidOperationException(
                    $"Không tìm thấy IFeatureContract tên '{featureName}'. " +
                    $"Đã đăng ký: {string.Join(", ", _map.Keys)}");
            return contract;
        }

        public bool TryResolve(string featureName, out IFeatureContract? contract)
        {
            return _map.TryGetValue(featureName, out contract);
        }

        public IEnumerable<IFeatureContract> All() => _map.Values;

        private void ScanAndRegister()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(ContractRegistry).Assembly.Location)
                              ?? throw new InvalidOperationException("Không xác định được folder assembly.");

            foreach (var dll in Directory.GetFiles(assemblyDir, "MEPAuto.*.dll"))
            {
                Assembly asm;
                try
                {
                    asm = Assembly.LoadFrom(dll);
                }
                catch
                {
                    // DLL hỏng / không load được — skip để không chặn add-in load
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                }

                foreach (var type in types)
                {
                    if (!typeof(IFeatureContract).IsAssignableFrom(type)) continue;
                    if (type.IsAbstract || type.IsInterface) continue;
                    try
                    {
                        if (Activator.CreateInstance(type) is IFeatureContract instance)
                        {
                            _map[instance.FeatureName] = instance;
                        }
                    }
                    catch
                    {
                        // Constructor throw — skip Contract đó, không chặn các Contract khác
                    }
                }
            }
        }
    }
}
