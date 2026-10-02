using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace PrivateSekai.Client;

public static class FailureDiagnostics
{
    public static JsonObject? Transport(Exception error)
    {
        if (error is not HttpRequestException http) return null;
        var details = new JsonObject
        {
            ["httpRequestError"] = http.HttpRequestError.ToString(),
            ["statusCode"] = http.StatusCode.HasValue ? (int)http.StatusCode.Value : null,
            ["innerErrorType"] = http.InnerException?.GetType().Name
        };
        if (http.InnerException is SocketException socket)
            details["socketError"] = socket.SocketErrorCode.ToString();
        return details;
    }
}
