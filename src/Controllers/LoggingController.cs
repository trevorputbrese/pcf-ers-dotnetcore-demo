using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Articulate.Controllers
{
    public class LoggingController : Controller
    {
        private readonly ILogger<LoggingController> _log;

        public LoggingController(ILogger<LoggingController> log)
        {
            _log = log;
        }

        public IActionResult Index() => View();
        
        [HttpPost]
        public void Log(LogLevel logLevel, string message) => _log.Log(logLevel, message);
    }
}