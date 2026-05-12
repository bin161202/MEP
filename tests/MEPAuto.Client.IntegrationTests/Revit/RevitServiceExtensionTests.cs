using System;
using System.Linq;
using MEPAuto.Client.Common.Revit;
using MEPAuto.Contracts.DTOs;
using FluentAssertions;
using Xunit;

namespace MEPAuto.Client.IntegrationTests.Revit
{
    /// <summary>
    /// Unit test cho 6 method generic của IRevitService (BreakPipe + Regenerate + CreateTeeY/Elbow/Transition
    /// + ConnectPipeToTeeBranch/ConnectClosest) — verify FakeRevitService log + return id + Elements state.
    /// Không cần Revit running.
    /// </summary>
    public class RevitServiceExtensionTests
    {
        [Fact]
        public void BreakPipe_returns_new_segment_id_and_logs_action()
        {
            var fake = new FakeRevitService();
            var newSegId = fake.BreakPipe("fake-100", new Point3D(150, 200, 50));

            newSegId.Should().StartWith("fake-").And.NotBe("fake-100");
            fake.Elements.Should().ContainKey(newSegId).WhoseValue.Category.Should().Be("Pipe");
            fake.OperationLog.Should().ContainSingle(op => op.StartsWith("BreakPipe"))
                .Which.Should().Contain("pipeId=fake-100").And.Contain("at=(150.0,200.0,50.0)").And.Contain("newSegId=" + newSegId);
        }

        [Fact]
        public void Regenerate_logs_single_entry()
        {
            var fake = new FakeRevitService();
            fake.Regenerate();

            fake.OperationLog.Should().ContainSingle().Which.Should().Be("Regenerate");
        }

        [Fact]
        public void CreateTeeY_returns_new_id_and_registers_fitting()
        {
            var fake = new FakeRevitService();
            var teeId = fake.CreateTeeY(
                mainPipeId: "fake-pipe-1",
                branchPointMm: new Point3D(0, 2800, 0),
                branchDnMm: 50.0,
                systemType: "Domestic",
                levelName: "L1",
                angleRad: Math.PI / 4,
                pipeTypeName: "PVC-D");

            teeId.Should().StartWith("fake-");
            fake.Elements[teeId].Category.Should().Be("Fitting");
            fake.Elements[teeId].FamilyName.Should().Be("Tee-Y");
            fake.Elements[teeId].TypeName.Should().Be("PVC-D");
            fake.OperationLog.Should().ContainSingle(op => op.StartsWith("CreateTeeY"))
                .Which.Should().Contain("mainId=fake-pipe-1").And.Contain("dn=50mm").And.Contain("sys=Domestic")
                .And.Contain("level=L1").And.Contain("angle=0.7854").And.Contain("type=PVC-D");
        }

        [Fact]
        public void CreateElbow_returns_new_id_and_registers_fitting()
        {
            var fake = new FakeRevitService();
            var elbowId = fake.CreateElbow("fake-pipe-1", "fake-pipe-2", new Point3D(200, 3000, 3));

            elbowId.Should().StartWith("fake-");
            fake.Elements[elbowId].FamilyName.Should().Be("Elbow");
            fake.OperationLog.Should().ContainSingle(op => op.StartsWith("CreateElbow"))
                .Which.Should().Be("CreateElbow a=fake-pipe-1 b=fake-pipe-2 at=(200.0,3000.0,3.0)");
        }

        [Fact]
        public void CreateTransition_returns_new_id_and_registers_fitting()
        {
            var fake = new FakeRevitService();
            var transId = fake.CreateTransition("fake-pipe-DN50", "fake-pipe-DN40", new Point3D(2572, 3000, 305));

            transId.Should().StartWith("fake-");
            fake.Elements[transId].FamilyName.Should().Be("Transition");
            fake.OperationLog.Should().ContainSingle(op => op.StartsWith("CreateTransition"))
                .Which.Should().Be("CreateTransition a=fake-pipe-DN50 b=fake-pipe-DN40 at=(2572.0,3000.0,305.0)");
        }

        [Fact]
        public void ConnectPipeToTeeBranch_logs_action()
        {
            var fake = new FakeRevitService();
            fake.ConnectPipeToTeeBranch("fake-pipe-1", "fake-tee-1");

            fake.OperationLog.Should().ContainSingle()
                .Which.Should().Be("ConnectPipeToTeeBranch pipe=fake-pipe-1 tee=fake-tee-1");
        }

