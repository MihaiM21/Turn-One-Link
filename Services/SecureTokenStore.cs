using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Turn_One_Link.Services;

internal static class SecureTokenStore
{
    private const string AppFolderName = "TurnOneLink";
    private const string AuthFileName = "authstate.bin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task SaveAsync(StoredAuthState state)
    {
        var path = GetStoragePath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var data = Encoding.UTF8.GetBytes(json);
        var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(path, protectedData);
    }

    public static async Task<StoredAuthState?> LoadAsync()
    {
        var path = GetStoragePath();
        if (!File.Exists(path))
            return null;

        var protectedData = await File.ReadAllBytesAsync(path);
        var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(data);

        return JsonSerializer.Deserialize<StoredAuthState>(json, JsonOptions);
    }

    public static void Clear()
    {
        var path = GetStoragePath();
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, AppFolderName, AuthFileName);
    }
}


