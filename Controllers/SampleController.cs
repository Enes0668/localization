using Microsoft.AspNetCore.Mvc;

namespace LocalizationApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SampleController : ControllerBase
{
    /// <summary>
    /// Otomatik çeviri testi (Sipariş senaryosu)
    /// Backend İngilizce döner, middleware istenen dile (?culture=tr) çevirir.
    /// GET /api/sample/order?culture=tr
    /// </summary>
    [HttpGet("order")]
    public IActionResult GetSampleOrder()
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
    /// GET /api/sample/error?culture=tr
    /// </summary>
    [HttpGet("error")]
    public IActionResult GetSampleError()
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
}
