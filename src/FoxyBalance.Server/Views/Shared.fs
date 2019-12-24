﻿namespace FoxyBalance.Server.Views

open Giraffe.GiraffeViewEngine
open Giraffe.GiraffeViewEngine.Accessibility

module Shared =
    let maybeEl nodeOption : XmlNode =
        match nodeOption with
        | Some el -> el
        | None -> emptyText
    
    let head pageTitle : XmlNode =
        head [] [
            title [] [str pageTitle]
            meta [_charset "UTF-8"]
            meta [_httpEquiv "X-UA-Compatible"; _content "IE=edge"]
            meta [_name "viewport"; _content "width=device-width, initial-scale=1.0"]
            link [
                _rel "shortcut icon"
                _href "/public/favicon.ico"
                _type "image/x-icon"
            ]
            link [
                _rel "stylesheet"
                _href "https://cdn.jsdelivr.net/npm/bulma@0.8.0/css/bulma.min.css"
                _integrity "sha256-D9M5yrVDqFlla7nlELDaYZIpXfFWDytQtiV+TaH6F1I="
                _crossorigin "anonymous"
            ]
        ]
        
    let nav : XmlNode =
        nav [_class "navbar has-shadow is-spaced"; _roleNavigation; _ariaLabel "main navigation"] [
            div [_class "navbar-brand"] [
                a [_class "navbar-item"; _href "https://bulma.io"] [
                    img [
                        _src "https://bulma.io/images/bulma-logo.png"
                        _width "112"
                        _height "28"
                    ]
                ]
                a [_roleButton; _class "navbar-burger burger"; _ariaLabel "menu"; _ariaExpanded "false"; _data "target" "navbarBasicExample"] [
                    span [_ariaHidden "true"] []
                    span [_ariaHidden "true"] []
                    span [_ariaHidden "true"] []
                ]
            ]
            
            div [_id "navbar"; _class "navbar-menu"] [
                div [_class "navbar-start"] [
                    a [_class "navbar-item"] [
                        str "Home"
                    ]

                    a [_class "navbar-item"] [
                        str "Documentation"
                    ]

                    div [_class "navbar-item has-dropdown is-hoverable"] [
                        a [_class "navbar-link"] [
                            str "More"
                        ]

                        div [_class "navbar-dropdown"] [
                            a [_class "navbar-item"] [
                                str "About"
                            ]
                            a [_class "navbar-item"] [
                                str "Jobs"
                            ]
                            a [_class "navbar-item"] [
                                str "Contact"
                            ]
                            hr [_class "navbar-divider"]
                            a [_class "navbar-item"] [
                                str "Report an issue"
                            ]
                        ]
                    ]
                ]
                
                div [_class "navbar-end"] [
                    div [_class "navbar-item"] [
                        div [_class "buttons"] [
                            a [_class "button is-primary"] [
                                strong [] [str "Sign up"]
                            ]
                            a [_class "button is-light"] [
                                str "Log in"
                            ] 
                        ]
                    ]
                ]
            ]
        ]
        
    let section children : XmlNode =
        section [_class "section"] children 

    type SectionWrap =
        | WrappedInSection
        | NoSectionWrap
        
    type FieldWrap =
        | WrappedInField
        | NoFieldWrap
    
    let pageContainer pageTitle sectionOption children : XmlNode =
        let children =
            match sectionOption with
            | WrappedInSection -> [section children]
            | NoSectionWrap -> children
            
        html [_lang "en"] [
            head pageTitle
            body [] [
                nav
                div [_id "content-host"; _class "container"] children 
            ]
        ]
        
    let field = div [_class "field"]
        
    type FieldOptions =
        { Title : string
          HtmlName : string }
        
    let formField options control =
        field [
            label [_class "label"; _for options.HtmlName] [str options.Title]
            div [_class "control"] control 
        ]
        
    let inline private find input defaultValue fn =
        let reducer (state : _ option) el : _ option =
            match fn el with
            | Some x -> Some x
            | None -> state  
        input
        |> Seq.fold reducer Option.None
        |> Option.defaultValue defaultValue

    type InputFieldOption =
        | Value of string
        | Placeholder of string
        | Title of string
        | HtmlName of string 
    
    let private inputField inputType options =
        let find = find options 
        let defaults =
            {| Value =
                   find "" (function | Value x -> Some x | _ -> None)
               Placeholder =
                   find "" (function | Placeholder x -> Some x | _ -> None)
               Title =
                   find "" (function | Title x -> Some x | _ -> None)
               HtmlName =
                   find "" (function | HtmlName x -> Some x | _ -> None) |}
                   
        formField { Title = defaults.Title; HtmlName = defaults.HtmlName } [
            input [_type inputType; _value defaults.Value; _class "input" ]
        ]
    
    let textField options = inputField "text" options
    
    let passwordField options = inputField "password" options
    
    type ButtonType =
        | Submit
        | Button
        
    type ButtonColor =
        | Default 
        | Primary
        | Link
        | White
        | Dark
        | Black
        | Text
        | Info
        | Success
        | Warning
        | Danger
    
    type ButtonShade =
        | Light
        | Normal 
    
    type ButtonFieldOption =
        | Type of ButtonType
        | Label of string
        | Color of ButtonColor
        | Shade of ButtonShade
        | Wrap of FieldWrap
    
    let buttonField options =
        let find defaultValue fn = find options defaultValue fn 
        let buttonType =
            match find Button (function | Type x -> Some x | _ -> None) with
            | Button -> "button"
            | Submit -> "submit"
        let label =
            find "" (function | Label x -> Some x | _ -> None)
        let classes =
            let classList =
                [ yield "button"
                
                  match find Default (function | Color x -> Some x | _ -> None) with
                  | Default ->
                      () 
                  | Primary ->
                      yield "is-primary"
                  | Link ->
                      yield "is-link"
                  | White ->
                      yield "is-white"
                  | Dark ->
                      yield "is-dark"
                  | Black ->
                      yield "is-black"
                  | Text ->
                      yield "is-text"
                  | Info ->
                      yield "is-info"
                  | Success ->
                      yield "is-success"
                  | Warning ->
                      yield "is-warning"
                  | Danger ->
                      yield "is-danger"
                    
                  match find Normal (function | Shade x -> Some x | _ -> None) with
                  | Normal ->
                      ()
                  | Light ->
                      yield "is-light" ]
  
            System.String.Join(" ", classList)          
        
        let el = 
            p [_class "control"] [
                button [_class classes; _type buttonType] [
                    str label 
                ]
            ]
        
        match find NoFieldWrap (function | Wrap x -> Some x | _ -> None) with
        | NoFieldWrap ->
            el
        | WrappedInField ->
            field [el]

    let title x = h1 [_class "title"] [str x]
    
    let subtitle x = h2 [_class "subtitle"] [str x]
    
    let error wrap text =
        let el =
            p [_class "error red"] [str text]
            
        match wrap with
        | NoFieldWrap ->
            el
        | WrappedInField ->
            field [el]
    
    let maybeErr wrap errorMessage : XmlNode =
        errorMessage
        |> Option.map (error wrap)
        |> maybeEl