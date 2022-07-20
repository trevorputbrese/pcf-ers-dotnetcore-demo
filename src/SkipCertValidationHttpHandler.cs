using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Articulate;

public class SkipCertValidationHttpHandler : HttpClientHandler
{
    private bool OnServerCertificateValidate(HttpRequestMessage request, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors policyErrors)
    {
        return true;
    }
}