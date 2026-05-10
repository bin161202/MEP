namespace MEPAuto.Contracts.DTOs
{
    /// <summary>Điểm 3D đơn vị mm. Convert sang feet (Revit internal) qua UnitHelper.</summary>
    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Point3D() { }
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>Vector 3D đơn vị mm.</summary>
    public class Vector3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Vector3D() { }
        public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
}
