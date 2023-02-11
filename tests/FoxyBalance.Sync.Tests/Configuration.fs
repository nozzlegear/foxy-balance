module FoxyBalance.Sync.Tests.Configuration

open System.Reflection
open System.Net.Http

open FoxyBalance.Sync.Models

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Options

type private DummyConfigType = interface end

let startupPath =
    Assembly.GetExecutingAssembly().Location
    |> System.IO.Path.GetDirectoryName

let config =
    ConfigurationBuilder()
        .SetBasePath(startupPath)
        .AddJsonFile("appSettings.json", optional = true)
        .AddUserSecrets<DummyConfigType>(optional = false)
        .AddEnvironmentVariables()
        .Build()

let configureHttpClientFactory(): IHttpClientFactory =
    { new IHttpClientFactory with member _.CreateClient(_: string) = new HttpClient() }

let configureGumroadOptions() =
    let section = config.GetRequiredSection("GUMROAD")
    let options = GumroadClientOptions()
    options.AccessToken <- section.Item "AccessToken"
    options.ApplicationId <- section.Item "ApplicationId"
    options.ApplicationSecret <- section.Item "ApplicationSecret"
    Options.Create(options)

let configureShopifyPartnerOptions() =
    let section = config.GetRequiredSection("Shopify")
    let options = ShopifyPartnerClientOptions()
    options.AccessToken <- section.Item "AccessToken"
    options.OrganizationId <- section.Item "OrganizationId"
    Options.Create(options)
