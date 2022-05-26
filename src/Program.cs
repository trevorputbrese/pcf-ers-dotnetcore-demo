using System.Collections.Generic;
using System.Reflection;
using Articulate;
using Articulate.Models;
using Articulate.Repositories;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Steeltoe.Common;
// using Steeltoe.Bootstrap.Autoconfig;
using Steeltoe.Common.Hosting;
using Steeltoe.Connector.EFCore;
using Steeltoe.Connector.MySql;
using Steeltoe.Connector.Services;
using Steeltoe.Connector.MySql.EFCore;
using Steeltoe.Connector.SqlServer;
using Steeltoe.Connector.SqlServer.EFCore;
using Steeltoe.Discovery.Client;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Extensions.Configuration.Placeholder;
using Steeltoe.Extensions.Logging;
using Steeltoe.Management.CloudFoundry;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.Endpoint.SpringBootAdminClient;
using Steeltoe.Management.TaskCore;
using Steeltoe.Management.Tracing;
using Steeltoe.Security.Authentication.CloudFoundry;

var builder = WebApplication.CreateBuilder(args);
builder
    .AddCloudFoundryConfiguration()
    .AddPlaceholderResolver();
builder.AddAllActuators();
var services = builder.Services;

//services.AddSpringBootAdminClient(); // if sb admin is not running, app will crash

services.AddSingleton(ctx => new CloudFoundryServicesOptions(builder.Configuration));
services.ConfigureCloudFoundryOptions(builder.Configuration);
services.AddScoped<AppEnv>();
services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
services.AddCloudFoundryCertificateAuth();

var isEurekaBound = builder.Configuration.IsServiceBound<EurekaServiceInfo>();
var isMySqlServiceBound = builder.Configuration.IsServiceBound<MySqlServiceInfo>();
var isSqlServerBound = builder.Configuration.IsServiceBound<SqlServerServiceInfo>();
services.AddDistributedTracing();
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

if (isEurekaBound)
{
    services.AddDiscoveryClient();
}

services.AddAuthentication((options) =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CloudFoundryDefaults.AuthenticationScheme;
    })
    .AddCookie((options) =>
    {
        options.AccessDeniedPath = new PathString("/Home/AccessDenied");
    })
    .AddCloudFoundryOAuth(builder.Configuration);

services.AddAuthorization(cfg => cfg
    .AddPolicy(SecurityPolicy.LoggedIn, policy => policy
        .AddAuthenticationSchemes(CloudFoundryDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()));

services.AddControllersWithViews().AddRazorRuntimeCompilation();

var app = builder.Build();

app.UseForwardedHeaders();
app.Use((context, next) =>
{
    context.Request.Scheme = "https";
    return next();
});
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