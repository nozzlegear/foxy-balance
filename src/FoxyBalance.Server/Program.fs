module Program

#nowarn "20"

open System.Text.Json
open BlazorApp1;
open FoxyBalance.Database
open FoxyBalance.Migrations
open FoxyBalance.Database.Interfaces
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server
open Microsoft.Extensions.Hosting
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open FoxyBalance.Server.Authentication
open Microsoft.Extensions.Configuration

let configureServices (app : WebHostBuilderContext) (services : IServiceCollection) =
    services.AddLogging()

    services.AddCors()
    services.AddControllers()
    services.AddCookieSessionAuthentication()
    services.ConfigureHttpJsonOptions(fun x ->
        x.SerializerOptions.WriteIndented <- app.HostingEnvironment.IsDevelopment()
        x.SerializerOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    )

    services.AddHttpClient()
    services.AddSingleton<Models.IConstants, Models.Constants>()
    services.AddSingleton<Models.IDatabaseOptions, Models.DatabaseOptions>()
    services.AddSingleton<ShopifyPartnerClient>()
    services.AddSingleton<PaypalTransactionParser>()
    services.AddSingleton<GumroadClient>()
    services.AddScoped<IUserDatabase, UserDatabase>()
    services.AddScoped<ITransactionDatabase, TransactionDatabase>()
    services.AddScoped<IIncomeDatabase, IncomeDatabase>()

    services.AddBlazorViews()
    services.AddDatabaseMigrationService(app.Configuration.GetSection "ConnectionStrings")
    services.Configure<GumroadClientOptions>(app.Configuration.GetSection "Gumroad")
    services.Configure<ShopifyPartnerClientOptions>(app.Configuration.GetSection "Shopify")
    ()

let configureAppConfiguration (ctx: WebHostBuilderContext) (configuration: IConfigurationBuilder) =
    configuration.AddUserSecrets(optional = true)
    configuration.AddEnvironmentVariables(prefix = "FoxyBalance_")
    configuration.AddKeyPerFile("/run/secrets", optional = true)
    // Start with the base appsettings.json file, whose settings can be extended by appsettings.{Environment}.json and then appsettings.local.json
    configuration.AddJsonFile("appsettings.json")
    configuration.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional = true)
    configuration.AddJsonFile("appsettings.local.json", optional = true)
    ()

let exitCode = 0

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot = Path.Combine(contentRoot, "WebRoot")
    let builder =
        WebApplicationOptions( Args = args, ContentRootPath = contentRoot, WebRootPath = webRoot )
        |> WebApplication.CreateBuilder

    builder
        .WebHost
        .ConfigureAppConfiguration(configureAppConfiguration)
        .UseUrls([|"https://+:8080"|])
        .ConfigureServices(configureServices)

    let app = builder.Build()

    if builder.Environment.IsDevelopment() then
        app.UseDeveloperExceptionPage()
    else
        app.UseExceptionHandler("/Home/Error")
        app.UseHsts()

    app.UseHttpsRedirection()

    app.UseStaticFiles()
    app.UseRouting()
    app.UseAuthorization()

    app.MapControllerRoute(name = "default", pattern = "{controller=Home}/{action=Index}/{id?}")

    app.Run()

    exitCode
