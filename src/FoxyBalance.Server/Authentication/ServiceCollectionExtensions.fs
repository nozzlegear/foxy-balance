namespace FoxyBalance.Server.Authentication

open System
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.CookiePolicy
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Options

[<AutoOpen>]
module ServiceCollectionExtensions =
    /// Alias of 'ignore'. Makes it less verbose to ignore service setup
    let inline (~%) x = ignore x

    let configureCookieOptions (options: CookieAuthenticationOptions) =
        options.SlidingExpiration <- true
        options.ExpireTimeSpan <- TimeSpan.FromDays(3)
        options.LogoutPath <- PathString "/Auth/Logout"
        options.LoginPath <- PathString "/Auth/Login"
        options.AccessDeniedPath <- PathString "/Auth/Login"
        options.Cookie.HttpOnly <- true
        options.Cookie.MaxAge <- Nullable (TimeSpan.FromDays 14)
        options.Cookie.SameSite <- SameSiteMode.None
        options.Cookie.SecurePolicy <- CookieSecurePolicy.Always
        options.Validate()

    type IServiceCollection with
        member services.AddCookieSessionAuthentication() =
            let authenticationScheme = CookieSessionAuthenticationHandler.AuthenticationScheme

            let builder =
                services.AddAuthentication(authenticationScheme)
                    .AddScheme<CookieAuthenticationOptions, CookieSessionAuthenticationHandler>(authenticationScheme, configureCookieOptions)

            %services
                .AddOptions<CookieAuthenticationOptions>(authenticationScheme)
                .Validate((fun x -> not x.Cookie.Expiration.HasValue), "Cookie.Expiration is ignored, use ExpireTimeSpan instead.")
                .ValidateOnStart()

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<CookieAuthenticationOptions>, PostConfigureCookieAuthenticationOptions>()
            )

            %services.AddCookiePolicy(fun x ->
                x.MinimumSameSitePolicy <- SameSiteMode.Strict
                x.Secure <- CookieSecurePolicy.Always
                x.HttpOnly <- HttpOnlyPolicy.Always
            )

            %services.AddSingleton<ISessionLoaderUtil, SessionLoaderUtil>()

            builder
