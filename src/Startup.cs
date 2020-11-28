using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using Articulate.Models;
using Articulate.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Steeltoe.Connector;
using Steeltoe.Connector.CloudFoundry;
using Steeltoe.Connector.EFCore;
using Steeltoe.Connector.Services;
using Steeltoe.Discovery.Client;
using Steeltoe.Extensions.Configuration.CloudFoundry;
using Steeltoe.Management.CloudFoundry;
using Steeltoe.Management.Endpoint;
using Steeltoe.Management.Endpoint.Env;
using Steeltoe.Management.Tracing;
using Steeltoe.Connector.MySql;
using Steeltoe.Connector.MySql.EFCore;
using Steeltoe.Connector.SqlServer.EFCore;
using Steeltoe.Management.Endpoint.SpringBootAdminClient;
using Steeltoe.Management.TaskCore;
using Steeltoe.Security.Authentication.CloudFoundry;

namespace Articulate
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(ctx => new CloudFoundryServicesOptions(Configuration));
            services.ConfigureCloudFoundryOptions(Configuration);
            services.AddScoped<AppEnv>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddCloudFoundryCertificateAuth(Configuration);

            var isEurekaBound = Configuration.IsServiceBound<EurekaServiceInfo>();
            var isMySqlServiceBound = Configuration.IsServiceBound<MySqlServiceInfo>();
            var isSqlServerBound = Configuration.IsServiceBound<SqlServerServiceInfo>();
            services.AddDistributedTracing(Configuration);
            services.AddDbContext<AttendeeContext>(db =>
            {
                if (isMySqlServiceBound)
                    db.UseMySql(Configuration);
                else if (isSqlServerBound)
                    db.UseSqlServer(Configuration);
                else
                {
                    var dbFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "users.db");
                    db.UseSqlite($"DataSource={dbFile}");
                }
            });
            services.AddTask<MigrateDbContextTask<AttendeeContext>>();
            
            if (isEurekaBound)
            {
                services.AddDiscoveryClient(Configuration);
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
                .AddCloudFoundryOAuth(Configuration);

            services.AddAuthorization(cfg => cfg
                .AddPolicy(SecurityPolicy.LoggedIn, policy => policy
                    .AddAuthenticationSchemes(CloudFoundryDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()));

            services.AddControllersWithViews().AddRazorRuntimeCompilation();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
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
            
            // app.UseDiscoveryClient();
            app.RegisterWithSpringBootAdmin(Configuration);
        }
        
    }
    
    public static class ExtensionMethods
    {
        public static void EnsureMigrationOfContext<T>(this IApplicationBuilder app) where T : DbContext
        {
            var context = app.ApplicationServices.CreateScope().ServiceProvider.GetService<T>();
            context.Database.Migrate();
        }

        public static bool IsServiceBound<T>(this IConfiguration configuration) where T : class => CloudFoundryServiceInfoCreator.Instance(configuration).GetServiceInfos<T>().Any();

    }
}