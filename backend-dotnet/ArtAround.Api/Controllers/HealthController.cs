using Microsoft.AspNetCore.Mvc;

namespace ArtAround.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "ok" });
    }
}