using System;
using System.Threading.Tasks;
using Articulate.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Articulate.Controllers
{
    public class BlueGreenController : Controller
    {
        private readonly ILogger<BlueGreenController> _log;
        private readonly AppEnv _app;

        public BlueGreenController(ILogger<BlueGreenController> log, AppEnv app)
        {
            _log = log;
            _app = app;
        }

        public IActionResult Index() => View();
        
        public AppEnv Kill()
        {
            _log.LogWarning("*** The system is shutting down. ***");
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                _log.LogWarning($"APP {_app.AppName} instance ID {_app.InstanceName} KILLED");
                Environment.Exit(0);
            });
            return _app;
        }
    }
}