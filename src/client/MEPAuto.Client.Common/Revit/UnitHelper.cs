namespace MEPAuto.Client.Common.Revit
{
    /// <summary>
    /// Convert giữa millimeter (đơn vị Client/UI/server giao tiếp) và feet (đơn vị nội bộ Revit API).
    /// </summary>
    public static class UnitHelper
    {
        public const double MM_PER_FOOT = 304.8;

        public static double FtToMm(double feet) => feet * MM_PER_FOOT;
        public static double MmToFt(double mm) => mm / MM_PER_FOOT;
    }
}
