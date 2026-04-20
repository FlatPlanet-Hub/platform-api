using FlatPlanet.Platform.Application.DTOs.Dataverse;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IDataverseService
{
    Task<IEnumerable<EmployeeDto>> GetEmployeesAsync();
    Task<IEnumerable<AccountDto>> GetAccountsAsync();
}
