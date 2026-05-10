using System.Collections.Generic;

namespace MEPAuto.Contracts.DTOs
{
    /// <summary>Snapshot 1 Level trong document.</summary>
    public class LevelData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double ElevationMm { get; set; }
    }

    /// <summary>Snapshot 1 family type.</summary>
    public class FamilyTypeData
    {
        public string Id { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Category { get; set; } = "";
    }

    /// <summary>Snapshot 1 element bất kỳ — DTO chung cho probe data gửi lên server.</summary>
    public class ElementSnapshotData
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        /// <summary>Lookup bằng <c>Parameters.FirstOrDefault(p =&gt; p.Name == "...")?.AsDouble</c>.</summary>
        public List<ParameterValueData> Parameters { get; set; } = new List<ParameterValueData>();
    }

    /// <summary>Giá trị 1 parameter — multi-typed (string / double / int).</summary>
    public class ParameterValueData
    {
        public string Name { get; set; } = "";
        public string? AsString { get; set; }
        public double? AsDouble { get; set; }
        public long? AsInteger { get; set; }
    }
}
