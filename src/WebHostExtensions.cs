using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Articulate.LocalCerts;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Options;
using Steeltoe.Common.Options;

namespace Articulate;

public static class WebHostExtensions
{
    public static WebApplicationBuilder UseCloudFoundryCertificateForInternalRoutes(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // we're doing some hacks with certs here when working locally to do pseudo SNI for c2c vs public routes, so we want to use 
            kestrel.GetType().GetMethod("EnsureDefaultCert", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(kestrel, null);
            var defaultCertificate = (X509Certificate2)kestrel.GetType().GetProperty("DefaultCertificate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(kestrel);
            kestrel.ConfigureHttpsDefaults(https =>
            {
                https.AllowAnyClientCertificate();

                https.ServerCertificateSelector = (context, name) =>
                {
                    if (name.EndsWith(".internal") || Regex.IsMatch(name, @"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$"))
                    {
                        var cert = kestrel.ApplicationServices.GetService<IOptionsMonitor<CertificateOptions>>().CurrentValue.Certificate;
                        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                            return cert;

                        // Hack for Windoze Bug No credentials are available in the security package 
                        // SslStream not working with ephemeral keys
                        try
                        {
                            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
                        }
                        catch (Exception)
                        {
                            return defaultCertificate;
                        }
                    }

                    return defaultCertificate;
                };
                // https.ServerCertificate = X509Certificate2.CreateFromPemFile(Environment.GetEnvironmentVariable("CF_INSTANCE_CERT"), Environment.GetEnvironmentVariable("CF_INSTANCE_KEY"));
                https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            });
        });
        return builder;
    }

    public static WebApplicationBuilder UseDevCertificate(this WebApplicationBuilder builder)
    {
        var task = new LocalCertificateWriter();
        task.Write(builder.Configuration.GetValue<Guid>("vcap:application:organization_id"), builder.Configuration.GetValue<Guid>("vcap:application:space_id"));
        Environment.SetEnvironmentVariable("CF_INSTANCE_CERT", Path.Combine(LocalCertificateWriter.AppBasePath, "GeneratedCertificates", "SteeltoeInstanceCert.pem"));
        Environment.SetEnvironmentVariable("CF_INSTANCE_KEY", Path.Combine(LocalCertificateWriter.AppBasePath, "GeneratedCertificates", "SteeltoeInstanceKey.pem"));
        return builder;
    }
}