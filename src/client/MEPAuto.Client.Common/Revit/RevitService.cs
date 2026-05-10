using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using MEPAuto.Contracts.DTOs;

namespace MEPAuto.Client.Common.Revit
{
    /// <summary>
    /// Real implementation của <see cref="IRevitService"/> — wrap <see cref="UIDocument"/> + Revit API.
    /// <para>
    /// PROBE/MODIFY method còn <c>throw NotImplementedException</c> — implement dần khi feature thật cần.
    /// PIPE/FITTING method đã implement cho MEP routing features.
    /// </para>
    /// </summary>
    public class RevitService : IRevitService
    {
        private readonly UIDocument _uidoc;

        // Cache để tránh FilteredElementCollector mỗi call (resolve theo tên).
        private readonly Dictionary<string, ElementId> _systemTypeCache = new Dictionary<string, ElementId>();
        private readonly Dictionary<string, ElementId> _pipeTypeCache = new Dictionary<string, ElementId>();
        private readonly Dictionary<string, Level> _levelCache = new Dictionary<string, Level>();

        public RevitService(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        }

        private Document Doc => _uidoc.Document;

        // ========== PROBE — implement dần ==========
        public LevelData[] GetLevels() => throw new NotImplementedException();
        public FamilyTypeData[] GetFamilyTypes(BuiltInCategory category) => throw new NotImplementedException();
        public ElementSnapshotData[] GetByCategory(BuiltInCategory category) => throw new NotImplementedException();
        public ElementSnapshotData[] GetSelected() => throw new NotImplementedException();
        public ElementSnapshotData? GetById(string elementId) => throw new NotImplementedException();
        public ParameterValueData? GetParameter(string elementId, string paramName) => throw new NotImplementedException();

        // ========== CREATE ==========

        public string CreatePipe(Point3D startMm, Point3D endMm, double dnMm, string systemType, string levelName, string pipeTypeName = "")
        {
            var sysTypeId = ResolveSystemType(systemType);
            var pipeTypeId = string.IsNullOrEmpty(pipeTypeName) ? ResolveDefaultPipeType() : ResolvePipeTypeByName(pipeTypeName);
            var level = ResolveLevel(levelName);

            var startXYZ = ToFt(startMm);
            var endXYZ = ToFt(endMm);

            var pipe = Pipe.Create(Doc, sysTypeId, pipeTypeId, level.Id, startXYZ, endXYZ);
            pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(UnitHelper.MmToFt(dnMm));
            return ElementIdAdapter.GetValue(pipe.Id).ToString();
        }

        public string CreateDuct(Point3D startMm, Point3D endMm, double widthMm, double heightMm, string systemType, string levelName)
            => throw new NotImplementedException();
        public string CreateFamilyInstance(string familyName, string typeName, Point3D posMm, string levelName)
            => throw new NotImplementedException();
        public string CreateFitting(string familyName, Point3D posMm, double sizeMm)
            => throw new NotImplementedException();

        // ========== MODIFY ==========
        public void SetParameter(string elementId, string paramName, object value) => throw new NotImplementedException();
        public void MoveElement(string elementId, Vector3D deltaMm) => throw new NotImplementedException();
        public void DeleteElement(string elementId) => throw new NotImplementedException();

        // ========== CONNECT ==========
        public void ConnectConnectors(string elementIdA, int connectorIdxA, string elementIdB, int connectorIdxB)
            => throw new NotImplementedException();

        // ========== PIPE GEOMETRY ==========

        public string BreakPipe(string pipeId, Point3D atMm)
        {
            var id = ElementIdAdapter.Create(long.Parse(pipeId));
            var newSegId = PlumbingUtils.BreakCurve(Doc, id, ToFt(atMm));
            return ElementIdAdapter.GetValue(newSegId).ToString();
        }

        public void Regenerate() => Doc.Regenerate();

        // ========== FITTINGS ==========

