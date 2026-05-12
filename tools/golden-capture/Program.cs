// Golden fixture capture tool — chạy member's BranchRouteCalculator (nhúng ở EmbeddedDomain.cs)
// với 3 input đại diện và xuất JSON fixture.
//
// Dùng cho Phase 2 test pin: code port (Batch 3) phải reproduce same waypoints ≤1e-6 mm.

using System;
using System.IO;
using System.Text.Json;
using MEPAuto.GoldenCapture.Embedded;

namespace MEPAuto.GoldenCapture;

internal static class Program
{
    static int Main(string[] args)
    {
        var outputDir = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                           "tests", "MEPAuto.Server.Tests", "Fixtures", "DrainageBranch");
        outputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(outputDir);

        var scenarios = new (string Name, Point3D Start, Point3D End, Point3D Cap)[]
        {
            (
                Name: "horizontal_main",
                Start: new Point3D(0,    0, 0),
                End:   new Point3D(0, 5000, 0),
                Cap:   new Point3D(2500, 3000, 2700)
            ),
            (
                Name: "sloped_main",
                Start: new Point3D(0,    0,   0),
                End:   new Point3D(0, 5000, -50),
                Cap:   new Point3D(2500, 3000, 2700)
            ),
            (
                Name: "short_distance",
                Start: new Point3D(0,    0, 0),
                End:   new Point3D(0, 1000, 0),
                Cap:   new Point3D(500, 800, 2700)
            ),
        };

        var calc = new BranchRouteCalculator();
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        foreach (var s in scenarios)
        {
            var result = calc.Calculate(s.Start, s.End, s.Cap);
            var fixture = new GoldenFixture
            {
                Name = s.Name,
                Input = new GoldenInput { Start = s.Start, End = s.End, Cap = s.Cap },
                Expected = new GoldenExpected
                {
                    BranchPoint     = result.BranchPoint,
                    DiagonalEnd     = result.DiagonalEnd,
                    HorizontalEnd   = result.HorizontalEnd,
                    Segment45UpEnd  = result.Segment45UpEnd,
                    VerticalBottom  = result.VerticalBottom,
                    TransitionPoint = result.TransitionPoint,
                    VerticalTop     = result.VerticalTop,
                    CapEndPoint     = result.CapEndPoint,
                },
            };
            var path = Path.Combine(outputDir, $"golden_{s.Name}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(fixture, jsonOpts));
            Console.WriteLine($"Wrote {path}");
        }

        return 0;
    }
}

public sealed class GoldenFixture
{
    public string Name { get; set; } = "";
    public GoldenInput Input { get; set; } = new();
    public GoldenExpected Expected { get; set; } = new();
}

public sealed class GoldenInput
{
    public Point3D Start { get; set; } = new(0, 0, 0);
    public Point3D End { get; set; } = new(0, 0, 0);
    public Point3D Cap { get; set; } = new(0, 0, 0);
}

public sealed class GoldenExpected
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
