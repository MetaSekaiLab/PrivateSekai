using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PrivateSekai.Client;

internal static class InheritToken
{
    public static string Create(string id, string password, string signingKey)
    {
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"HS256\"}"));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new { inheritId = id, password }));
        var input = header + "." + payload;
        var key = Encoding.UTF8.GetBytes(signingKey);
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(input));
        CryptographicOperations.ZeroMemory(key);
        return input + "." + Encode(signature);
    }
}
