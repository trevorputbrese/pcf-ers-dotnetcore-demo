using System.Collections.Generic;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// using Steeltoe.Bootstrap.Autoconfig;
using Steeltoe.Common.Hosting;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Extensions.Configuration.Placeholder;
using Steeltoe.Extensions.Logging;
using Steeltoe.Management.CloudFoundry;
using Steeltoe.Management.TaskCore;


namespace Articulate
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .AddCloudFoundryConfiguration()
                .AddPlaceholderResolver()
                .AddCloudFoundryActuators()
                
                .UseStartup<Startup>()
                // .AddDynamicLogging()
                // .AddSteeltoe()
                // .ConfigureWebHost(cfg => cfg.UseStartup<Startup>())
                .Build();
    }
    
    
}