        [Fact]
        public void ConnectClosest_logs_with_near_point()
        {
            var fake = new FakeRevitService();
            fake.ConnectClosest("fake-pipe-1", "fake-cap-1", new Point3D(2500.0, 3000.0, 2700.0));

            fake.OperationLog.Should().ContainSingle()
                .Which.Should().Be("ConnectClosest a=fake-pipe-1 b=fake-cap-1 near=(2500.0,3000.0,2700.0)");
        }

        [Fact]
        public void Full_drainage_branch_sequence_inside_RunInTransaction_produces_expected_op_log()
        {
            // Generic op-log smoke test — sequence Tee+Pipe+Elbow+Transition điển hình MEP.
            //   BeginTx → CreateTeeY → CreatePipe×6 → ConnectPipeToTeeBranch+ConnectClosest → CreateElbow×4 → CreateTransition×1 → CommitTx
            var fake = new FakeRevitService();
            const string sysType = "Sanitary";
            const string level = "L1";

            fake.RunInTransaction("MepOpSequence", () =>
            {
                var teeId = fake.CreateTeeY("main-pipe", new Point3D(0, 2800, 0), 50.0, sysType, level, Math.PI / 4);

                var pipe1 = fake.CreatePipe(new Point3D(0, 2800, 0), new Point3D(200, 3000, 3), 50.0, sysType, level);
                fake.ConnectPipeToTeeBranch(pipe1, teeId);

                var pipe2 = fake.CreatePipe(new Point3D(200, 3000, 3), new Point3D(2467, 3000, 26), 50.0, sysType, level);
                var elbow1 = fake.CreateElbow(pipe1, pipe2, new Point3D(200, 3000, 3));

                var pipe3 = fake.CreatePipe(new Point3D(2467, 3000, 26), new Point3D(2544, 3000, 102), 50.0, sysType, level);
                var elbow2 = fake.CreateElbow(pipe2, pipe3, new Point3D(2467, 3000, 26));

                var pipe4 = fake.CreatePipe(new Point3D(2572, 3000, 130), new Point3D(2572, 3000, 305), 50.0, sysType, level);
                var elbow3 = fake.CreateElbow(pipe3, pipe4, new Point3D(2572, 3000, 130));

                var pipe5 = fake.CreatePipe(new Point3D(2572, 3000, 305), new Point3D(2572, 3000, 2700), 40.0, sysType, level);
                var trans = fake.CreateTransition(pipe4, pipe5, new Point3D(2572, 3000, 305));

                var pipe6 = fake.CreatePipe(new Point3D(2572, 3000, 2700), new Point3D(2500, 3000, 2700), 40.0, sysType, level);
                var elbow4 = fake.CreateElbow(pipe5, pipe6, new Point3D(2572, 3000, 2700));
                fake.ConnectClosest(pipe6, "cap-end", new Point3D(2500, 3000, 2700));
            });

            fake.OperationLog.First().Should().StartWith("BeginTransaction");
            fake.OperationLog.Last().Should().StartWith("CommitTransaction");

            fake.OperationLog.Count(op => op.StartsWith("CreateTeeY")).Should().Be(1);
            fake.OperationLog.Count(op => op.StartsWith("CreatePipe")).Should().Be(6);
            fake.OperationLog.Count(op => op.StartsWith("CreateElbow")).Should().Be(4);
            fake.OperationLog.Count(op => op.StartsWith("CreateTransition")).Should().Be(1);
            fake.OperationLog.Count(op => op.StartsWith("ConnectPipeToTeeBranch")).Should().Be(1);
            fake.OperationLog.Count(op => op.StartsWith("ConnectClosest")).Should().Be(1);

            // Total elements registered: 1 tee + 6 pipe + 4 elbow + 1 trans = 12
            fake.Elements.Count.Should().Be(12);
            fake.Elements.Values.Count(e => e.Category == "Pipe").Should().Be(6);
            fake.Elements.Values.Count(e => e.Category == "Fitting").Should().Be(6); // 1 tee + 4 elbow + 1 trans
        }
    }
}
