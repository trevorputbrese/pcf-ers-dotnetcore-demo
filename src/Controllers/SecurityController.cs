using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Articulate.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Steeltoe.Discovery;
using Steeltoe.Security.Authentication.CloudFoundry;

namespace Articulate.Controllers;

public class SecurityController : Controller
{
    private readonly AppEnv _app;

    public SecurityController(AppEnv app)
    {
        _app = app;
    }

    public IActionResult OpenIdConnect() => View();
    public IActionResult Mtls() => View();

    [Authorize(SecurityPolicy.LoggedIn)]
    public IActionResult SecureEndpoint()
    {
        return View(nameof(OpenIdConnect));
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(OpenIdConnect));
    }

    public async Task<string> CallMtlsService([FromServices]IHttpClientFactory httpFactory, [FromServices]IDiscoveryClient discoveryClient)
    {
        var client = httpFactory.CreateClient();
        
        var anotherAppName = discoveryClient.Services.FirstOrDefault(x => x != _app.AppName);
        if (anotherAppName == null)
        {
            return "Another instance of the app needs to be registered with eureka";
        }

        try
        {
            return await client.GetStringAsync($"https://{anotherAppName}/Security/SecuredByMtls");
        }
        catch (Exception e)
        {
            return e.ToString();
        }
    }
    
    public async Task<string> CallUrl([FromServices]IHttpClientFactory httpFactory, string url)
    {
        var client = httpFactory.CreateClient("default");

        var sb = new StringBuilder();

        void PrintHeaders(HttpHeaders headers)
        {
            foreach (var (name, value) in headers)
            {
                sb.AppendLine($"{name}: {value.FirstOrDefault()}");
            }
        }
        try
        {
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var request = response.RequestMessage;
            sb.AppendLine("======REQUEST=======");
            sb.AppendLine($"{request.Method} {request.RequestUri}");
            PrintHeaders(request.Headers);
            sb.AppendLine("");
            if (request.Content != null)
            {
                sb.AppendLine(await request.Content.ReadAsStringAsync());
            }
            
            sb.AppendLine("======RESPONSE=======");
            sb.AppendLine($"{(int)response.StatusCode} {response.StatusCode}");
            PrintHeaders(response.Headers);
            sb.AppendLine(await response.Content.ReadAsStringAsync());
            return sb.ToString();
        }
        catch (Exception e)
        {
            return e.ToString();
        }
    }
        
    [Authorize(SecurityPolicy.SameSpace)]
    public string SecuredByMtls() => $"I'm app {_app.AppName} and you called successfully on a method protected by MTLs authorization policy";
}