using Microsoft.EntityFrameworkCore;
using Steeltoe.Connector.CloudFoundry;

namespace Articulate;

public static class ExtensionMethods
{
    public static void EnsureMigrationOfContext<T>(this IApplicationBuilder app) where T : DbContext
    {
        var context = app.ApplicationServices.CreateScope().ServiceProvider.GetService<T>();
        context.Database.Migrate();
    }

    public static bool IsServiceBound<T>(this IConfiguration configuration) where T : class => CloudFoundryServiceInfoCreator.Instance(configuration).GetServiceInfos<T>().Any();

}