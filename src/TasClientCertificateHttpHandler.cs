using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Steeltoe.Common.Http;
using Steeltoe.Common.Options;

namespace Articulate;

public class TasClientCertificateHttpHandler : ClientCertificateHttpHandler
{
    public TasClientCertificateHttpHandler(IOptionsMonitor<CertificateOptions> certOptions) : base(certOptions)
    {
        ServerCertificateCustomValidationCallback = OnServerCertificateValidate;

    }
    private bool OnServerCertificateValidate(HttpRequestMessage request, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors policyErrors)
    {
        return true;
    }
}