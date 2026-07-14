# Environment Variables Setup

## How It Works

The Turn One Link application now automatically loads environment variables from `.env` files during startup. This is implemented using the **DotNetEnv** NuGet package.

## Files

### `.env` (Committed to Git)
This is the **primary environment configuration file** containing default values for all environment variables. It's safe to commit to version control and should be updated when configuration requirements change.

**Current variables:**
- `API_URL` - Backend API endpoint (defaults to production: `https://backend.t1f1.com/api`)
- `USE_LOCAL_API` - Flag to use local API instead (set to `true` for local development)
- `WEBSOCKET_PORT` - WebSocket server port (default: `8080`)
- `LOG_LEVEL` - Logging level (default: `Information`)
- `DEBUG_MODE` - Debug mode flag (default: `false`)
- `CONNECTION_TIMEOUT_SECONDS` - HTTP timeout (default: `15`)
- `CONNECTION_POLL_INTERVAL_MS` - Poll interval (default: `3000`)
- `TELEMETRY_ENABLED` - Telemetry streaming (default: `true`)
- `TELEMETRY_BUFFER_SIZE` - Buffer size for telemetry (default: `100`)
- `SECURE_TOKEN_STORE_ENABLED` - Secure token storage (default: `true`)

### `.env.local` (Not Committed)
This file is **optional** and **should not be committed** to git. Add it locally for development overrides. Any variables defined here will override values from `.env`.

**Example `.env.local` for local development:**
```dotenv
USE_LOCAL_API=true
API_URL=http://localhost:5271/api
DEBUG_MODE=true
LOG_LEVEL=Debug
```

## Usage

### For End Users
No action needed! The `.env` file is automatically loaded when the application starts.

### For Developers

1. **To override settings locally:**
   - Create a `.env.local` file in the project root
   - Add only the variables you want to override
   - This file is ignored by git (add to `.gitignore` if not already there)

2. **To add a new environment variable:**
   - Add it to `.env` with a sensible default
   - Add documentation above the variable explaining its purpose
   - Access it in code using: `Environment.GetEnvironmentVariable("VARIABLE_NAME")`

3. **Example: Reading an Environment Variable**
   ```csharp
   var apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "https://default-url.com";
   ```

## Loading Order

The application loads environment variables in this order:
1. System/Process environment variables (OS level)
2. `.env` file (in the application's base directory)
3. `.env.local` file (in the application's base directory, if it exists)

Later values override earlier ones, so `.env.local` takes precedence over `.env`.

## Integration Points

### `AuthService.cs`
Already uses `USE_LOCAL_API` and `API_URL`:
```csharp
if (Environment.GetEnvironmentVariable("USE_LOCAL_API") == "true")
{
    _apiBaseUrl = NormalizeBaseUrl(apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL"))
                  ?? "http://localhost:5271/api";
}
```

### App Startup (`App.xaml.cs`)
The `.env` files are automatically loaded in `OnStartup()`:
```csharp
Env.Load(envPath);        // Loads .env
Env.Load(envLocalPath);   // Loads .env.local (if exists)
```

## Troubleshooting

**Q: The app isn't picking up my environment variables**
- Ensure the `.env` file is in the application's working directory
- For debug builds: `bin/Debug/net9.0-windows/`
- For release builds: `bin/Release/net9.0-windows/`
- Check that variables are correctly formatted: `KEY=value`

**Q: My `.env.local` changes aren't being applied**
- The app loads variables at startup. Restart the application to see changes.
- Check that `.env.local` is in the same directory as `.env`

**Q: Should I commit `.env.local`?**
- **No!** Add it to `.gitignore` if it isn't already. It's for local development only.

## Best Practices

✅ **DO:**
- Add new configuration to `.env` with sensible defaults
- Use `.env.local` for personal development settings
- Document each variable with a comment explaining its purpose
- Use descriptive names: `API_URL` instead of `URL`, `LOG_LEVEL` instead of `LOG`

❌ **DON'T:**
- Commit `.env.local` to git
- Store sensitive credentials in `.env` (use `.env.local` instead)
- Use uppercase variables with spaces
- Forget to add `=` between key and value
