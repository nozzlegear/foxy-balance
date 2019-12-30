namespace FoxyBalance.Server.Views

open Giraffe.GiraffeViewEngine
open FoxyBalance.Server.Models.ViewModels

module Auth =
    let loginPageView (model : LoginViewModel) : XmlNode =
        Shared.pageContainer "Login" Shared.Unauthenticated Shared.WrappedInSection [
            form [_class "login-form"; _method "POST"] [
                Shared.title "Login to your account here."
                
                Shared.textField [
                    Shared.InputFieldOption.Title "Email address"
                    Shared.InputFieldOption.Placeholder "me@example.com"
                    Shared.InputFieldOption.Value (model.Username |> Option.defaultValue "")
                    Shared.InputFieldOption.HtmlName "username" ]
                
                Shared.passwordField [
                    Shared.InputFieldOption.Title "Password"
                    Shared.InputFieldOption.HtmlName "password" ]
                
                Shared.maybeErr Shared.WrappedInField model.Error
                
                Shared.buttonField [
                    Shared.ButtonFieldOption.Label "Login"
                    Shared.ButtonFieldOption.Wrap Shared.WrappedInField
                    Shared.ButtonFieldOption.Color Shared.ButtonColor.Primary
                    Shared.ButtonFieldOption.Type Shared.ButtonType.Submit ]
            ]
        ]
    
    let registerPageView (model : RegisterViewModel) : XmlNode =
        Shared.pageContainer "Create an account" Shared.Unauthenticated Shared.WrappedInSection [
            form [_class "register-form"; _method "POST"] [
                Shared.title "Create an account here."
                
                Shared.textField [
                    Shared.InputFieldOption.Title "Email address"
                    Shared.InputFieldOption.Placeholder "me@example.com"
                    Shared.InputFieldOption.Value (model.Username |> Option.defaultValue "")
                    Shared.InputFieldOption.HtmlName "username" ]
                
                Shared.passwordField [
                    Shared.InputFieldOption.Title "Password"
                    Shared.InputFieldOption.HtmlName "password" ]
                
                Shared.maybeErr Shared.WrappedInField model.Error
                
                Shared.buttonField [
                    Shared.ButtonFieldOption.Label "Create Account"
                    Shared.ButtonFieldOption.Wrap Shared.WrappedInField
                    Shared.ButtonFieldOption.Color Shared.ButtonColor.Primary
                    Shared.ButtonFieldOption.Type Shared.ButtonType.Submit ]
            ]
        ]
