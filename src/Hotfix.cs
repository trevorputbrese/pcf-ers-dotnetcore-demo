using System.Reflection;
using Steeltoe.Connector.SqlServer;

namespace Articulate;

public static class Hotfix
{
    // temporary fixes an issue with steeltoe connector not liking microsoft.data.sqlclient. will be fixed in next version
    public static void Apply()
    {
        var assemblyNames = SqlServerTypeLocator.Assemblies.ToList();
        assemblyNames.Add("Microsoft.Data.SqlClient");
        var assembliesProp = typeof(SqlServerTypeLocator).GetProperty(nameof(SqlServerTypeLocator.Assemblies), BindingFlags.Static | BindingFlags.Public); 
        assembliesProp.SetValue(null, assemblyNames.ToArray());

        
        var typeNames = SqlServerTypeLocator.ConnectionTypeNames.ToList();
        typeNames.Add("Microsoft.Data.SqlClient.SqlConnection");
        var connectionTypeNamesProp = typeof(SqlServerTypeLocator).GetProperty(nameof(SqlServerTypeLocator.ConnectionTypeNames), BindingFlags.Static | BindingFlags.Public); 
        connectionTypeNamesProp.SetValue(null, typeNames.ToArray());
    }
}