using Autodesk.Revit.DB;

namespace MEPAuto.Client.Common.Revit
{
    /// <summary>
    /// Compat shim cho ElementId: Revit 2022-2024 dùng <c>ElementId.IntegerValue</c> (int);
    /// Revit 2025+ dùng <c>ElementId.Value</c> (long).
    /// <para>
    /// TUYỆT ĐỐI không gọi <c>.IntegerValue</c> hoặc <c>.Value</c> trực tiếp ở nơi khác —
    /// luôn đi qua adapter này. CI lint <c>tools/verify-elementid-usage.ps1</c> sẽ fail build
    /// nếu phát hiện vi phạm.
    /// </para>
    /// </summary>
    public static class ElementIdAdapter
    {
        // CS0618: Revit 2024 đã deprecate IntegerValue (vẫn còn hoạt động đến 2025 mới remove).
        // File này là điểm DUY NHẤT trong codebase được phép gọi IntegerValue / Value, nên suppress local.
#pragma warning disable CS0618

        public static long GetValue(ElementId id)
        {
#if REVIT_LONG_ID
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId Create(long value)
        {
#if REVIT_LONG_ID
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

#pragma warning restore CS0618
    }
}
