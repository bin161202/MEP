using System;
using MEPAuto.Contracts.Manifests;
using MEPAuto.SheetFromExcel.Commands;

namespace MEPAuto.SheetFromExcel.Manifest
{
    public class SheetFromExcelManifest : IFeatureManifest
    {
        public string Name => "SheetFromExcel";
        public string DisplayName => "Sheet From Excel";
        public string ServerEndpoint => "/api/v1/sheetfromexcel/execute";
        public string LicenseFeature => "sheetfromexcel.basic";
        public string PanelGroup => "MEPAuto - General";
        public int Order => 50;
        public string IconResourcePath => "Icons/sheetfromexcel.png";
        public Type CommandType => typeof(SheetFromExcelCommand);
    }
}
