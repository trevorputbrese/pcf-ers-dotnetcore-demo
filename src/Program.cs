using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Articulate;
using Articulate.Models;
using Articulate.Repositories;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;
using Steeltoe.Common.Http;
using Steeltoe.Common.Http.Discovery;
using Steeltoe.Common.Options;
using Steeltoe.Common.Security;
// using Steeltoe.Bootstrap.Autoconfig;
using Steeltoe.Connector.EFCore;
using Steeltoe.Connector.MySql;
using Steeltoe.Connector.Services;
using Steeltoe.Connector.MySql.EFCore;
using Steeltoe.Connector.SqlServer;
using Steeltoe.Connector.SqlServer.EFCore;
using Steeltoe.Discovery.Client;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Extensions.Configuration.Placeholder;
using Steeltoe.Extensions.Configuration.RandomValue;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.TaskCore;
using Steeltoe.Management.Tracing;
using Steeltoe.Security.Authentication.CloudFoundry;
using LocalCertificateWriter = Articulate.LocalCerts.LocalCertificateWriter;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // we're doing some hacks with certs here when working locally to do pseudo SNI for c2c vs public routes, so we want to use 
    kestrel.GetType().GetMethod("EnsureDefaultCert", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(kestrel, null);
    var defaultCertificate = (X509Certificate2)kestrel.GetType().GetProperty("DefaultCertificate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(kestrel);
    kestrel.ConfigureHttpsDefaults(https =>
    {
        https.AllowAnyClientCertificate();

        https.ServerCertificateSelector = (context, name) =>
        {
            if (name.EndsWith(".internal") || Regex.IsMatch(name, @"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$"))
            {
                var cert = kestrel.ApplicationServices.GetService<IOptionsMonitor<CertificateOptions>>().CurrentValue.Certificate;
                if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    return cert;

                // Hack for Windoze Bug No credentials are available in the security package 
                // SslStream not working with ephemeral keys
                return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            }

            return defaultCertificate;
        };
        // https.ServerCertificate = X509Certificate2.CreateFromPemFile(Environment.GetEnvironmentVariable("CF_INSTANCE_CERT"), Environment.GetEnvironmentVariable("CF_INSTANCE_KEY"));
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
    });
});
// when running locally, get config from <gitroot>/config folder
if (!File.Exists("appsettings.yaml"))
{
    var configDir = "../config";
    var appName = typeof(Program).Assembly.GetName().Name;
    builder.Configuration
        .AddYamlFile($"{configDir}/{appName}.yaml", false, true)
        .AddYamlFile($"{configDir}/{appName}-{builder.Environment.EnvironmentName}.yaml", true, true);
}
else
{
    builder.Configuration
        .AddYamlFile("appsettings.yaml", false, true)
        .AddYamlFile($"appsettings.{builder.Environment.EnvironmentName}.yaml", true, true)
        .AddCloudFoundry();
}

if (builder.Environment.IsDevelopment())
{
    var task = new LocalCertificateWriter();
    task.Write(builder.Configuration.GetValue<Guid>("vcap:application:organization_id"), builder.Configuration.GetValue<Guid>("vcap:application:space_id"));
    Environment.SetEnvironmentVariable("CF_INSTANCE_CERT", Path.Combine(LocalCertificateWriter.AppBasePath, "GeneratedCertificates", "SteeltoeInstanceCert.pem"));
    Environment.SetEnvironmentVariable("CF_INSTANCE_KEY", Path.Combine(LocalCertificateWriter.AppBasePath, "GeneratedCertificates", "SteeltoeInstanceKey.pem"));
}
builder.Configuration
    .AddCloudFoundryContainerIdentity()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .AddRandomValueSource()
    .AddPlaceholderResolver()
    .AddInMemoryCollection(new Dictionary<string, string>());

var overrideProvider = ((IConfigurationRoot)builder.Configuration).Providers.OfType<MemoryConfigurationProvider>().Last();

builder.AddAllActuators();

var services = builder.Services;
services.AddDistributedTracing();

