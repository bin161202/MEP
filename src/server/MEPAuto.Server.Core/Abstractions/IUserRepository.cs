using System.Threading.Tasks;
using MEPAuto.Server.Core.Models;

namespace MEPAuto.Server.Core.Abstractions
{
    /// <summary>
    /// Storage cho user account. Phase 1 = JSON file (JsonFileUserRepository); Phase 2 = Postgres EF Core.
    /// Application/Domain code KHÔNG đổi khi swap.
    /// </summary>
    public interface IUserRepository
    {
        Task<User?> GetByEmail(string email);
        Task<User?> GetById(string userId);
        Task Save(User user);
    }
}
