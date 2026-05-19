using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet("check")]
        public IActionResult Check()
        {
            return Ok(new
            {
                status = "running",
                timestamp = DateTime.UtcNow,
                message = "API is up and running"
            });
        }
    }
}
