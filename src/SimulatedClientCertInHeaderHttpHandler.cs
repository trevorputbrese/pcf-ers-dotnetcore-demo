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

    public SimulatedClientCertInHeaderHttpHandler(IOptionsMonitor<CertificateOptions> certOptions, IOptionsMonitor<CertificateForwardingOptions> forwardingOptions) : base(certOptions)
    {
        _forwardingOptions = forwardingOptions;
        // this.ClientCertificateOptions = ClientCertificateOption.Automatic;
        ServerCertificateCustomValidationCallback += (message, certificate2, arg3, arg4) => true;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add(_forwardingOptions.CurrentValue.CertificateHeader, Convert.ToBase64String(ClientCertificates[0].GetRawCertData()));
        return base.SendAsync(request, cancellationToken);
    }
}