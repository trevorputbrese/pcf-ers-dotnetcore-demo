using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Steeltoe.Common.Http;
using Steeltoe.Common.Options;

/// <summary>
/// Appends client certs as http header. This is normally done by GoRouter - this code is purely for simulating behavior locally
/// </summary>
public class SimulatedClientCertInHeaderHttpHandler : ClientCertificateHttpHandler
{
    private readonly IOptionsMonitor<CertificateForwardingOptions> _forwardingOptions;
    private readonly IWebHostEnvironment _environment;

    public SimulatedClientCertInHeaderHttpHandler(IOptionsMonitor<CertificateOptions> certOptions,
        IOptionsMonitor<CertificateForwardingOptions> forwardingOptions,
        IWebHostEnvironment environment,
        IHttpContextAccessor httpContextAccessor) : base(certOptions)
    {
        _forwardingOptions = forwardingOptions;
        _environment = environment;
        ServerCertificateCustomValidationCallback = OnServerCertificateValidate;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // when doing stuff locally and not calling over "internal" route, simulate cert being sent as header
        if (_environment.IsDevelopment() && !request.RequestUri.Host.EndsWith(".internal"))
        {
            request.Headers.Add(_forwardingOptions.CurrentValue.CertificateHeader, Convert.ToBase64String(ClientCertificates[0].GetRawCertData()));
        }

        return base.SendAsync(request, cancellationToken);
    }
    
    private bool OnServerCertificateValidate(HttpRequestMessage request, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors policyErrors)
    {
        // when doing c2c, we don't validate server certificate as it doesn't contain the route. We could do additional validation based on it being issued by same root CA (TAS root CA)
        return true;
    }
}