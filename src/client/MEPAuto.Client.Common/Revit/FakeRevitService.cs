using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAuto.Contracts.DTOs;

namespace MEPAuto.Client.Common.Revit
{
    /// <summary>
    /// Fake implementation của <see cref="IRevitService"/> — không cần Revit, dùng cho unit/integration test.
    /// Track <see cref="OperationLog"/> để verify command đúng thứ tự + tham số.
    /// </summary>
    public class FakeRevitService : IRevitService
    {
        public List<string> OperationLog { get; } = new List<string>();
        public Dictionary<string, ElementSnapshotData> Elements { get; } = new Dictionary<string, ElementSnapshotData>();
        public List<LevelData> SeedLevels { get; } = new List<LevelData>
        {
            new LevelData { Id = "fake-level-1", Name = "Level 1", ElevationMm = 0 },
            new LevelData { Id = "fake-level-2", Name = "Level 2", ElevationMm = 4000 },
        };

        private int _nextId = 1000;
        private string NewId() => "fake-" + _nextId++;
        private void Log(string op) => OperationLog.Add(op);

        public LevelData[] GetLevels() { Log("GetLevels"); return SeedLevels.ToArray(); }
        public FamilyTypeData[] GetFamilyTypes(BuiltInCategory category) { Log($"GetFamilyTypes {category}"); return Array.Empty<FamilyTypeData>(); }
        public ElementSnapshotData[] GetByCategory(BuiltInCategory category) { Log($"GetByCategory {category}"); return Array.Empty<ElementSnapshotData>(); }
        public ElementSnapshotData[] GetSelected() { Log("GetSelected"); return Array.Empty<ElementSnapshotData>(); }
        public ElementSnapshotData? GetById(string elementId)
        {
            Log($"GetById {elementId}");
            return Elements.TryGetValue(elementId, out var e) ? e : null;
        }
        public ParameterValueData? GetParameter(string elementId, string paramName) { Log($"GetParameter {elementId}.{paramName}"); return null; }

        public string CreatePipe(Point3D startMm, Point3D endMm, double dnMm, string systemType, string levelName, string pipeTypeName = "")
        {
            var id = NewId();
            Log($"CreatePipe id={id} dn={dnMm}mm sys={systemType} level={levelName} type={pipeTypeName}");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "Pipe", TypeName = pipeTypeName };
            return id;
        }
        public string CreateDuct(Point3D startMm, Point3D endMm, double widthMm, double heightMm, string systemType, string levelName)
        {
            var id = NewId();
            Log($"CreateDuct id={id} {widthMm}x{heightMm}mm sys={systemType}");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "Duct" };
            return id;
        }
        public string CreateFamilyInstance(string familyName, string typeName, Point3D posMm, string levelName)
        {
            var id = NewId();
            Log($"CreateFamilyInstance id={id} {familyName}/{typeName} level={levelName}");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "FamilyInstance", FamilyName = familyName, TypeName = typeName };
            return id;
        }
        public string CreateFitting(string familyName, Point3D posMm, double sizeMm)
        {
            var id = NewId();
            Log($"CreateFitting id={id} {familyName} size={sizeMm}mm");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "Fitting", FamilyName = familyName };
            return id;
        }

        public void SetParameter(string elementId, string paramName, object value)
            => Log($"SetParameter {elementId}.{paramName} = {value}");
        public void MoveElement(string elementId, Vector3D deltaMm)
            => Log($"MoveElement {elementId} delta=({deltaMm.X},{deltaMm.Y},{deltaMm.Z})mm");
        public void DeleteElement(string elementId)
        {
            Log($"DeleteElement {elementId}");
            Elements.Remove(elementId);
        }
        public void ConnectConnectors(string elementIdA, int connectorIdxA, string elementIdB, int connectorIdxB)
            => Log($"ConnectConnectors {elementIdA}[{connectorIdxA}] <-> {elementIdB}[{connectorIdxB}]");

        // ========== PIPE GEOMETRY ==========

        public string BreakPipe(string pipeId, Point3D atMm)
        {
            var newSegId = NewId();
            Log($"BreakPipe pipeId={pipeId} at=({atMm.X:F1},{atMm.Y:F1},{atMm.Z:F1}) newSegId={newSegId}");
            Elements[newSegId] = new ElementSnapshotData { Id = newSegId, Category = "Pipe" };
            return newSegId;
        }

        public void Regenerate() => Log("Regenerate");

        // ========== FITTINGS ==========

        public string CreateTeeY(string mainPipeId, Point3D branchPointMm, double branchDnMm,
                                 string systemType, string levelName, double angleRad, string pipeTypeName = "")
        {
            var id = NewId();
            Log($"CreateTeeY mainId={mainPipeId} branch=({branchPointMm.X:F1},{branchPointMm.Y:F1},{branchPointMm.Z:F1}) " +
                $"dn={branchDnMm}mm sys={systemType} level={levelName} angle={angleRad:F4} type={pipeTypeName}");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "Fitting", FamilyName = "Tee-Y", TypeName = pipeTypeName };
            return id;
        }

        public string CreateElbow(string pipeIdA, string pipeIdB, Point3D atMm)
        {
            var id = NewId();
            Log($"CreateElbow a={pipeIdA} b={pipeIdB} at=({atMm.X:F1},{atMm.Y:F1},{atMm.Z:F1})");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "Fitting", FamilyName = "Elbow" };
            return id;
        }

        public string CreateTransition(string pipeIdA, string pipeIdB, Point3D atMm)
        {
            var id = NewId();
            Log($"CreateTransition a={pipeIdA} b={pipeIdB} at=({atMm.X:F1},{atMm.Y:F1},{atMm.Z:F1})");
            Elements[id] = new ElementSnapshotData { Id = id, Category = "Fitting", FamilyName = "Transition" };
            return id;
        }

        public void ConnectPipeToTeeBranch(string pipeId, string teeId)
            => Log($"ConnectPipeToTeeBranch pipe={pipeId} tee={teeId}");

        public void ConnectClosest(string elementIdA, string elementIdB, Point3D nearMm)
            => Log($"ConnectClosest a={elementIdA} b={elementIdB} near=({nearMm.X:F1},{nearMm.Y:F1},{nearMm.Z:F1})");

        // ========== TRANSACTION ==========

        public void RunInTransaction(string name, Action body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            Log($"BeginTransaction {name}");
            try
            {
                body();
                Log($"CommitTransaction {name}");
            }
            catch
            {
                Log($"RollbackTransaction {name}");
                throw;
            }
        }
    }
}
