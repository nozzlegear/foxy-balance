module Program 

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
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting

let allRoutes : HttpHandler =
    choose [
        GET >=> choose [
            route "/auth/login" >=> Routes.Users.loginHandler
            route "/auth/register" >=> Routes.Users.registerHandler
            choose [
                route "/"
                route "/home"
            ] >=> RouteUtils.requiresAuthentication >=> text "This is the home page, but it is not yet implemented"
        ]
        POST >=> choose [
            route "/auth/login" >=> Routes.Users.loginPostHandler
            route "/auth/register" >=> Routes.Users.registerPostHandler
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
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(allRoutes)

let configureServices (services : IServiceCollection) =
    let add (fn : unit -> IServiceCollection) = fn () |> ignore
    
    add (fun _ -> services.AddCors())
    add (fun _ -> services.AddGiraffe())
    add (fun _ -> services.AddSingleton<Models.IConstants, Models.Constants>())
    add (fun _ -> services.AddSingleton<Models.IDatabaseOptions, Models.DatabaseOptions>())
    add (fun _ -> services.AddScoped<IUserDatabase, UserDatabase>())
    add (fun _ -> services.AddScoped<ITransactionDatabase, TransactionDatabase>())

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    WebHost.CreateDefaultBuilder()
        .UseUrls([|"http://+:5000"|])
//        .UseKestrel()
//        .UseContentRoot(contentRoot)
//        .UseIISIntegration()
        .UseWebRoot(webRoot)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()
    0
