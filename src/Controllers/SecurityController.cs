using System.Security.Claims;
using System.Threading.Tasks;
using Articulate.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Steeltoe.Security.Authentication.CloudFoundry;

namespace Articulate.Controllers
{
    public class SecurityController : Controller
    {
        private readonly AppEnv _app;

        public SecurityController(AppEnv app)
        {
            _app = app;
        }

        public IActionResult Index() => View();

        [Authorize(SecurityPolicy.SameSpace)]
        public string SecuredByMtls() => $"I'm app {_app.AppName} and you called successfully on a method protected by MTLs authorization policy";

        [Authorize(SecurityPolicy.LoggedIn)]
        public IActionResult SecureEndpoint()
        {
            return View("Index");
        }
        
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}