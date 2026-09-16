using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IPasswordService
{
    string Hash(UserAccount account, string password);
    bool Verify(UserAccount account, string password);
}
