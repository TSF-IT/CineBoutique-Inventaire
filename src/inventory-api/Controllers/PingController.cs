using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CineBoutique.Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PingController : ControllerBase
{
    /// <summary>
    /// Vérifie que l'API répond.
    /// </summary>
    /// <returns>"pong"</returns>
    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new { message = "pong" });
    }
}
