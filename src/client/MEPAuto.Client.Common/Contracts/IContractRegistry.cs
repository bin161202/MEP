using System.Collections.Generic;

namespace MEPAuto.Client.Common.Contracts
{
    /// <summary>
    /// Sổ đăng ký <see cref="IFeatureContract"/> — tra theo tên feature ra Contract tương ứng.
    /// Implementation thật (<c>ContractRegistry</c>) ở <c>MEPAuto.Client.Shell</c> dùng reflection scan
    /// — tách ra Shell vì Common không reference Shell, nhưng feature DLL chỉ load được ở runtime của Shell.
    /// </summary>
    public interface IContractRegistry
    {
        /// <summary>Tra Contract theo tên feature. Throw nếu không có.</summary>
        IFeatureContract Resolve(string featureName);

        /// <summary>Có Contract với tên này không?</summary>
        bool TryResolve(string featureName, out IFeatureContract? contract);

        /// <summary>Liệt kê mọi Contract đã đăng ký (dùng cho diagnostic + LangGraph tools/list endpoint).</summary>
        IEnumerable<IFeatureContract> All();
    }
}
