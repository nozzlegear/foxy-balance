namespace FoxyBalance.Server.Controllers

open FoxyBalance.BlazorViews.Components
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Server
open FoxyBalance.Server.Hashes
open FoxyBalance.Server.Models
open FoxyBalance.Server.Models.RequestModels
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Components.Web
open Microsoft.AspNetCore.Mvc
open System.Security.Claims
open System.Threading.Tasks

[<Route("auth")>]
type AuthController(
    htmlRenderer: HtmlRenderer,
    database: IUserDatabase,
    constants: IConstants,
    signInHandler: IAuthenticationSignInHandler
) =
    inherit Controller()

    [<HttpGet("logout")>]
    member this.GetLogout(): Task<RedirectToActionResult> =
        task {
            do! this.HttpContext.SignOutAsync()
            return this.RedirectToAction("Login")
        }

    [<HttpGet("login")>]
    member this.GetLogin(): ExtendedActionResult =
        Blazor.Component<Pages.Auth>
        |> Blazor.SetParameters [
            "Error" => None
            "Username" => None
            "PageType" => Pages.Auth.AuthPageType.Login
        ]
        |> BlazorView
        // AuthViews.loginPageView { Error = None; Username = None }
        // |> htmlView

    [<HttpPost("login")>]
    member this.PostLoginAsync(
        [<FromForm>] body: LoginRequest
    ): Task<ExtendedActionResult> =
        let errorView str =
            Blazor.Component<Pages.Auth>
            |> Blazor.SetParameters [
                "Error" => Some str
                "Username" => None
                "PageType" => Pages.Auth.AuthPageType.Login
            ]
            |> BlazorView
            // AuthViews.loginPageView { Error = Some str; Username = Some body.Username }
            // |> BlazorViewResult

        task {
            match! database.GetAsync(UserIdentifier.Email body.Username) with
            | None ->
                return errorView "A user with that username does not exist."
            | Some user ->
                let hashesMatch =
                    (Hashed user.HashedPassword, Unhashed body.Password)
                    ||> VerifyHmacSha256Hash constants.HashingKey
                
                match hashesMatch with
                | false ->
                    return errorView "Password is incorrect."
                | true ->
                    let session = CookieSession(user.Id)
                    do! signInHandler.SignInAsync(ClaimsPrincipal(session), AuthenticationProperties())
                    return ExtendedActionResult.RedirectToAction("Index", "Income")
        }

    [<HttpGet("register")>]
    member this.GetRegister(): ExtendedActionResult =
        Blazor.Component<Pages.Home>
        |> Blazor.SetParameters [
            "Error" => None
            "Username" => None
        ]
        |> BlazorView
        // AuthViews.registerPageView { Error = None; Username = None }
        // |> htmlView

    [<HttpPost("register")>]
    member this.PostRegisterAsync(
        [<FromForm>] body: LoginRequest
    ): Task<ExtendedActionResult> =
        let errorView str =
            Blazor.Component<Pages.Home>
            |> Blazor.SetParameters [
                "Error" => Some str
                "Username" => Some body.Username
            ]
            |> BlazorView
            // AuthViews.registerPageView { Error = Some str; Username = Some body.Username }
            // |> htmlView

        task {
            if body.Password.Length < 8 then
                return errorView "Password must be at least eight characters long."
            else
                match! database.ExistsAsync (Email body.Username) with
                | true ->
                    return errorView "A user with that username already exists."
                | false ->
                    let partialUser: PartialUser =
                        { EmailAddress = body.Username
                          HashedPassword =
                              match CreateHmacSha256Hash constants.HashingKey (Unhashed body.Password) with
                              | Hashed hashed -> hashed }
                    let! user = database.CreateAsync partialUser
                    let cookieSession = CookieSession(user.Id)

                    do! signInHandler.SignInAsync(ClaimsPrincipal(cookieSession), AuthenticationProperties())

                    return RedirectToAction ("Index", "Balance")
        }
