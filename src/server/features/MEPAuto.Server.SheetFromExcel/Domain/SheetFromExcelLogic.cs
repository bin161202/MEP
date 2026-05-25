using System;

namespace MEPAuto.Server.SheetFromExcel.Domain
{
    public static class SheetFromExcelLogic
    {
        public static string BuildJobId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
