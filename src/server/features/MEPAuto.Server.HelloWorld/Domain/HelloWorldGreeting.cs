using System;

namespace MEPAuto.Server.HelloWorld.Domain
{
    public static class HelloWorldGreeting
    {
        public static string Build(string userName)
        {
            var safe = string.IsNullOrWhiteSpace(userName) ? "bạn" : userName;
            return $"Xin chào {safe}, server time: {DateTime.UtcNow:O}";
        }
    }
}
