module Program

/// Marker type for WebApplicationFactory to find the assembly
type Marker = class end

open FoxyBalance.Database
open FoxyBalance.Database.Interfaces
open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open FoxyBalance.Server
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting

module Migrator = FoxyBalance.Migrations.Migrator

let allRoutes : HttpHandler =
    // Requires authentication for all routes inside the list
    let authenticated routes =
        let wrapped : HttpHandler list =
            routes
            |> List.map (fun r -> RouteUtils.requiresAuthentication >=> r)
        choose wrapped

    choose [
        // REST API v1 routes
        subRoute "/api/v1" (choose [
            // Auth endpoints (no authentication required)
            POST >=> route "/auth/token" >=> Api.Routes.Auth.tokenExchangeHandler
            POST >=> route "/auth/refresh" >=> Api.Routes.Auth.tokenRefreshHandler

            // Balance endpoints
            GET >=> route "/balance" >=> Api.Routes.Balance.getBalanceHandler

            // Transaction endpoints
            GET >=> route "/transactions" >=> Api.Routes.Transactions.listHandler
            GET >=> routef "/transactions/%d" Api.Routes.Transactions.getHandler
            POST >=> route "/transactions" >=> Api.Routes.Transactions.createHandler
            POST >=> route "/transactions/import" >=> Api.Routes.Transactions.importHandler
            PUT >=> routef "/transactions/%d" Api.Routes.Transactions.updateHandler
            DELETE >=> routef "/transactions/%d" Api.Routes.Transactions.deleteHandler

            // Recurring bills endpoints
            GET >=> route "/bills" >=> Api.Routes.RecurringBills.listHandler
            GET >=> route "/bills/match/suggestions" >=> Api.Routes.BillMatching.getSuggestionsHandler
            GET >=> routef "/bills/%d" Api.Routes.RecurringBills.getHandler
            POST >=> route "/bills" >=> Api.Routes.RecurringBills.createHandler
            POST >=> route "/bills/match" >=> Api.Routes.BillMatching.executeMatchHandler
            POST >=> routef "/bills/%d/toggle-active" Api.Routes.RecurringBills.toggleActiveHandler
            PUT >=> routef "/bills/%d" Api.Routes.RecurringBills.updateHandler
            DELETE >=> routef "/bills/%d" Api.Routes.RecurringBills.deleteHandler
        ])

        // Web UI routes
        GET >=> choose [
            route "/" >=> redirectTo false "/balance"
            route "/home" >=> redirectTo false "/balance"
            route "/auth/logout" >=> Routes.Auth.logoutHandler
            route "/auth/login" >=> Routes.Auth.loginHandler
            route "/auth/register" >=> Routes.Auth.registerHandler
            authenticated [
                route "/balance/clear" >=> text "Not yet implemented"
                route "/balance/adjust-balance" >=> text "Not yet implemented"
                route "/balance/new" >=> Routes.Balance.newTransactionHandler
                route "/balance/upload" >=> Routes.Balance.uploadTransactionsView
                route "/balance" >=> Routes.Balance.homePageHandler
                routef "/balance/%d" Routes.Balance.editTransactionHandler

                route "/bills" >=> Routes.Bills.listBillsHandler
                route "/bills/new" >=> Routes.Bills.newBillHandler
                route "/bills/match" >=> Routes.Bills.matchingInterfaceHandler
                routef "/bills/%d" Routes.Bills.editBillHandler

                route "/income" >=> Routes.Income.homePageHandler
                route "/income/sync" >=> Routes.Income.syncHandler
                route "/income/new" >=> Routes.Income.newRecordHandler
                routef "/income/%d" Routes.Income.recordDetailsHandler
                routef "/income/%d/shopify-details.json" Routes.Income.rawShopifyTransactionHandler
                routef "/income/tax-rate/%i" Routes.Income.taxRateHandler

                route "/api-keys" >=> Routes.ApiKeys.listApiKeysHandler
                route "/api-keys/new" >=> Routes.ApiKeys.newApiKeyHandler
            ]
        ]
        POST >=> choose [
            route "/auth/login" >=> Routes.Auth.loginPostHandler
            route "/auth/register" >=> Routes.Auth.registerPostHandler
            authenticated [
                route "/balance/new" >=> Routes.Balance.newTransactionPostHandler
                route "/balance/upload" >=> Routes.Balance.uploadTransactionsHandler
                routef "/balance/%d/delete" Routes.Balance.deleteTransactionPostHandler
                routef "/balance/%d/match" Routes.Bills.executeMatchHandler
                routef "/balance/%d" Routes.Balance.existingTransactionPostHandler

                route "/bills/new" >=> Routes.Bills.newBillPostHandler
                routef "/bills/%d/delete" Routes.Bills.deleteBillPostHandler
                routef "/bills/%d/toggle" Routes.Bills.toggleActiveBillPostHandler
                routef "/bills/%d" Routes.Bills.existingBillPostHandler

                route "/income/sync" >=> Routes.Income.executeSyncHandler
                route "/income/new" >=> Routes.Income.executeNewRecordHandler
                routef "/income/%d/ignore" Routes.Income.executeToggleIgnoreHandler
                routef "/income/%d/delete" Routes.Income.executeDeleteHandler
                routef "/income/tax-rate/%i" Routes.Income.executeTaxRateHandler

                route "/api-keys/new" >=> Routes.ApiKeys.newApiKeyPostHandler
                routef "/api-keys/%d/revoke" Routes.ApiKeys.revokeApiKeyPostHandler
                routef "/api-keys/%d/delete" Routes.ApiKeys.deleteApiKeyPostHandler
            ]
        ]
        setStatusCode 404 >=> text "Not Found"
    ]

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostEnvironment>()
    let app = app.UseAuthentication()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(allRoutes)

