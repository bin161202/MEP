using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if NET48
namespace System.Runtime.CompilerServices
{
    /// <summary>Polyfill cho net48 — C# 9 ModuleInitializer marker. Compiler emits ::.cctor → Initialize() chạy module load time.
    /// net8.0 đã có sẵn type này → shim chỉ áp dụng khi target net48 (Revit 2022-2024).</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
#endif

namespace MEPAuto.Client.IntegrationTests
{
    /// <summary>
    /// Module initializer cho test runner ngoài Revit context:
    /// 1. <c>SetDllDirectory</c> trỏ Revit install dir → native deps (ASM, RevitNet, …) load được.
    /// 2. Preload <c>RevitAPI.dll</c> + <c>RevitAPIUI.dll</c> từ Revit dir → tránh probing path conflict
    ///    (test bin có copy nhưng dependencies chuỗi cần load từ cùng dir để khớp ASM versions).
    /// 3. <see cref="AppDomain.AssemblyResolve"/> → fallback resolve khác managed Revit DLL.
    /// </summary>
    internal static class ModuleInit
    {
        // Chọn Revit install dir theo compile-time constant REVIT_{ver} (set bởi csproj DefineConstants).
        // Default fallback 2024 để dev local không pass config vẫn build/run được.
#if REVIT_2022
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2022";
#elif REVIT_2023
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2023";
#elif REVIT_2024
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2024";
#elif REVIT_2025
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2025";
#elif REVIT_2026
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2026";
#elif REVIT_2027
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2027";
#else
        private const string RevitDir = @"C:\Program Files\Autodesk\Revit 2024";
#endif

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [ModuleInitializer]
        internal static void Initialize()
        {
            if (Directory.Exists(RevitDir))
            {
                SetDllDirectory(RevitDir);

                // Preload bộ Revit DLL từ Revit dir (priority over local bin copy) để dependencies match.
                TryLoad(Path.Combine(RevitDir, "RevitAPI.dll"));
                TryLoad(Path.Combine(RevitDir, "RevitAPIUI.dll"));
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolveRevitAssembly;
        }

        private static void TryLoad(string path)
        {
            try { if (File.Exists(path)) Assembly.LoadFrom(path); } catch { /* swallow — fallback resolver sẽ thử lại */ }
        }

        private static Assembly? ResolveRevitAssembly(object? sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name)) return null;
            var path = Path.Combine(RevitDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
