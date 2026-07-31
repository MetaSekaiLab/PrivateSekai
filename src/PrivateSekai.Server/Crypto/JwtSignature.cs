using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using PrivateSekai.Config;

namespace PrivateSekai.Crypto;

public static class JwtSignature
{
    private static SymmetricSecurityKey? _signingKey;
    private static SigningCredentials? _credentials;

    private static SymmetricSecurityKey SigningKey =>
        _signingKey ??= new(Encoding.UTF8.GetBytes(ServerConfig.JwtKey));

    private static SigningCredentials Credentials =>
        _credentials ??= new(SigningKey, SecurityAlgorithms.HmacSha256);

    private static readonly JwtSecurityTokenHandler Handler = new();

    /// <summary>
    /// 创建 JWT，payload 使用 JwtPayload 以保留原始类型（如 long 不被转为 string）
    /// </summary>
    public static string CreateToken(Dictionary<string, object> claims)
    {
        var payload = new JwtPayload();
        foreach (var kv in claims)
            payload[kv.Key] = kv.Value;

        var header = new JwtHeader(Credentials);
        var token  = new JwtSecurityToken(header, payload);
        return Handler.WriteToken(token);
    }

    /// <summary>
    /// 验证并解码 JWT。若 IgnoreInvalidCredential 为 true，则仅 base64 解码 payload，不做签名验证。
    /// </summary>
    public static Dictionary<string, object>? VerifyToken(string token)
    {
        if (ServerConfig.IgnoreInvalidCredential)
            return DecodePayloadWithoutVerification(token);

        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer   = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                IssuerSigningKey = SigningKey
            };

            Handler.ValidateToken(token, parameters, out var validatedToken);
            var jwt = (JwtSecurityToken)validatedToken;

            return jwt.Payload.ToDictionary(c => c.Key, c => c.Value);
        }
        catch
        {
            return null;
        }
    }

    // ---------- 便捷方法，对应 Python crypto/signature.py ----------

    public static string GenUserSignature(long userId) =>
        CreateToken(new Dictionary<string, object> { ["userId"] = userId });

    public static string GenUserCredential(long userId) =>
        CreateToken(new Dictionary<string, object>
        {
            ["credential"] = Guid.NewGuid().ToString(),
            ["userId"]     = userId
        });

    public static bool VerifyCredential(string credential, long userId)
    {
        if (ServerConfig.IgnoreInvalidCredential) return true;

        var payload = VerifyToken(credential);
        if (payload is null) return false;
        if (!payload.TryGetValue("userId", out var uid)) return false;
        return Convert.ToInt64(uid) == userId;
    }

    public static string GenSessionToken(long userId) =>
        CreateToken(new Dictionary<string, object>
        {
            ["userId"]       = userId,
            ["sessionToken"] = Guid.NewGuid().ToString()
        });

    public static bool VerifySessionToken(string sessionToken, long userId)
    {
        var payload = VerifyToken(sessionToken);
        if (payload is null) return false;
        if (!payload.TryGetValue("userId", out var uid)) return false;
        return Convert.ToInt64(uid) == userId;
    }

    // ---------- 内部工具 ----------

    /// <summary>
    /// 不验证签名，仅 base64 解码 JWT 的 payload 部分（对应 Python 的 IGNORE_INVALID_CREDENTIAL 行为）
    /// </summary>
    private static Dictionary<string, object>? DecodePayloadWithoutVerification(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            // URL-safe base64 → standard base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            // 补 padding
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "=";  break;
            }

            var bytes = Convert.FromBase64String(payload);
            var json  = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }
}
