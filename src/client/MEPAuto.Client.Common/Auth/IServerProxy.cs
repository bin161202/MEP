using System.Threading.Tasks;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// HTTP proxy để gọi VPS. Tự inject Authorization header, retry 401 sau khi refresh.
    /// </summary>
    public interface IServerProxy
    {
        Task<TResponse> Post<TResponse>(string path, object body) where TResponse : class;
        Task Post(string path, object body);
        Task<TResponse> Get<TResponse>(string path) where TResponse : class;
    }

    /// <summary>Thrown khi refresh token cũng fail → cần re-login.</summary>
    public class SessionExpiredException : System.Exception
    {
        public SessionExpiredException() : base("Session đã hết hạn, vui lòng đăng nhập lại.") { }
    }

    /// <summary>Thrown khi server trả lỗi nghiệp vụ (4xx ngoài 401) hoặc 5xx.</summary>
    public class ServerErrorException : System.Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }
        public ServerErrorException(int statusCode, string body)
            : base($"Server error {statusCode}: {body}")
        {
            StatusCode = statusCode;
            ResponseBody = body;
        }
    }
}
