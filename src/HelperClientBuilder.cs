namespace Articulate;

public class HelperClientBuilder : IHttpClientBuilder
{
    public HelperClientBuilder(IServiceCollection services, string name)
    {
        Services = services;
        Name = name;
    }

    public string Name { get; }

    public IServiceCollection Services { get; }
    
}