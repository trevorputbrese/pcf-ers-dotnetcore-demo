using System;
using Articulate.Models;
using Microsoft.AspNetCore.Mvc;

namespace Articulate.Controllers
{

    public class HomeController : Controller
    {
        public IActionResult Index() => View();
        public AppEnv InstanceInfo([FromServices] AppEnv env)
        {
            // env.AppName = $"{env.AppName}-{new Random().Next(1, 3)}";
            // env.InstanceName = $"blah-{new Random().Next(1, 5)}";
            return env;
        }
        
        public void WriteFile()
        {
            var fileName = "ers-ssh-demo.log";
            
            System.IO.File.WriteAllText(fileName,DateTime.Now.ToString("MM-dd-yy HH:mm:ss"));
        }
        
        
    }
}