using Microsoft.AspNetCore.Mvc;

namespace TranscriptService.API.Controllers
{
    [ApiController]
    [Route("api/v{v:apiVersion}/[controller]")]
    public class BaseController : ControllerBase
    {
        protected ObjectResult InternalServerError<T>(T obj) =>
            StatusCode(StatusCodes.Status500InternalServerError, obj);
    }
}
