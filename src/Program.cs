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
using Steeltoe.Common.Hosting;
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
using Steeltoe.Discovery;
using Steeltoe.Discovery.Client;
using Steeltoe.Discovery.Client.SimpleClients;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Extensions.Configuration.Placeholder;
using Steeltoe.Extensions.Configuration.RandomValue;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.TaskCore;
using Steeltoe.Management.Tracing;
using Steeltoe.Security.Authentication.CloudFoundry;
using Steeltoe.Discovery.Eureka;
using Steeltoe.Extensions.Configuration.ConfigServer;
using LocalCertificateWriter = Articulate.LocalCerts.LocalCertificateWriter;

var builder = WebApplication.CreateBuilder(args);
builder.UseCloudFoundryCertificateForInternalRoutes();    
// when running locally, get config from <gitroot>/config folder
// string configDir = "../config/";
// var appName = typeof(Program).Assembly.GetName().Name;
Hotfix.Apply();
builder.Configuration
    .AddYamlFile("appsettings.yaml", false, true)
    .AddYamlFile($"appsettings.{builder.Environment.EnvironmentName}.yaml", true, true)
    .AddProfiles()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .AddCloudFoundry()
    .AddConfigServer()
    .AddProfiles()
    .AddEnvironmentVariables();

if (builder.Environment.IsDevelopment())
{
    builder.UseDevCertificate();
}

builder.Configuration
    .AddCloudFoundryContainerIdentity()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .AddInMemoryCollection(new Dictionary<string, string>
    {
        { "MyConnectionString", "Server=${SqlHost};Database=${SqlDatabase};User Id=${SqlUser};Password=${SqlPassword};" },
        { "AppInstanceId", "${random:value}" }
    })
    .AddRandomValueSource()
    .AddPlaceholderResolver();
    

builder.AddAllActuators();

var services = builder.Services;
services.AddDistributedTracing();

//services.AddSpringBootAdminClient(); 
services.AddSingleton(_ => new CloudFoundryApplicationOptions(builder.Configuration));
services.AddSingleton(_ => new CloudFoundryServicesOptions(builder.Configuration));
services.ConfigureCloudFoundryOptions(builder.Configuration);
services.AddScoped<AppEnv>();
services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
services.AddConfigServerHealthContributor();

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
services.AddTransient<SkipCertValidationHttpHandler>();
services.AddTransient<TasClientCertificateHttpHandler>();

var httpClientBuilder = services.AddHttpClient(Options.DefaultName)
    .ConfigurePrimaryHttpMessageHandler<TasClientCertificateHttpHandler>();


var config = builder.Configuration;
if (isEurekaBound)
{
    services.AddDiscoveryClient();
    httpClientBuilder.AddServiceDiscovery();
    services.AddHttpClient<EurekaDiscoveryClient>("Eureka").ConfigurePrimaryHttpMessageHandler<SkipCertValidationHttpHandler>();
    services.PostConfigure<EurekaInstanceOptions>(c => // use for development to set instance ID and other things for simulated c2c communications
    {
        if (c.RegistrationMethod == "direct")
        {
            config.Bind("Eureka:Instance", c);
        }
    });
}
else
{
    services.AddConfigurationDiscoveryClient(config);
    services.AddSingleton<IDiscoveryClient, ConfigurationDiscoveryClient>();
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
    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) && opt.Certificate != null)
    {
        // work around bug when running on Windows
        opt.Certificate = new X509Certificate2(opt.Certificate.Export(X509ContentType.Pkcs12));
    }
});


    

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
