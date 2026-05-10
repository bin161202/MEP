using BCrypt.Net;

namespace MEPAuto.Server.Infrastructure.FileSystem
{
    /// <summary>
    /// Helper static cho BCrypt password hashing — work factor 11.
    /// AuthController gọi <see cref="Verify"/> lúc login, admin CLI gọi <see cref="Hash"/> lúc tạo user.
    /// </summary>
    public static class BCryptPasswordHasher
    {
        private const int WorkFactor = 11;

        public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

        public static bool Verify(string password, string hash)
        {
            try { return BCrypt.Net.BCrypt.Verify(password, hash); }
            catch (SaltParseException) { return false; }
        }
    }
}
