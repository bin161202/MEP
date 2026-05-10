using System;
using Autodesk.Revit.DB;
using MEPAuto.Contracts.DTOs;

namespace MEPAuto.Client.Common.Revit
{
    /// <summary>
    /// Surface contract giữa Client (feature command) và Revit API.
    /// Mọi feature CHỈ tương tác với Revit qua interface này — KHÔNG gọi Revit API trực tiếp.
    /// <para>
    /// 2 implementation: <see cref="RevitService"/> (real, wrap UIDocument) và FakeRevitService (test, không cần Revit).
    /// </para>
    /// </summary>
    public interface IRevitService
    {
        // ========== PROBE (đọc Revit, trả DTO) ==========
        LevelData[] GetLevels();
        FamilyTypeData[] GetFamilyTypes(BuiltInCategory category);
        ElementSnapshotData[] GetByCategory(BuiltInCategory category);
        ElementSnapshotData[] GetSelected();
        ElementSnapshotData? GetById(string elementId);
        ParameterValueData? GetParameter(string elementId, string paramName);

        // ========== CREATE (tạo element, trả ElementId dưới dạng string) ==========
        /// <summary>
        /// Tạo pipe. <paramref name="pipeTypeName"/> empty = pick default PipeType đầu document.
        /// CHÚ Ý: pass đúng <paramref name="pipeTypeName"/> của ống chính để Tee/Elbow auto-pick fitting tương thích
        /// qua RoutingPreference (nếu khác → "failed to insert tee/elbow" runtime).
        /// </summary>
        string CreatePipe(Point3D startMm, Point3D endMm, double dnMm, string systemType, string levelName, string pipeTypeName = "");
        string CreateDuct(Point3D startMm, Point3D endMm, double widthMm, double heightMm, string systemType, string levelName);
        string CreateFamilyInstance(string familyName, string typeName, Point3D posMm, string levelName);
        string CreateFitting(string familyName, Point3D posMm, double sizeMm);

        // ========== MODIFY ==========
        void SetParameter(string elementId, string paramName, object value);
        void MoveElement(string elementId, Vector3D deltaMm);
        void DeleteElement(string elementId);

        // ========== CONNECT (MEP) ==========
        void ConnectConnectors(string elementIdA, int connectorIdxA, string elementIdB, int connectorIdxB);

        // ========== PIPE GEOMETRY ==========

        /// <summary>Cắt 1 ống tại điểm <paramref name="atMm"/>. Trả về Id của đoạn ống MỚI (đoạn còn lại giữ Id cũ).
        /// Caller PHẢI gọi <see cref="Regenerate"/> trước khi probe connector của 2 đoạn vừa cắt.</summary>
        string BreakPipe(string pipeId, Point3D atMm);

        /// <summary>Yêu cầu Revit cập nhật geometry pending. Gọi sau <see cref="BreakPipe"/> hoặc
        /// <see cref="CreateTeeY"/> để connector mới được populate trước khi reference.</summary>
        void Regenerate();

        // ========== FITTINGS (MEP) ==========

        /// <summary>Tạo Tee-Y trên ống chính tại <paramref name="branchPointMm"/>.
        /// Đóng gói workaround: break pipe → temp pipe vuông góc → NewTeeFitting 90° → delete temp →
        /// chỉnh angle về <paramref name="angleRad"/> (45° = π/4 cho Tee-Y).
        /// <paramref name="pipeTypeName"/> empty = pick default; pass tên PipeType của ống chính để Tee compat.</summary>
        string CreateTeeY(string mainPipeId, Point3D branchPointMm, double branchDnMm,
                          string systemType, string levelName, double angleRad, string pipeTypeName = "");

        /// <summary>Tạo elbow nối 2 ống tại junction <paramref name="atMm"/>.
        /// Trên mỗi ống pick connector closest tới <paramref name="atMm"/> (deterministic).</summary>
        string CreateElbow(string pipeIdA, string pipeIdB, Point3D atMm);

        /// <summary>Tạo transition nối 2 ống khác đường kính tại junction <paramref name="atMm"/>.
        /// Trên mỗi ống pick connector closest tới <paramref name="atMm"/>.</summary>
        string CreateTransition(string pipeIdA, string pipeIdB, Point3D atMm);

        /// <summary>Connect ống tới Tee branch connector (FIRST UNCONNECTED trên Tee, KHÔNG closest).
        /// Sau Tee insert, 2 main connector của Tee đã connected vào ống chính bị break — chỉ branch còn free.</summary>
        void ConnectPipeToTeeBranch(string pipeId, string teeId);

        /// <summary>Connect 2 element bằng cách tìm connector gần <paramref name="nearMm"/> nhất trên mỗi element.
        /// Dùng cho cap end (FamilyInstance 1-2 connector).</summary>
        void ConnectClosest(string elementIdA, string elementIdB, Point3D nearMm);

        // ========== TRANSACTION (manual control khi cần group nhiều op) ==========
        void RunInTransaction(string name, Action body);
    }
}