        public string CreateTeeY(string mainPipeId, Point3D branchPointMm, double branchDnMm,
                                 string systemType, string levelName, double angleRad, string pipeTypeName = "")
        {
            // DEFENSIVE FLUSH: Force regen ngay đầu transaction để xử lý pending state từ undo/redo trước đó.
            // Race condition: nếu user vừa Ctrl+Z trước khi click button, Revit MEPComponentTracker + connector
            // network update vẫn pending background ~100-500ms. BreakCurve gọi ngay → MODIFICATION FORBIDDEN
            // (transient state dirty). Forcing Regenerate() đầu transaction = flush hết pending → state clean.
            Doc.Regenerate();

            var mainPipe = GetPipe(mainPipeId);
            var branchXYZ = ToFt(branchPointMm);
            var mainCurve = ((LocationCurve)mainPipe.Location).Curve;
            branchXYZ = mainCurve.Project(branchXYZ).XYZPoint;

            var newSegId = PlumbingUtils.BreakCurve(Doc, mainPipe.Id, branchXYZ);
            Doc.Regenerate();

            var seg1 = (Pipe)Doc.GetElement(mainPipe.Id);
            var seg2 = (Pipe)Doc.GetElement(newSegId);

            var mc1 = FindUnconnectedConnector(seg1, branchXYZ);
            var mc2 = FindUnconnectedConnector(seg2, branchXYZ);

            var sysTypeId = ResolveSystemType(systemType);
            ElementId pipeTypeId = !string.IsNullOrEmpty(pipeTypeName)
                ? ResolvePipeTypeByName(pipeTypeName)
                : mainPipe.GetTypeId();
            var level = ResolveLevel(levelName);
            double dnFt = UnitHelper.MmToFt(branchDnMm);

            var tempEnd = new XYZ(
                branchXYZ.X + UnitHelper.MmToFt(300),
                branchXYZ.Y,
                branchXYZ.Z + UnitHelper.MmToFt(3));

            Pipe? tempPipe = null;
            FamilyInstance teeInst;
            try
            {
                tempPipe = Pipe.Create(Doc, sysTypeId, pipeTypeId, level.Id, branchXYZ, tempEnd);
                tempPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(dnFt);
                Doc.Regenerate();

                var tempConn = FindClosestConnector(tempPipe, branchXYZ);
                teeInst = Doc.Create.NewTeeFitting(mc1, mc2, tempConn);
                Doc.Regenerate();
            }
            finally
            {
                if (tempPipe != null && Doc.GetElement(tempPipe.Id) != null)
                {
                    Doc.Delete(tempPipe.Id);
                    Doc.Regenerate();
                }
            }

            foreach (Parameter p in teeInst.Parameters)
            {
                if (p.Definition.Name.ToLower().Contains("angle") && !p.IsReadOnly)
                {
                    p.Set(angleRad);
                    break;
                }
            }
            Doc.Regenerate();

            return ElementIdAdapter.GetValue(teeInst.Id).ToString();
        }

        public string CreateElbow(string pipeIdA, string pipeIdB, Point3D atMm)
        {
            var atXYZ = ToFt(atMm);
            var pipeA = GetPipe(pipeIdA);
            var pipeB = GetPipe(pipeIdB);
            var connA = FindClosestConnector(pipeA, atXYZ);
            var connB = FindClosestConnector(pipeB, atXYZ);
            var elbow = Doc.Create.NewElbowFitting(connA, connB);
            Doc.Regenerate();
            return ElementIdAdapter.GetValue(elbow.Id).ToString();
        }

        public string CreateTransition(string pipeIdA, string pipeIdB, Point3D atMm)
        {
            var atXYZ = ToFt(atMm);
            var pipeA = GetPipe(pipeIdA);
            var pipeB = GetPipe(pipeIdB);
            var connA = FindClosestConnector(pipeA, atXYZ);
            var connB = FindClosestConnector(pipeB, atXYZ);
            var trans = Doc.Create.NewTransitionFitting(connA, connB);
            Doc.Regenerate();
            return ElementIdAdapter.GetValue(trans.Id).ToString();
        }

        public void ConnectPipeToTeeBranch(string pipeId, string teeId)
        {
            var pipe = GetPipe(pipeId);
            var tee = Doc.GetElement(ElementIdAdapter.Create(long.Parse(teeId))) as FamilyInstance
                ?? throw new InvalidOperationException($"Tee {teeId} không phải FamilyInstance.");

            Connector? teeBranch = null;
            foreach (Connector c in tee.MEPModel.ConnectorManager.Connectors)
            {
                if (!c.IsConnected) { teeBranch = c; break; }
            }
            if (teeBranch == null)
                throw new InvalidOperationException($"Tee {teeId} không còn connector unconnected (đã full?).");

            var pipeConn = FindClosestConnector(pipe, teeBranch.Origin);
            pipeConn.ConnectTo(teeBranch);
        }

        public void ConnectClosest(string elementIdA, string elementIdB, Point3D nearMm)
        {
            var nearXYZ = ToFt(nearMm);
            var elemA = Doc.GetElement(ElementIdAdapter.Create(long.Parse(elementIdA)))
                ?? throw new InvalidOperationException($"Element {elementIdA} không tồn tại.");
            var elemB = Doc.GetElement(ElementIdAdapter.Create(long.Parse(elementIdB)))
                ?? throw new InvalidOperationException($"Element {elementIdB} không tồn tại.");
            var connA = FindClosestConnectorOf(elemA, nearXYZ);
            var connB = FindClosestConnectorOf(elemB, nearXYZ);
            connA.ConnectTo(connB);
        }

        // ========== TRANSACTION ==========

        public void RunInTransaction(string name, Action body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            using (var tx = new Transaction(Doc, name))
            {
                tx.Start();
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new MepWarningSuppressor());
                opts.SetClearAfterRollback(true);
                opts.SetForcedModalHandling(false);
                tx.SetFailureHandlingOptions(opts);

                try
                {
                    body();
                    tx.Commit();
                }
                catch
                {
                    if (tx.HasStarted()) tx.RollBack();
                    throw;
                }
            }
        }

