namespace Turn_One_Link.Services;

public record UserSession(string Email, string DisplayName);

public class AuthService
{
    private UserSession? _session;
    public UserSession? CurrentSession => _session;

    public async Task<(bool Success, string? Error)> LoginAsync(string email, string password)
    {
        await Task.Delay(800); // replace with real API call

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return (false, "Invalid credentials.");

        // TODO: POST to TurnOne auth endpoint, validate JWT
        _session = new UserSession(email, email.Split('@')[0]);
        return (true, null);
    }

    public void Logout() => _session = null;
}
