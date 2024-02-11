namespace FoxyBalance.Server.Authentication

open System
open System.Runtime.CompilerServices
open System.Security.Principal
open FoxyBalance.Server.Models

type ISessionLoaderUtil =
    abstract member LoadSessionFromPrincipal: principal: IPrincipal -> CookieSession

type SessionLoaderUtil() =
    interface ISessionLoaderUtil with
        member this.LoadSessionFromPrincipal(principal: IPrincipal): CookieSession =
            ArgumentNullException.ThrowIfNull(principal, nameof principal)
            match principal with
            | :? CookieSession as cookie -> cookie
            | _ -> raise (SwitchExpressionException principal)
