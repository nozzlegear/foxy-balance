namespace FoxyBalance.Server.Views

open Giraffe.GiraffeViewEngine
open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels

module Auth =
    type private ViewType =
        | Login of LoginViewModel
        | Register of RegisterViewModel
    
    let private sharedView viewType : XmlNode =
        let model =
            match viewType with
            | Login model ->
                {| FormTitle = "Login to your account."
                   PageTitle = "Login"
                   ButtonText = "Login"
                   Username = model.Username
                   Error = model.Error |}
            | Register model ->
                {| FormTitle = "Create an account."
                   PageTitle = "Create Account"
                   ButtonText = "Create Account"
                   Username = model.Username
                   Error = model.Error |}
                   
        Shared.pageContainer model.PageTitle Shared.Unauthenticated Shared.WrappedInSection [
            Form.create [Form.Method Form.Post] [
                Form.Title model.FormTitle
                
                Form.TextInput [
                    Form.LabelText "Email address"
                    Form.Placeholder "me@example.com"
                    Form.Value (model.Username |> Option.defaultValue "")
                    Form.HtmlName "username" ]
                
                Form.PasswordInput [
                    Form.LabelText "Password"
                    Form.HtmlName "password" ]
                
                Form.MaybeError model.Error
                
                Form.Element.Button [
                    Form.ButtonOption.ButtonText model.ButtonText
                    Form.Color Form.Primary
                    Form.Type Form.Submit ]
            ]
        ]
    
    
    let loginPageView (model : LoginViewModel) : XmlNode =
        sharedView (Login model)
    
    let registerPageView (model : RegisterViewModel) : XmlNode =
        sharedView (Register model)
