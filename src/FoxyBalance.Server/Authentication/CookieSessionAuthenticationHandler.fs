namespace FoxyBalance.Server.Authentication

open System.Security.Claims
open System.Text.Encodings.Web
open System.Threading.Tasks
open FoxyBalance.Server.Models
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options

type CookieSessionAuthenticationHandler(
    options: IOptionsMonitor<CookieAuthenticationOptions>,
    logger: ILoggerFactory,
    encoder: UrlEncoder
) =
    inherit CookieAuthenticationHandler(options, logger, encoder)

    static AuthenticationScheme = "CookieSession";

    override this.HandleAuthenticateAsync(): Task<AuthenticateResult> =
        let baseAuthentication = base.AuthenticateAsync()

        task {
            let! result = baseAuthentication

            if not result.Succeeded then
                return result
            else
                // Change the principal so it uses a CookieSession identity
                let session = CookieSession(result.Principal.Claims)
                let principal = ClaimsPrincipal(session);
                let ticket = AuthenticationTicket(principal, CookieSessionAuthenticationHandler.AuthenticationScheme);

                return AuthenticateResult.Success(ticket)
        }
