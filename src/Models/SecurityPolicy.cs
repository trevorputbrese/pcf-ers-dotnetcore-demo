using Steeltoe.Security.Authentication.CloudFoundry;

namespace Articulate.Models;

public class SecurityPolicy
{
    public const string LoggedIn = "loggedin";
    public const string SameSpace = CloudFoundryDefaults.SameSpaceAuthorizationPolicy;
        
}