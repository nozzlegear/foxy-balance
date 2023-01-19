namespace FoxyBalance.Server.Views

open FoxyBalance.Server.Models.ViewModels
open Giraffe.ViewEngine
open Giraffe.ViewEngine.Accessibility
open FoxyBalance.Database.Models

module Shared =
    let statusFilterText status =
        match status with
        | AllTransactions ->
            "All"
        | PendingTransactions ->
            "Pending"
        | ClearedTransactions ->
            "Cleared"
            
    let statusFilterQueryParam status =
        match status with
        | AllTransactions ->
            "all"
        | PendingTransactions ->
            "pending"
        | ClearedTransactions ->
            "cleared"
            
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
            // Icons
            link [_rel "shortcut icon"; _href "/Images/Icons/favicon.ico"; _type "image/x-icon" ]
            link [_rel "apple-touch-icon"; _href "/Images/Icons/apple-touch-icon.png" ]
            link [_rel "apple-touch-icon"; _sizes "57x57"; _href "/Images/Icons/apple-touch-icon-57x57.png" ]
            link [_rel "apple-touch-icon"; _sizes "72x72"; _href "/Images/Icons/apple-touch-icon-72x72.png" ]
            link [_rel "apple-touch-icon"; _sizes "76x76"; _href "/Images/Icons/apple-touch-icon-76x76.png" ]
            link [_rel "apple-touch-icon"; _sizes "114x114"; _href "/Images/Icons/apple-touch-icon-114x114.png" ]
            link [_rel "apple-touch-icon"; _sizes "120x120"; _href "/Images/Icons/apple-touch-icon-120x120.png" ]
            link [_rel "apple-touch-icon"; _sizes "144x144"; _href "/Images/Icons/apple-touch-icon-144x144.png" ]
            link [_rel "apple-touch-icon"; _sizes "152x152"; _href "/Images/Icons/apple-touch-icon-152x152.png" ]
            link [_rel "apple-touch-icon"; _sizes "180x180"; _href "/Images/Icons/apple-touch-icon-180x180.png" ]
            // Manifest for PWA capabilities
            link [_rel "manifest"; _href "/manifest.json"]
            // CSS stylesheets
            link [
                _rel "stylesheet"
                _href "https://cdn.jsdelivr.net/npm/bulma@0.8.0/css/bulma.min.css"
                _integrity "sha256-D9M5yrVDqFlla7nlELDaYZIpXfFWDytQtiV+TaH6F1I="
                _crossorigin "anonymous"
            ]
            link [
                _rel "stylesheet"
                _href "/main.css"
            ]
            // Let the app be installed as a full screen web app on mobile devices
            meta [_name "mobile-web-app-capable"; _content "yes"]
            meta [_name "mobile-web-app-title"; _content "Foxy Balance"]
            meta [_name "apple-mobile-web-app-capable"; _content "yes"]
            meta [_name "apple-mobile-web-app-title"; _content "Foxy Balance"]
        ]
    
    type AuthStatus =
        | Unauthenticated
        | Authenticated
    
    let nav authStatus : XmlNode =
        let authButtons =
            match authStatus with
            | Authenticated ->
                [
                    a [_class "button is-light"; _href "/auth/logout"] [
                        str "Log out"
                    ]
                ]
            | Unauthenticated ->
                [
                    a [_class "button is-primary"; _href "/auth/register"] [
                        strong [] [str "Sign up"]
                    ]
                    a [_class "button is-light"; _href "/auth/login"] [
                        str "Log in"
                    ] 
                ]
                
        
        nav [_class "navbar has-shadow is-spaced"; _roleNavigation; _ariaLabel "main navigation"] [
            div [_class "navbar-brand"] [
                a [_class "navbar-item"; _href "/"] [
                    img [_src "/Images/logo.png"; _height "28px"]
                ]
                a [_roleButton; _class "navbar-burger burger"; _ariaLabel "menu"; _ariaExpanded "false"; _data "target" "navbarBasicExample"] [
                    span [_ariaHidden "true"] []
                    span [_ariaHidden "true"] []
                    span [_ariaHidden "true"] []
                ]
            ]
            
            div [_id "navbar"; _class "navbar-menu"] [
                div [_class "navbar-start"] [
                    a [_class "navbar-item"; _href "/balance"] [
                        str "Balance"
                    ]

                    a [_class "navbar-item"; _href "/income"] [
                        str "Income"
                    ]

                    a [_class "navbar-item"; _href "/expenses"] [
                        str "Expenses"
                    ]

                    a [_class "navbar-item"; _href "https://github.com/nozzlegear/foxy-balance"; _target "blank"] [
                        str "Open Source"
                    ]
                ]
                
                div [_class "navbar-end"] [
                    div [_class "navbar-item"] [
                        div [_class "buttons"] authButtons
                    ]
                ]
            ]
        ]
        
    let section children : XmlNode =
        section [_class "section"] children 

    type SectionWrap =
        | WrappedInSection
        | NoSectionWrap
    
    let pageContainer pageTitle isAuthenticated sectionWrap children : XmlNode =
        let children =
            match sectionWrap with
            | WrappedInSection -> [section children]
            | NoSectionWrap -> children
            
        html [_lang "en"] [
            head pageTitle
            body [] [
                nav isAuthenticated 
                div [_id "content-host"; _class "container"] children 
            ]
        ]
        
    let inline find input defaultValue fn =
        let reducer (state : _ option) el : _ option =
            match fn el with
            | Some x -> Some x
            | None -> state  
        input
        |> Seq.fold reducer Option.None
        |> Option.defaultValue defaultValue

    let title x = h1 [_class "title"] [str x]
    
    let subtitleFromList els = h2 [_class "subtitle"] els
    
    let subtitle x = subtitleFromList [str x]
    
    let error text =
        p [_class "error has-text-danger"] [str text]
    
    let maybeErr errorMessage : XmlNode =
        errorMessage
        |> Option.map error
        |> maybeEl

    type LevelItem =
        | HeadingAndTitle of string * string
        | HeadingAndTitleLink of {| heading: string; title: string; url: string |}
        | Element of XmlNode 
    
    let private levelItem = function
        | HeadingAndTitle (heading, title) ->
            div [_class "level-item has-text-centered"] [
                div [] [
                    p [_class "heading"] [str heading]
                    p [_class "title"] [str title]
                ]
            ]
        | HeadingAndTitleLink options ->
            div [_class "level-item has-text-centered"] [
                div [] [
                    p [_class "heading"] [str options.heading]
                    p [_class "title"] [
                        a [_href options.url; _title options.title] [
                            str options.title
                        ]
                    ]
                ]
            ]
        | Element el ->
            div [_class "level-item"] [el]
   
    let evenlySpacedLevel levelItems =
        levelItems
        |> List.map levelItem
        |> HtmlElements.nav [_class "level"]
    
    type LevelSection =
        | LeftLevel of LevelItem list
        | RightLevel of LevelItem list
    
    let level items =
        let left, right =
            let folder (leftState, rightState) itemGroup =
                match itemGroup with
                | LeftLevel items ->
                    leftState@items, rightState
                | RightLevel items ->
                    leftState, rightState@items
             
            Seq.fold folder ([], []) items 
                
        HtmlElements.nav [_class "level"] [
            left
            |> List.map levelItem
            |> div [_class "level-left"]
            
            right
            |> List.map levelItem
            |> div [_class "level-right"]
        ]
        
    type TableCell =
        | TableCell of XmlNode 
        
    type TableRow =
        | TableRow of TableCell list
        
    type TableSection =
        | TableHead of TableCell list 
        | TableBody of TableRow list
        
    let table tableOptions =
        let headCell = function
            | TableCell el ->
                th [] [el]
        let bodyCell = function
            | TableCell el ->
                td [] [el]
        let row = function
            | TableRow cells ->
                List.map bodyCell cells
                |> tr []
        let headCells, tableRows =
            let folder (headState, rowState) option =
                match option with
                | TableHead cells ->
                    headState@(List.map headCell cells), rowState
                | TableBody rows ->
                    headState, rowState@(List.map row rows)
            List.fold folder ([], []) tableOptions
        
        table [_class "table is-striped is-hoverable is-fullwidth"] [
            // Only show the table head if it isn't empty
            (match headCells with
             | [] -> None
             | cells -> Some (thead [] [tr [] cells]))
            |> maybeEl
            
            tbody [] tableRows
        ]
        
    let pagination (options: PaginationOptions) =
        let statusQueryParam =
            statusFilterQueryParam options.StatusFilter
        let route =
            match options.RouteType with
            | Balance -> "balance"
            | Income -> "income"
        let previousPageAttrs =
            let baseAttrs = [
                _class "pagination-previous"
                options.CurrentPage - 1
                |> sprintf "/balance?status=%s&page=%i" statusQueryParam
                |> _href 
            ]
            // Disable the previous page link if the user is on the first page
            match options.CurrentPage <= 1 with
            | true -> baseAttrs@[_disabled]
            | false -> baseAttrs
        let nextPageAttrs =
            let baseAttrs = [
                _class "pagination-next"
                options.CurrentPage + 1
                |> sprintf "/%s?status=%s&page=%i" route statusQueryParam
                |> _href
            ]
            // Disable the next page link if the user is on the last page
            match options.CurrentPage + 1 > options.MaxPages with
            | true -> baseAttrs@[_disabled]
            | false -> baseAttrs
        let pageLinks =
            let link page =
                let attrs =
                    if page = options.CurrentPage then
                        // Do not add a link to the current page
                        [ _class "pagination-link is-current" ]
                    else
                        [ _class "pagination-link"
                          _href (sprintf "/%s?status=%s&page=%i" route statusQueryParam page)
                          _ariaLabel (sprintf "Go to page %i" page) ]
                    
                li [] [
                    a attrs [
                        str $"{page}"
                    ] 
                ]
            let ellipsis =
                li [] [
                    span [_class "pagination-ellipsis"] [str "…"]
                ]

            [ for page in [1..options.MaxPages] do
                if options.MaxPages <= 5 then
                    // Show all pages as a link
                    yield link page
                else 
                    match page with
                    | 1 ->
                        yield link page 
                    | x when x = options.CurrentPage ->
                        yield link page 
                    | x when x = options.MaxPages ->
                        yield link page
                    | x when x + 1 = options.CurrentPage || x - 1 = options.CurrentPage ->
                        yield link page 
                    | x when x + 2 = options.CurrentPage || x - 2 = options.CurrentPage ->
                        yield ellipsis
                    | _ ->
                        () ]
            
        HtmlElements.nav [_class "pagination is-centered"; _roleNavigation; _ariaLabel "pagination"] [
            a previousPageAttrs [str "Previous"]
            a nextPageAttrs [str "Next"]
            ul [_class "pagination-list"] pageLinks
        ]
