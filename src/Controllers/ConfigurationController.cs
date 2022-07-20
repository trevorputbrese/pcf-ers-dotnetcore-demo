using Microsoft.AspNetCore.Mvc;

namespace Articulate.Controllers;

public class ConfigurationController : Controller
{
    IConfigurationRoot _config;
    public ConfigurationController(IConfiguration configuration)
    {
        _config = (IConfigurationRoot)configuration;
    }

    public IActionResult Index() => View();
    public Dictionary<string, string> GetConfiguration(int id)
    {
        return new ConfigurationRoot(_config.Providers.Skip(id).Take(1).ToList())
            .AsEnumerable()
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Value);
    }

    public Dictionary<int, string> GetProviders()
    {
        return _config.Providers.Select((provider, i) =>
        {
            // var type = provider.GetType();
            string name = provider.GetType().Name;
            if (provider is FileConfigurationProvider file)
                name += $"[{Path.GetFileName(file.Source.Path)}]";
            return (id: i, name);
        })
        .ToDictionary(x => x.id, x => x.name);
    }
    
    
}