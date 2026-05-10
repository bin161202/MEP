namespace MEPAuto.Contracts.DTOs
{
    public class HelloWorldSnapshotData
    {
        public string UserName { get; set; } = "";
    }

    public class HelloWorldRequest
    {
        public HelloWorldSnapshotData Snapshot { get; set; } = new HelloWorldSnapshotData();
    }

    public class HelloWorldResponse
    {
        public string Message { get; set; } = "";
        public string JobId { get; set; } = "";
    }

    public class HelloWorldResultRequest
    {
        public string JobId { get; set; } = "";
        public bool Success { get; set; }
    }
}
