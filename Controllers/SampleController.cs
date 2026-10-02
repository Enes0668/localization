using Microsoft.AspNetCore.Mvc;

namespace LocalizationApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SampleController : ControllerBase
{
    /// <summary>
    /// Otomatik çeviri testi (Sipariş senaryosu)
    /// Backend İngilizce döner, middleware istenen dile (?lang=tr) çevirir.
    /// GET /api/sample/order?lang=tr
    /// </summary>
    [HttpGet("order")]
    public IActionResult GetSampleOrder([FromQuery] string? lang = "tr")
    {
        return Ok(new
        {
            ResponseValue = new
            {
                Code = 200,
                Message = "Payment was successful."
            }
        });
    }

    /// <summary>
    /// Otomatik çeviri testi (Hata senaryosu)
    /// GET /api/sample/error?lang=tr
    /// </summary>
    [HttpGet("error")]
    public IActionResult GetSampleError([FromQuery] string? lang = "tr")
    {
        return Ok(new
        {
            Response = new
            {
                Texts = new
                {
                    UserMessage = "Order not found."
                }
            }
        });
    }

    /// <summary>
    /// Genel şirket sözlüğü testi (Login senaryosu)
    /// GET /api/sample/login?lang=es veya ?lang=Spanish
    /// </summary>
    [HttpGet("login")]
    public IActionResult GetLogin([FromQuery] string? lang = "es")
    {
        return Ok(new
        {
            ResponseValue = new
            {
                Code = 200,
                Message = "Login"
            }
        });
    }
}
