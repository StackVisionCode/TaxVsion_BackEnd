using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// JWKS público del microservicio Signature. Expone las claves RSA activas para que
/// consumidores externos (verificadores de audit, otros microservicios) puedan validar
/// los JWTs de firmante sin depender de secreto compartido. Formato RFC 7517.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("signature/.well-known")]
public sealed class JwksController(IRsaKeyProvider keyProvider) : ControllerBase
{
    [HttpGet("jwks.json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Jwks()
    {
        var keys = keyProvider
            .GetPublicKeys()
            .Select(key => new
            {
                kid = key.Kid,
                kty = "RSA",
                use = "sig",
                alg = key.Alg,
                n = key.N,
                e = key.E,
            })
            .ToList();
        return Ok(new { keys });
    }
}