//services.AddSpringBootAdminClient(); // if sb admin is not running, app will crash
services.AddSingleton(_ => new CloudFoundryApplicationOptions(builder.Configuration));
services.AddSingleton(_ => new CloudFoundryServicesOptions(builder.Configuration));
services.ConfigureCloudFoundryOptions(builder.Configuration);
services.AddScoped<AppEnv>();
services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

var isEurekaBound = builder.Configuration.IsServiceBound<EurekaServiceInfo>();
var isMySqlServiceBound = builder.Configuration.IsServiceBound<MySqlServiceInfo>();
var isSqlServerBound = builder.Configuration.IsServiceBound<SqlServerServiceInfo>();
if (isMySqlServiceBound)
{
    services.AddMySqlHealthContributor(builder.Configuration);
}
else if (isSqlServerBound)
{
    services.AddSqlServerHealthContributor(builder.Configuration);
}
services.AddDbContext<AttendeeContext>(db =>
{
    if (isMySqlServiceBound)
    {
        db.UseMySql(builder.Configuration);
    }
    else if (isSqlServerBound)
    {
        db.UseSqlServer(builder.Configuration);
    }
    else
    {
        var dbFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "users.db");
        db.UseSqlite($"DataSource={dbFile}");
    }
});
services.AddTask<MigrateDbContextTask<AttendeeContext>>(ServiceLifetime.Scoped);

services.AddDiscoveryClient();

if (!isEurekaBound)
{
    overrideProvider.Set("Eureka:Client:ShouldFetchRegistry","false");
    overrideProvider.Set("Eureka:Client:ShouldRegisterWithEureka","false");
}
services.AddCloudFoundryContainerIdentity();
services.AddAuthentication((options) =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CloudFoundryDefaults.AuthenticationScheme;
    })
    .AddCookie((options) =>
    {
        options.AccessDeniedPath = new PathString("/Home/AccessDenied");
    })
    .AddCloudFoundryOAuth(builder.Configuration)
    .AddCloudFoundryIdentityCertificate();

services.AddAuthorization(cfg =>
{
    cfg.AddPolicy(SecurityPolicy.LoggedIn, policy => policy
        .AddAuthenticationSchemes(CloudFoundryDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
    cfg.AddPolicy(CloudFoundryDefaults.SameOrganizationAuthorizationPolicy, policy =>
    {
        policy.AuthenticationSchemes.Add(CertificateAuthenticationDefaults.AuthenticationScheme);
        policy.SameOrg();
    });
    cfg.AddPolicy(CloudFoundryDefaults.SameSpaceAuthorizationPolicy, policy =>
    {
        policy.AuthenticationSchemes.Add(CertificateAuthenticationDefaults.AuthenticationScheme);
        policy.SameSpace();
    });
});
services.PostConfigure<CertificateOptions>(opt =>
{
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
    {
        // work around bug when running on Windows
        opt.Certificate = new X509Certificate2(opt.Certificate.Export(X509ContentType.Pkcs12));
    }
});
services.AddTransient<ClientCertificateHttpHandler>();
var httpClientBuilder = services.AddHttpClient("default")
    .ConfigurePrimaryHttpMessageHandler<ClientCertificateHttpHandler>()
    .AddServiceDiscovery();

if (builder.Environment.IsDevelopment())
{
    services.AddTransient<SimulatedClientCertInHeaderHttpHandler>();
    httpClientBuilder.ConfigurePrimaryHttpMessageHandler<SimulatedClientCertInHeaderHttpHandler>();
}



services
    .AddControllersWithViews()
    .AddRazorRuntimeCompilation();

var app = builder.Build();

app.UseForwardedHeaders();
// app.Use((context, next) =>
// {
//     context.Request.Scheme = "https";
//     return next();
// });
app.UseCookiePolicy(); 
app.UseDeveloperExceptionPage();
app.UseStaticFiles();
app.UseRouting();
app.UseCloudFoundryCertificateAuth();
// app.UseAuthentication();
// app.UseAuthorization();


app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
});
app.EnsureMigrationOfContext<AttendeeContext>();
            
app.Run();
