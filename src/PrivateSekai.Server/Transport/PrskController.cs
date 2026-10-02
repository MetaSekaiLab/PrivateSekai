using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Transport;

namespace PrivateSekai.Transport;

[PrskDecryptRequest]
[PrskEncryptResponse]
[ApiController]
public abstract class PrskController : ControllerBase
{
    protected FileContentResult Encoded(byte[] response) => File(response, "application/octet-stream");
}
