using Microsoft.AspNetCore.Mvc;
using PrivateSekai.Crypto;

namespace PrivateSekai.Controllers;

[PrskDecryptRequest]
[PrskEncryptResponse]
[ApiController]
public abstract class PrskController : ControllerBase;