        /// <summary>
        /// Tự delete warnings (severity=Warning) trong transaction để Revit không hiện modal dialog hoặc rollback.
        /// Warnings thường gặp khi tạo Tee chèn vào ống có sẵn network.
        /// </summary>
        private class MepWarningSuppressor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                var failures = failuresAccessor.GetFailureMessages();
                foreach (var f in failures)
                {
                    if (f.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(f);
                    }
                }
                return FailureProcessingResult.Continue;
            }
        }

        // ===================================================================
        //  Private helpers
        // ===================================================================

        private XYZ ToFt(Point3D mm) => new XYZ(UnitHelper.MmToFt(mm.X), UnitHelper.MmToFt(mm.Y), UnitHelper.MmToFt(mm.Z));

        private Pipe GetPipe(string id)
        {
            var elem = Doc.GetElement(ElementIdAdapter.Create(long.Parse(id)))
                ?? throw new InvalidOperationException($"Element {id} không tồn tại.");
            return elem as Pipe ?? throw new InvalidOperationException($"Element {id} không phải Pipe (thực tế: {elem.GetType().Name}).");
        }

        private ElementId ResolveSystemType(string name)
        {
            if (_systemTypeCache.TryGetValue(name, out var cached)) return cached;
            var collector = new FilteredElementCollector(Doc).OfClass(typeof(MEPSystemType));
            foreach (MEPSystemType st in collector)
            {
                if (st.Name == name) { _systemTypeCache[name] = st.Id; return st.Id; }
            }
            throw new InvalidOperationException($"MEPSystemType '{name}' không tìm thấy trong document.");
        }

        private ElementId ResolveDefaultPipeType()
        {
            if (_pipeTypeCache.TryGetValue("__default__", out var cached)) return cached;
            var collector = new FilteredElementCollector(Doc).OfClass(typeof(PipeType));
            foreach (PipeType pt in collector)
            {
                _pipeTypeCache["__default__"] = pt.Id;
                return pt.Id;
            }
            throw new InvalidOperationException("Document không có PipeType nào — cần load 1 pipe type trước.");
        }

        private ElementId ResolvePipeTypeByName(string name)
        {
            if (_pipeTypeCache.TryGetValue(name, out var cached)) return cached;
            var collector = new FilteredElementCollector(Doc).OfClass(typeof(PipeType));
            foreach (PipeType pt in collector)
            {
                if (pt.Name == name) { _pipeTypeCache[name] = pt.Id; return pt.Id; }
            }
            return ResolveDefaultPipeType();
        }

        private Level ResolveLevel(string name)
        {
            if (_levelCache.TryGetValue(name, out var cached)) return cached;
            var collector = new FilteredElementCollector(Doc).OfClass(typeof(Level));
            foreach (Level lv in collector)
            {
                if (lv.Name == name) { _levelCache[name] = lv; return lv; }
            }
            throw new InvalidOperationException($"Level '{name}' không tìm thấy trong document.");
        }

        private static Connector FindUnconnectedConnector(Pipe pipe, XYZ nearPoint)
        {
            // PHẢI pick UNCONNECTED + CLOSEST tới nearPoint — KHÔNG dùng "first unconnected"
            // vì iteration order ConnectorManager.Connectors không deterministic.
            Connector? best = null;
            double minDist = double.MaxValue;
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                if (c.IsConnected) continue;
                double d = c.Origin.DistanceTo(nearPoint);
                if (d < minDist) { minDist = d; best = c; }
            }
            if (best != null) return best;

            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                double d = c.Origin.DistanceTo(nearPoint);
                if (d < minDist) { minDist = d; best = c; }
            }
            return best ?? throw new InvalidOperationException($"Pipe {pipe.Id} không có connector.");
        }

        private static Connector FindClosestConnector(Pipe pipe, XYZ point)
        {
            Connector? best = null;
            double minDist = double.MaxValue;
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                double d = c.Origin.DistanceTo(point);
                if (d < minDist) { minDist = d; best = c; }
            }
            return best ?? throw new InvalidOperationException($"Pipe {pipe.Id} không có connector.");
        }

        private static Connector FindClosestConnectorOf(Element elem, XYZ point)
        {
            ConnectorManager? cm = elem switch
            {
                Pipe p => p.ConnectorManager,
                FamilyInstance fi => fi.MEPModel?.ConnectorManager,
                _ => null,
            };
            if (cm == null)
                throw new InvalidOperationException($"Element {elem.Id} không có ConnectorManager.");

            Connector? best = null;
            double minDist = double.MaxValue;
            foreach (Connector c in cm.Connectors)
            {
                double d = c.Origin.DistanceTo(point);
                if (d < minDist) { minDist = d; best = c; }
            }
            return best ?? throw new InvalidOperationException($"Element {elem.Id} không có connector.");
        }
    }
}
