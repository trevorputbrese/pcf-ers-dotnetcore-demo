using System;
using Articulate.Models;
using Microsoft.AspNetCore.Mvc;

namespace Articulate.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
    public AppEnv InstanceInfo([FromServices] AppEnv env)
    {
        return env;
    }
        
    public void WriteFile()
    {
        var fileName = "ers-ssh-demo.log";
            
        System.IO.File.WriteAllText(fileName,DateTime.Now.ToString("MM-dd-yy HH:mm:ss"));
    }
        
        
}