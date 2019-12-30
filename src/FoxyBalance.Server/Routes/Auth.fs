namespace FoxyBalance.Server.Routes

open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Server
open FoxyBalance.Server.Hashes
open FoxyBalance.Server.Models
open FoxyBalance.Server.Models.RequestModels
module AuthViews = FoxyBalance.Server.Views.Auth

module Auth =
    let logoutHandler : HttpHandler =
        Giraffe.Auth.signOut RouteUtils.authenticationScheme
        >=> RouteUtils.redirectToLogin
    
    let loginHandler : HttpHandler =
        AuthViews.loginPageView { Error = None; Username = None }
        |> htmlView 
        
    let loginPostHandler : HttpHandler =
        fun next ctx -> task {
            let! body = ctx.BindFormAsync<LoginRequest>()
            let error str =
                AuthViews.loginPageView { Error = Some str; Username = Some body.Username }
                |> htmlView 
            let database, constants =
                ctx.GetService<IUserDatabase>(),
                ctx.GetService<IConstants>()
                
            match! database.GetAsync(UserIdentifier.Email body.Username) with
            | None ->
                return! error "A user with that username does not exist." next ctx
            | Some user ->
                let hashesMatch =
                    (Hashed user.HashedPassword, Unhashed body.Password)
                    ||> Hashes.VerifyHmacSha256Hash constants.HashingKey
                
                match hashesMatch with
                | false ->
                    return! error "Password is incorrect." next ctx
                | true ->
                    return! (RouteUtils.authenticateUser user 
                             >=> redirectTo false "/") next ctx }
        
    let registerHandler : HttpHandler =
        AuthViews.registerPageView { Error = None; Username = None }
        |> htmlView 
        
    let registerPostHandler : HttpHandler =
        fun next ctx -> task {
            let! body = ctx.BindFormAsync<LoginRequest>()
            let error str =
                AuthViews.registerPageView { Error = Some str; Username = Some body.Username }
                |> htmlView 
                
            if body.Password.Length < 6 then
                return! error "Password must be at least six characters long." next ctx
            else
                let database, constants =
                    ctx.GetService<IUserDatabase>(),
                    ctx.GetService<IConstants>()
                
                match! database.ExistsAsync (Email body.Username) with
                | true ->
                    return! error "A user with that username already exists." next ctx
                | false ->
                    let partialUser : PartialUser =
                        { EmailAddress = body.Username
                          HashedPassword =
                              match Hashes.CreateHmacSha256Hash constants.HashingKey (Unhashed body.Password) with
                              | Hashed hashed -> hashed }
                    let! user = database.CreateAsync partialUser
                    
                    return! (RouteUtils.authenticateUser user
                             >=> redirectTo false "/home") next ctx }
