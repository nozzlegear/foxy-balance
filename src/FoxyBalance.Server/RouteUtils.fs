namespace FoxyBalance.Server

open System.Security.Claims
open Giraffe
open Giraffe.Auth
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Authentication
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models
open Microsoft.AspNetCore.Authentication.Cookies
open FSharp.Control.Tasks.V2.ContextInsensitive

module RouteUtils =
    let authenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme
    
    let redirectToLogin : HttpHandler =
        redirectTo false "/auth/login"
        
    /// Requires the user to be signed in before accessing a route. Redirects to /auth/login if the user is not logged in.
    let requiresAuthentication : HttpHandler =
        requiresAuthentication redirectToLogin

    let authenticateUser (user : User) : HttpHandler =
        fun next ctx -> 
            let claims = [ Claim("UserId", string user.Id, ClaimValueTypes.Integer) ]
            let principal =
                ClaimsIdentity(claims, authenticationScheme)
                |> ClaimsPrincipal
            
            task {
                do! ctx.SignInAsync principal
                return! next ctx
            }
            
    let getSessionFromContext (ctx : HttpContext) : Result<Session, string> =
        if isNull ctx.User then
            Error "User object is null, the user is not authenticated."
        else
            let userId =
                ctx.User.Claims
                |> Seq.tryFind (fun claim -> claim.Type = "UserId")
                |> Option.map (fun claim -> int claim.Value)
            
            match userId with
            | Option.Some userId ->
                Ok { UserId = userId }
            | Option.None ->
                Error "Could not find claim \"UserId\" in list of user claims."
             
    let withSession (fn: Session -> HttpHandler) : HttpHandler =
        requiresAuthentication >=> fun next ctx ->
            match getSessionFromContext ctx with
            | Ok session -> fn session next ctx
            | Error _ -> redirectToLogin next ctx