let cookieAuth (options : CookieAuthenticationOptions) =
    options.Cookie.HttpOnly <- true
    options.SlidingExpiration <- true
    // After 60 days of inactivity, the user must sign in again
    options.ExpireTimeSpan <- TimeSpan.FromDays 60.0
    options.LoginPath <- PathString "/auth/login"
    options.LogoutPath <- PathString "/auth/logout"

let configureServices (app : WebHostBuilderContext) (services : IServiceCollection) =
    let add (fn : unit -> _) = fn () |> ignore

    add (fun _ -> services.AddCors())
    add (fun _ -> services.AddGiraffe())
    add (fun _ -> services.AddHttpClient())
    add (fun _ -> services.AddSingleton<Models.IConstants, Models.Constants>())
    add (fun _ -> services.AddSingleton<Models.IDatabaseOptions, Models.DatabaseOptions>())
    // add (fun _ -> services.AddSingleton<Json.ISerializer>(jsonSerializer()))
    add (fun _ -> services.AddScoped<IUserDatabase, UserDatabase>())
    add (fun _ -> services.AddScoped<ITransactionDatabase, TransactionDatabase>())
    add (fun _ -> services.AddScoped<IRecurringBillDatabase, RecurringBillDatabase>())
    add (fun _ -> services.AddScoped<IIncomeDatabase, IncomeDatabase>())
    add (fun _ -> services.AddScoped<IApiKeyDatabase, ApiKeyDatabase>())
    add (fun _ -> services.AddScoped<IRefreshTokenDatabase, RefreshTokenDatabase>())
    add (fun _ -> services.AddScoped<Services.BillMatchingService>())
    add (fun _ -> services.AddScoped<Services.RecurringBillApplicationService>())
    add (fun _ -> services.AddHostedService<Services.RecurringBillBackgroundService>())

    // API authentication services
    let jwtConfig : Api.JwtConfig =
        { SecretKey = app.Configuration.["HashingKey"]
          Issuer = "FoxyBalance"
          Audience = "FoxyBalanceApi"
          AccessTokenLifetime = TimeSpan.FromHours(1.0)
          RefreshTokenLifetime = TimeSpan.FromDays(30.0) }
    add (fun _ -> services.AddSingleton<Api.JwtService>(Api.JwtService(jwtConfig)))
    add (fun _ -> services.AddScoped<Api.ApiKeyService>(fun sp ->
        let apiKeyDb = sp.GetRequiredService<IApiKeyDatabase>()
        let hashingKey = app.Configuration.["HashingKey"]
        Api.ApiKeyService(apiKeyDb, hashingKey)))

    add (fun _ -> services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookieAuth))
    services.AddFoxyBalanceSyncClients()

    services.Configure<GumroadClientOptions>(app.Configuration.GetSection "Gumroad") |> ignore
    services.Configure<ShopifyPartnerClientOptions>(app.Configuration.GetSection "Shopify") |> ignore

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l >= LogLevel.Information)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    let host =
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(fun hostingContext configuration ->
                configuration.AddEnvironmentVariables(prefix = "FoxyBalance_") |> ignore
                configuration.AddKeyPerFile("/run/secrets", optional = true) |> ignore
                // Must come after AddKeyPerFile so it takes priority over individual secret files
                configuration.AddJsonFile("/run/secrets/appsettings.secrets.json", optional = true, reloadOnChange = true) |> ignore
            )
            .ConfigureWebHostDefaults(fun webBuilder ->
                webBuilder.UseUrls([|"http://+:3000"|]) |> ignore
                webBuilder.UseWebRoot(webRoot) |> ignore
                webBuilder.Configure(Action<IApplicationBuilder> configureApp) |> ignore
                webBuilder.ConfigureServices(configureServices) |> ignore
                webBuilder.ConfigureLogging(configureLogging) |> ignore
            )
            .ConfigureLogging(configureLogging)
            .Build()

    // Run post-startup tasks here
    let constants = host.Services.GetRequiredService<Models.IConstants>()
    // Migrate the SQL database to the latest version
    Migrator.migrate Migrator.Latest constants.ConnectionString
    // Start up the web server
    host.Run()
    0
