using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace TranscriptService.API.Controllers.Neutral
{
    [ApiVersionNeutral]
    public class PingController : BaseController
    {
        /// <summary>
        /// Health check endpoint
        /// </summary>
        /// <returns>Pong string</returns>
        [HttpGet]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public IActionResult Ping()
        {
            return Ok("Pong");
        }
    }
}
