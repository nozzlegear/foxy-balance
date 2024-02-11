using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp1;

public static class ServiceCollectionExtensions
{
    public static void AddBlazorViews(this IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var env = provider.GetRequiredService<IHostEnvironment>();

        services.AddRazorComponents(x => x.DetailedErrors = !env.IsProduction())
            .AddInteractiveServerComponents();
        services.AddScoped<HtmlRenderer>();
    }
}
