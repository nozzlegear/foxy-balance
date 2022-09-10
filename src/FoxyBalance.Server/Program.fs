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
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http
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
                route "/balance" >=> Routes.Balance.homePageHandler
                routef "/balance/%d" Routes.Balance.editTransactionHandler
            ]
        ]
        POST >=> choose [
            route "/auth/login" >=> Routes.Auth.loginPostHandler
            route "/auth/register" >=> Routes.Auth.registerPostHandler
            authenticated [
                route "/balance/new" >=> Routes.Balance.newTransactionPostHandler
                routef "/balance/%d/delete" Routes.Balance.deleteTransactionPostHandler
                routef "/balance/%d" Routes.Balance.existingTransactionPostHandler
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

let configureServices (services : IServiceCollection) =
    let add (fn : unit -> _) = fn () |> ignore
    
    add (fun _ -> services.AddCors())
    add (fun _ -> services.AddGiraffe())
    add (fun _ -> services.AddSingleton<Models.IConstants, Models.Constants>())
    add (fun _ -> services.AddSingleton<Models.IDatabaseOptions, Models.DatabaseOptions>())
    add (fun _ -> services.AddScoped<IUserDatabase, UserDatabase>())
    add (fun _ -> services.AddScoped<ITransactionDatabase, TransactionDatabase>())
    add (fun _ -> services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookieAuth))

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    let host =
        WebHost.CreateDefaultBuilder()
            .UseUrls([|"http://+:3000"|])
    //        .UseContentRoot(contentRoot)
            .UseWebRoot(webRoot)
            .Configure(Action<IApplicationBuilder> configureApp)
            .ConfigureServices(configureServices)
            .ConfigureLogging(configureLogging)
            .Build()
    
    // Run post-startup tasks here
    let constants = host.Services.GetRequiredService<Models.IConstants>()
    // Migrate the SQL database to the latest version 
    Migrator.migrate Migrator.Latest constants.ConnectionString
    // Start up the web server 
    host.Run()
    0
