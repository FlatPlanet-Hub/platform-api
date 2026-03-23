namespace FlatPlanet.Platform.Application.DTOs;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public int? RowsAffected { get; init; }
    public string? Error { get; init; }

    public static ApiResponse<T> Ok(T data, int? rowsAffected = null) =>
        new() { Success = true, Data = data, RowsAffected = rowsAffected };

    public static ApiResponse<T> Fail(string error) =>
        new() { Success = false, Error = error };
}
