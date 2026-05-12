// =====================================================================
// COPY VERBATIM TỪ MEMBER's CODE — pure Domain (no Revit refs).
//
// Sources:
//   d:/MEP Add-in/Plumbing/member/DrainageBranchRouting/src/DrainageBranchRouting.Domain/ValueObjects/Point3D.cs
//   d:/MEP Add-in/Plumbing/member/DrainageBranchRouting/src/DrainageBranchRouting.Domain/Constants/DrainageConstants.cs
//   d:/MEP Add-in/Plumbing/member/DrainageBranchRouting/src/DrainageBranchRouting.Domain/Algorithms/BranchRouteCalculator.cs
//
// Mục đích:
//   1. Tool `golden-capture` chạy code này → produce JSON fixtures (Phase 2.1).
//   2. MEPAuto.Server.Tests link file này (Compile Include) → dùng làm "member baseline"
//      cho golden test (Phase 2.2-2.3 sanity check).
//   3. SAU Batch 3 (port Domain xong), test sẽ chuyển sang reference
//      `MEPAuto.Server.DrainageBranchRouting.Domain.BranchRouteCalculator` thay vì
//      embedded — file này có thể delete khi đó.
//
// NGUYÊN TẮC: KHÔNG sửa logic ở file này. Bất kỳ thay đổi sẽ làm fixture lệch baseline.
// =====================================================================

using System;
using System.Text.Json.Serialization;

namespace MEPAuto.GoldenCapture.Embedded;

public sealed class Point3D
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public Point3D() { }

    [JsonConstructor]
    public Point3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double DistanceTo(Point3D other)
    {
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1})";
}

public static class DrainageConstants
{
    public const double TEE_Y_OFFSET_MM = 200.0;
    public const double TEE_Y_ANGLE_RAD = System.Math.PI / 4;
    public const double STANDARD_SLOPE = 0.01;
    public const double SEGMENT_45_UP_LENGTH_MM = 108.0;
    public const double VERTICAL_DN50_SEGMENT_MM = 175.0;
    public const double CAP_END_OFFSET_MM = 100.0;
    public const double ELBOW_DIMENSION_MM = 28.0;
    public const double MIN_HORIZONTAL_LENGTH_MM = 100.0;
    public const double DN50_MM = 50.0;
    public const double DN40_MM = 40.0;
    public const double MM_TO_FEET = 1.0 / 304.8;
}

public class BranchRouteCalculator
{
    public BranchRouteResult Calculate(Point3D mainPipeStart, Point3D mainPipeEnd, Point3D capEndPoint)
    {
        var result = new BranchRouteResult();

        double perpY = capEndPoint.Y;
        double branchY = perpY - DrainageConstants.TEE_Y_OFFSET_MM;

        double tBranch = (branchY - mainPipeStart.Y) / (mainPipeEnd.Y - mainPipeStart.Y);
        double branchZ = mainPipeStart.Z + tBranch * (mainPipeEnd.Z - mainPipeStart.Z);
        result.BranchPoint = new Point3D(mainPipeStart.X, branchY, branchZ);

        double diagHoriz = DrainageConstants.TEE_Y_OFFSET_MM * Math.Sqrt(2);
        double diagDZ = diagHoriz * DrainageConstants.STANDARD_SLOPE;
        result.DiagonalEnd = new Point3D(
            result.BranchPoint.X + DrainageConstants.TEE_Y_OFFSET_MM,
            perpY,
            result.BranchPoint.Z + diagDZ);

        double seg45Up = DrainageConstants.SEGMENT_45_UP_LENGTH_MM;
        double dx45 = seg45Up * Math.Cos(Math.PI / 4);
        double elbD = DrainageConstants.ELBOW_DIMENSION_MM;
        double vertX = capEndPoint.X + DrainageConstants.CAP_END_OFFSET_MM;

        double horizEndX = vertX - dx45 - elbD * 2;
        double horizDist = horizEndX - result.DiagonalEnd.X;

        if (horizDist < DrainageConstants.MIN_HORIZONTAL_LENGTH_MM)
        {
            horizEndX = result.DiagonalEnd.X + DrainageConstants.TEE_Y_OFFSET_MM;
            vertX = horizEndX + elbD * 2 + dx45;
            horizDist = horizEndX - result.DiagonalEnd.X;
        }

        result.HorizontalEnd = new Point3D(
            horizEndX,
            perpY,
            result.DiagonalEnd.Z + horizDist * DrainageConstants.STANDARD_SLOPE);

        double cos45 = Math.Cos(Math.PI / 4);
        double sin45 = Math.Sin(Math.PI / 4);
        result.Segment45UpEnd = new Point3D(
            result.HorizontalEnd.X + cos45 * seg45Up,
            perpY,
            result.HorizontalEnd.Z + sin45 * seg45Up);

        result.VerticalBottom = new Point3D(
            result.Segment45UpEnd.X + elbD,
            perpY,
            result.Segment45UpEnd.Z + elbD);

        result.TransitionPoint = new Point3D(
            result.VerticalBottom.X,
            perpY,
            result.VerticalBottom.Z + DrainageConstants.VERTICAL_DN50_SEGMENT_MM);

        result.VerticalTop = new Point3D(
            result.VerticalBottom.X,
            perpY,
            capEndPoint.Z);

        result.CapEndPoint = capEndPoint;

        return result;
    }
}

public class BranchRouteResult
{
    public Point3D BranchPoint { get; set; } = new(0, 0, 0);
    public Point3D DiagonalEnd { get; set; } = new(0, 0, 0);
    public Point3D HorizontalEnd { get; set; } = new(0, 0, 0);
    public Point3D Segment45UpEnd { get; set; } = new(0, 0, 0);
    public Point3D VerticalBottom { get; set; } = new(0, 0, 0);
    public Point3D TransitionPoint { get; set; } = new(0, 0, 0);
    public Point3D VerticalTop { get; set; } = new(0, 0, 0);
    public Point3D CapEndPoint { get; set; } = new(0, 0, 0);
}
