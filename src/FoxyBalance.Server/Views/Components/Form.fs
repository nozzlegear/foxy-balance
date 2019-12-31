namespace FoxyBalance.Server.Views.Components

module Form =
    module S = FoxyBalance.Server.Views.Shared
    module G = Giraffe.GiraffeViewEngine
    module A = G.Attributes
    
    type ControlOption =
        | Value of string
        | HtmlName of string
        | LabelText of string
        | Placeholder of string
        | HelpText of string
        | Expanded
        with
        static member Defaults (options : ControlOption list) =
            let inline find defaultValue fn = S.find options defaultValue fn
            
            {| Value = find "" (function | Value str -> Some str | _ -> None)
               HtmlName = find "" (function | HtmlName str -> Some str | _ -> None)
               Label = find "" (function | LabelText str -> Some str | _ -> None)
               Expanded = find false (function | Expanded -> Some true | _ -> None )
               Placeholder = find "" (function | Placeholder str -> Some str | _ -> None)
               HelpText = find None (function | HelpText str -> Some (Some str) | _ -> None) |}
               
    type CheckboxOption =
        | Checked of bool
        | HtmlName of string
        | CheckboxText of string
        with
        static member Defaults (options : CheckboxOption list) =
            let inline find defaultValue fn = S.find options defaultValue fn
            
            {| Checked = find false (function | Checked x -> Some x | _ -> None)
               HtmlName = find "" (function | HtmlName x -> Some x | _ -> None)
               Label = find "" (function | CheckboxText x -> Some x | _ -> None) |}
        
    type ButtonType =
        | Submit
        | Button
        | Link of string
        with
        member x.HtmlTypeAttr : string =
            match x with
            | Button ->
                "button"
            | Submit ->
                "submit"
            | Link _ ->
                "button"
        
    type ButtonColor =
        | Default 
        | Primary
        | PlainLink
        | White
        | Dark
        | Black
        | Text
        | Info
        | Success
        | Warning
        | Danger
        with
        member x.BulmaCssClass : string =
            match x with
            | Default ->
                "" 
            | Primary ->
                "is-primary"
            | PlainLink ->
                "is-link"
            | White ->
                "is-white"
            | Dark ->
                "is-dark"
            | Black ->
                "is-black"
            | Text ->
                "is-text"
            | Info ->
                "is-info"
            | Success ->
                "is-success"
            | Warning ->
                "is-warning"
            | Danger ->
                "is-danger"
    
    type ButtonShade =
        | Light
        | Normal
        with
        member x.BulmaCssClass : string =
            match x with
            | Normal ->
                ""
            | Light ->
                "is-light"
    
    type ButtonOption =
        | ButtonText of string
        | Type of ButtonType
        | Color of ButtonColor
        | Shade of ButtonShade
        | Expanded
        with
        static member Defaults (options : ButtonOption list) =
            let inline find defaultValue fn = S.find options defaultValue fn
            
            {| Label = find "" (function | ButtonText str -> Some str | _ -> None)
               Type = find Button (function | Type btnType -> Some btnType | _ -> None)
               Color = find Default (function | Color color -> Some color | _ -> None)
               Shade = find Normal (function | Shade shade -> Some shade | _ -> None)
               Expanded = find false (function | Expanded -> Some true | _ -> None) |}
        
    type Element =
        | TextInput of ControlOption list
        | PasswordInput of ControlOption list 
        | DateInput of ControlOption list
        | CheckboxInput of CheckboxOption list
        | Button of ButtonOption list
        | MaybeError of string option
        | Title of string
        | Subtitle of string
        | Raw of G.XmlNode list
        | Group of Element list
        
    type Method =
        | Get
        | Post
        
    type EncType =
        | Default
        | Multipart 
        
    type FormOption =
        | Class of string 
        | Method of Method
        | EncType of EncType
        | Action of string
        
    type WrapLevel =
        | TopLevel
        | Nested 
        
    let private label htmlName title =
        G.label [A._class "label"; A._for htmlName] [G.str title]
        
    let private field grouped =
        let fieldClass =
            match grouped with
            | true -> "field is-grouped"
            | false -> "field"
            
        G.div [A._class fieldClass]
        
    let private control expanded =
        let className =
            if expanded then "control is-expanded" else "control"
        
        G.div [A._class className]
        
    let private inputControl typeAttr options : G.XmlNode =
        let defaults = ControlOption.Defaults options
        
        control defaults.Expanded [
            label defaults.HtmlName defaults.Label
            G.input [
                A._class "input"
                A._type typeAttr
                A._placeholder defaults.Placeholder
                A._name defaults.HtmlName
                A._value defaults.Value
            ]
            defaults.HelpText
            |> Option.map (fun text -> G.p [A._class "help"] [G.str text])
            |> S.maybeEl 
        ]
        
    let private textInput = inputControl "text"
    
    let private passwordInput = inputControl "password"
    
    let private dateInput = inputControl "date"
        
    let private checkboxInput options : G.XmlNode =
        let defaults = CheckboxOption.Defaults options
        let inputProps =
            let baseProps = [A._type "checkbox"; A._name defaults.HtmlName]
            match defaults.Checked with
            | true ->
                baseProps@[A._checked]
            | false ->
                baseProps
                
        control false [
            G.label [A._class "checkbox"] [
                G.input inputProps
                G.str defaults.Label
            ]
        ]
        
    let private button options : G.XmlNode =
        let defaults = ButtonOption.Defaults options
        let className =
            let list = ["button"; defaults.Color.BulmaCssClass; defaults.Shade.BulmaCssClass]
            System.String.Join(" ", list)
        let buttonEl =  
            match defaults.Type with
            | ButtonType.Submit
            | ButtonType.Button ->
                G.button [A._class className; A._type defaults.Type.HtmlTypeAttr] [
                    G.str defaults.Label
                ]
            | ButtonType.Link href ->
                G.a [A._class className; A._href href] [
                    G.str defaults.Label
                ]
                
        control defaults.Expanded [buttonEl]
        
        
    let create (options : FormOption list) (children : Element list) : G.XmlNode =
        let props =
            let rec mapNextProp current remaining =
                match remaining with
                | [] ->
                    current 
                | Class str :: rest ->
                    mapNextProp (current@[A._class str]) rest
                | Method Get :: rest ->
                    mapNextProp (current@[A._method "GET"]) rest
                | Method Post :: rest ->
                    mapNextProp (current@[A._method "POST"]) rest
                | Action str :: rest ->
                    mapNextProp (current@[A._action str]) rest
                | EncType Default :: rest ->
                    mapNextProp (current@[A._enctype "application/x-www-form-urlencoded"]) rest
                | EncType Multipart :: rest ->
                    mapNextProp (current@[A._enctype "multipart/form-data"]) rest
            mapNextProp [] options
        let children =
            let rec mapNextChild wrapLevel rest (current : G.XmlNode list) =
                let maybeWrap node =
                    match wrapLevel with
                    | TopLevel ->
                        field false [node]
                    | Nested ->
                        node
                let next rest newNodes =
                    current @ newNodes
                    |> mapNextChild wrapLevel rest
                
                match rest with
                | [] ->
                    current
                | TextInput options :: rest ->
                    [textInput options |> maybeWrap]
                    |> next rest 
                | PasswordInput options :: rest ->
                    [passwordInput options |> maybeWrap]
                    |> next rest
                | DateInput options :: rest ->
                    [dateInput options |> maybeWrap]
                    |> next rest
                | CheckboxInput options :: rest ->
                    [checkboxInput options |> maybeWrap]
                    |> next rest
                | Button options :: rest ->
                    [button options |> maybeWrap]
                    |> next rest
                | MaybeError err :: rest ->
                    [S.maybeErr err |> maybeWrap]
                    |> next rest
                | Title str :: rest ->
                    // Title is not wrapped in a field
                    [S.title str]
                    |> next rest
                | Subtitle str :: rest ->
                    // Subtitle is not wrapped in a field
                    [S.subtitle str]
                    |> next rest
                | Raw els :: rest ->
                    // Developer is responsible for wrapping elements in fields if necessary
                    els
                    |> next rest
                | Group els :: rest ->
                    // Recursively create another group of elements, but these ones should not be wrapped in fields
                    let groupedElements = mapNextChild WrapLevel.Nested els []
                    
                    [field true groupedElements]
                    |> next rest
                    
            mapNextChild WrapLevel.TopLevel children []
            
        G.form props children 
