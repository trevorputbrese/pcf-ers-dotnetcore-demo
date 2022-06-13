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
        
        if (IPAddress.TryParse(request.RequestUri!.Host, out _) && request.RequestUri.Host.StartsWith("10")) // don't do validation for internal addresses
        {
            return true;
        }

        
        return chain.Build(certificate);
    }
}