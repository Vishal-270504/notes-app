namespace NotesApi.Models;

public class User
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public record AuthRequest(string? Email, string? Password);

public record SignupResponse(Guid Id, string Email);

public record LoginResponse(string Token, DateTime ExpiresAt, Guid UserId, string Email);
