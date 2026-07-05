# Security Configuration Guide

## Development Setup

### 1. JWT Configuration (Required)

The application requires JWT keys to be configured for authentication. **Never commit real secrets to source control.**

#### For Development (Local Machine)

Use .NET User Secrets to store sensitive configuration:

```bash
# Navigate to AuthService directory
cd MicroservicesServer/AuthService

# Set JWT Key (generate a strong random key)
dotnet user-secrets set "Jwt:Key" "your-long-random-secret-key-minimum-32-characters"

# Set JWT Issuer (optional, defaults to what's in appsettings.json)
dotnet user-secrets set "Jwt:Issuer" "AuthService"
```

#### For Production

Use environment variables or a secure key vault:

**Azure Key Vault:**
```bash
# Configure Azure Key Vault reference in appsettings.Production.json
{
  "Jwt": {
    "Key": "@Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/jwt-key/)"
  }
}
```

**AWS Secrets Manager:**
```bash
# Use AWS Systems Manager Parameter Store or Secrets Manager
export Jwt__Key="<secret-from-aws>"
```

**Environment Variables:**
```bash
export Jwt__Key="your-production-secret-key"
export Jwt__Issuer="AuthService"
```

### 2. MongoDB Configuration

#### Development
Update `appsettings.Development.json`:
```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "AuthServiceDb_Dev"
  }
}
```

#### Production
Use environment variables or secrets:
```bash
export MongoDB__ConnectionString="mongodb+srv://username:password@cluster.mongodb.net/?retryWrites=true&w=majority"
export MongoDB__DatabaseName="AuthServiceDb_Prod"
```

## Security Best Practices

### ✅ DO

1. **Use User Secrets for local development**
   ```bash
   dotnet user-secrets set "ConnectionStrings:MongoDB" "mongodb://localhost:27017"
   ```

2. **Use Key Vaults for production**
   - Azure Key Vault
   - AWS Secrets Manager
   - HashiCorp Vault

3. **Rotate secrets regularly**
   - JWT keys: every 90 days
   - Database passwords: every 60 days

4. **Use strong, random keys**
   ```bash
   # Generate a strong random key
   openssl rand -base64 32
   ```

5. **Validate all user input**
   - DeviceID validation is already implemented
   - Add more validation as needed

6. **Enable HTTPS in production**
   ```json
   {
     "Kestrel": {
       "Endpoints": {
         "Https": {
           "Url": "https://*:443"
         }
       }
     }
   }
   ```

### ❌ DON'T

1. **Never commit secrets to source control**
   - Check `.gitignore` includes `appsettings.*.json` (except base template)
   - Use `dotnet user-secrets` instead

2. **Never use default/weak secrets in production**
   - Current placeholder in `appsettings.json` is for development only
   - MUST be overridden in production

3. **Never log sensitive data**
   - Don't log passwords, tokens, or connection strings
   - Use structured logging and filter sensitive fields

4. **Don't expose detailed errors to clients**
   - Use generic error messages in production
   - Log detailed errors server-side only

## Input Validation

Current validation rules for `UserLogin.deviceID`:

- **Length**: 10-100 characters
- **Format**: Alphanumeric, hyphens, and underscores only (`^[a-zA-Z0-9\-_]+$`)
- **Required**: Cannot be null or empty

Example valid deviceIDs:
- ✅ `device-12345-abcde`
- ✅ `iPhone_15_Pro_ABC123`
- ✅ `android_pixel_8`

Example invalid deviceIDs:
- ❌ `abc` (too short)
- ❌ `device@123` (invalid character @)
- ❌ `device 123` (contains space)

## Rate Limiting (Recommended)

To add rate limiting to prevent brute force attacks, install:

```bash
dotnet add package AspNetCoreRateLimit
```

Configuration example:
```csharp
// In Program.cs
builder.Services.AddMemoryCache();
builder.Services.AddInMemoryRateLimiting();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "POST:/login",
            Limit = 5,
            Period = "1m"
        }
    };
});

app.UseIpRateLimiting();
```

## Monitoring & Alerts

### Recommended Monitoring

1. **Failed login attempts**
   - Alert on > 10 failed attempts per IP in 5 minutes

2. **JWT token validation failures**
   - Alert on spike in invalid tokens

3. **Unusual traffic patterns**
   - Alert on > 100 requests/second from single IP

4. **Configuration errors**
   - Alert on startup failures due to missing secrets

## Quick Start Checklist

- [ ] Clone repository
- [ ] Set up User Secrets for JWT key
- [ ] Configure MongoDB connection
- [ ] Run `dotnet build` to verify
- [ ] Run `dotnet run` to start
- [ ] Test `/login` endpoint with valid deviceID
- [ ] Verify JWT token is returned
- [ ] Test with invalid deviceID to verify validation

## Support

For security issues, please report privately to the repository owner.
Do not create public GitHub issues for security vulnerabilities.

---
**Last Updated**: 2025-01-17
