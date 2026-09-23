using Microsoft.AspNetCore.Mvc;

namespace Enes3.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpGet(nameof(Get2))]
        public IActionResult Get2()
        {
            return BadRequest("Olmadı."); // "" path testi
        }

        [HttpGet(nameof(Get3))]
        public IActionResult Get3()
        {
            return BadRequest(new
            {
                Status = false,
                Message = "Olmadı." // "Message" path testi
            });
        }

        [HttpGet(nameof(Get4))]
        public IActionResult Get4()
        {
            return Ok(new
            {
                Status = false,
                Message = "Olmadı.",
                Log = new
                {
                    LogId = Guid.NewGuid(),
                    LogDate = DateTime.Now,
                    LogMessage = "Log mesajı" // "Log.LogMessage" path testi
                }
            });
        }

        [HttpGet(nameof(Get5))]
        public IActionResult Get5()
        {
            return Ok(new
            {
                Status = false,
                Text = "Oldu", // Listede yok, dokunulmayacak
                Message = "Olmadı." // "Message" path testi
            });
        }

        [HttpGet(nameof(Get6))]
        public IActionResult Get6()
        {
            return Ok(new
            {
                Status = false,
                Message = "Olmadı.", // "Message" path testi
                Log = new
                {
                    LogId = Guid.NewGuid(),
                    LogDate = DateTime.Now,
                    LogMessage = "Log mesajı",      // "Log.LogMessage" path testi
                    LogMessage2 = "Beni de değiştir", // "Log.LogMessage2" path testi
                    LogMessage3 = "Bana dokunma"     // Listede yok, dokunulmayacak!
                }
            });
        }
    }
}
