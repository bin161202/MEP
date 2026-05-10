using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using MEPAuto.Contracts.Manifests;

namespace MEPAuto.Client.Shell.Ribbon
{
    /// <summary>
    /// Scan reflection toàn bộ DLL trong thư mục cài đặt → tìm class implement <see cref="IFeatureManifest"/>
    /// → tạo PushButton trên ribbon. Group theo PanelGroup, sort theo Order.
    /// </summary>
    public static class RibbonBuilder
    {
        public const string TabName = "MEPAuto";

        public static void Build(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab tồn tại — skip */ }

            var manifests = ScanManifests();
            var groups = manifests
                .GroupBy(m => string.IsNullOrEmpty(m.PanelGroup) ? "General" : m.PanelGroup)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                RibbonPanel panel;
                try { panel = application.CreateRibbonPanel(TabName, group.Key); }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Panel tồn tại — tìm panel cũ thay vì tạo mới
                    panel = application.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == group.Key)
                            ?? application.CreateRibbonPanel(TabName, group.Key + "_2");
                }

                foreach (var manifest in group.OrderBy(m => m.Order))
                {
                    var asmPath = manifest.CommandType.Assembly.Location;
                    var className = manifest.CommandType.FullName!;
                    var data = new PushButtonData(manifest.Name, manifest.DisplayName, asmPath, className)
                    {
                        ToolTip = manifest.DisplayName,
                    };
                    panel.AddItem(data);
                }
            }
        }

        private static List<IFeatureManifest> ScanManifests()
        {
            var assemblyDir = Path.GetDirectoryName(typeof(RibbonBuilder).Assembly.Location)
                              ?? throw new InvalidOperationException("Không xác định được folder assembly.");
            var manifests = new List<IFeatureManifest>();
            foreach (var dll in Directory.GetFiles(assemblyDir, "MEPAuto.*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetTypes())
                    {
                        if (!typeof(IFeatureManifest).IsAssignableFrom(type)) continue;
                        if (type.IsAbstract || type.IsInterface) continue;
                        var instance = (IFeatureManifest?)Activator.CreateInstance(type);
                        if (instance != null) manifests.Add(instance);
                    }
                }
                catch
                {
                    // DLL hỏng / không load được — skip để không chặn ribbon load
                }
            }
            return manifests;
        }
    }
}
