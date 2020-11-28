using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Steeltoe.Extensions.Configuration;
// using Steeltoe.CloudFoundry.Connector.App;
using Steeltoe.Extensions.Configuration.CloudFoundry;

namespace Articulate.Models
{
    public class AppEnv
    {
        public AppEnv(IHttpContextAccessor context, 
            IOptionsSnapshot<CloudFoundryApplicationOptions> appInfo, 
            IOptionsSnapshot<CloudFoundryServicesOptions> services)
        {
            var connectionContext = context.HttpContext.Features.Get<IHttpConnectionFeature>();
            ContainerAddress = $"{connectionContext.LocalIpAddress}:{connectionContext.LocalPort}";

            AppName = appInfo.Value.Name;
            InstanceName =  !string.IsNullOrEmpty(appInfo.Value.InstanceId) ? appInfo.Value.InstanceId : Environment.GetEnvironmentVariable("CF_INSTANCE_GUID");
            // if (InstanceName == "-1")
                // InstanceName = "--";
            Services = services.Value.Services
                .ToDictionary(x => x.Key, 
                    service => service.Value.Select(x => new ServiceBinding
                    {
                        Name = x.Name,
                        Label = x.Label,
                        Plan = x.Plan,
                        Tags = x.Tags,
                        Credentials = x.Credentials.ToDictionary(y => y.Key, y => MapCredentials(y.Value))
                    }) );
            ClrVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            HostAddress = Environment.GetEnvironmentVariable("CF_INSTANCE_ADDR") ?? "localhost";

        }

        object MapCredentials(Credential credentials)
        {
            if (credentials.Value != null)
                return credentials.Value;
            return credentials.ToDictionary(x => x.Key, x => MapCredentials(x.Value));
        }

        public string HostAddress { get; }
        public string ContainerAddress { get; }
        public string AppName { get; set; }
        public string InstanceName { get; set; }
        public Dictionary<string, IEnumerable<ServiceBinding>> Services { get; }
        public string ClrVersion { get; }
        
    }

    public class ServiceBinding
    {
        /// <summary>
        /// Gets or sets the name of the service instance
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a label describing the type of service
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Gets or sets the plan level at which the service is provisoned
        /// </summary>
        public IEnumerable<string> Tags { get; set; }

        /// <summary>
        /// Gets or sets a list of tags describing the service
        /// </summary>
        public string Plan { get; set; }
        
        public Dictionary<string,object> Credentials { get; set; }
    }
}