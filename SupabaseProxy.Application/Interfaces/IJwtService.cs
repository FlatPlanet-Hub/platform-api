using SupabaseProxy.Application.DTOs;

namespace SupabaseProxy.Application.Interfaces;

public interface IJwtService
{
    string GenerateToken(GenerateTokenRequest request);
}
