namespace FoxyBalance.Server.Views

open Giraffe.GiraffeViewEngine
open FoxyBalance.Server.Models.ViewModels

module Users =
    let loginPageView (model : LoginViewModel) : XmlNode =
        section [_class "login-page"] [
            form [_class "login-form"; _action "POST"] [
                label [_for "username"] [str "Email address"]
                input [
                    _name "username"
                    _type "text"
                    _value (model.Username |> Option.defaultValue "")
                    _placeholder "me@example.com" ]
                
                label [_for "password"] [str "Password"]
                input [
                    _name "password"
                    _type "text"
                    _value ""
                ]
                
                maybeErr model.Error
            ]
        ]
    

