namespace FoxyBalance.Server.Views.Components

module Form =
    module S = FoxyBalance.Server.Views.Shared
    module G = Giraffe.ViewEngine.HtmlElements
    module A = Giraffe.ViewEngine.Attributes
    
    type ControlOption =
        | Value of string
        | HtmlName of string
        | LabelText of string
        | Placeholder of string
        | HelpText of string
        | Min of decimal
        | Max of decimal
        | Step of decimal 
        | Required
        with
        static member Defaults (options : ControlOption list) =
            let inline find defaultValue fn = S.find options defaultValue fn
            
            {| Value = find "" (function | Value str -> Some str | _ -> None)
               HtmlName = find "" (function | HtmlName str -> Some str | _ -> None)
               Label = find "" (function | LabelText str -> Some str | _ -> None)
               Placeholder = find "" (function | Placeholder str -> Some str | _ -> None)
               HelpText = find None (function | HelpText str -> Some (Some str) | _ -> None)
               Required = find false (function | Required -> Some true | _ -> None)
               Min = find None (function | Min x -> Some (Some x) | _ -> None)
               Max = find None (function | Max x -> Some (Some x) | _ -> None)
               Step = find None (function | Step x -> Some (Some x) | _ -> None) |}
               
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
    
    type SelectOption =
        | Value of string
        | Options of {| Value : string; Label : string; Selected : bool |} list
        | HtmlName of string
        | LabelText of string
        | Required
        with
        static member Defaults (options : SelectOption list) =
            let inline find defaultValue fn = S.find options defaultValue fn
            
            {| Value = find "" (function | Value x -> Some x | _ -> None)
               Options = find [] (function | Options x -> Some x | _ -> None)
               HtmlName = find "" (function | HtmlName x -> Some x | _ -> None)
               LabelText = find "" (function | LabelText x -> Some x | _ -> None)
               Required = find false (function | Required -> Some true | _ -> None) |}
        
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
                
    type ButtonPull =
        | Left
        | Right
        | NoPull
        with
        member x.BulmaCssClass : string =
            match x with
            | NoPull ->
                ""
            | Left ->
                "is-pulled-left"
            | Right ->
                "is-pulled-right"
                
    type ButtonAlignment =
        | Left
        | Right
        | DefaultAlignment
        with
        member x.BulmaCssClass : string =
            match x with
            | Left ->
                "has-text-left-tablet"
            | Right ->
                "has-text-right-tablet"
            | DefaultAlignment ->
                ""
    
    type ButtonOption =
        | ButtonText of string
        | Type of ButtonType
        | Color of ButtonColor
        | Shade of ButtonShade
        | Alignment of ButtonAlignment
        | ButtonHtmlName of string
        | ButtonFormAction of string
        | OnClick of string 
        with
        static member Defaults (options : ButtonOption list) =
            let inline find defaultValue fn = S.find options defaultValue fn
            
            {| Label = find "" (function | ButtonText str -> Some str | _ -> None)
               Type = find Button (function | Type btnType -> Some btnType | _ -> None)
               Color = find Default (function | Color color -> Some color | _ -> None)
               Shade = find Normal (function | Shade shade -> Some shade | _ -> None)
               HtmlName = find "" (function | ButtonHtmlName str -> Some str | _ -> None)
               OnClick = find None (function | OnClick str -> Some (Some str) | _ -> None)
               FormAction = find None (function | ButtonFormAction str -> Some (Some str) | _ -> None)
               Alignment = find DefaultAlignment (function | Alignment x -> Some x | _ -> None) |}
        
    type Element =
        | TextInput of ControlOption list
        | PasswordInput of ControlOption list 
        | DateInput of ControlOption list
        | NumberInput of ControlOption list
        | SelectBox of SelectOption list
        | CheckboxInput of CheckboxOption list
        | Button of ButtonOption list
        | MaybeError of string option
        | Title of string
        | Subtitle of string
        | Raw of G.XmlNode list
        | Group of Element list
        | MaybeElement of Element option
        | EmptyDiv
        
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
        | AutoComplete of bool
        
    let private label htmlName title =
        G.label [A._class "label"; A._for htmlName] [G.str title]
        
    let private control children =
        G.div [A._class "field"] [
            G.div [A._class "control"] children
        ]
        
    let private inputControl typeAttr options : G.XmlNode =
        let defaults = ControlOption.Defaults options
        let inputAttrs =
            [ yield A._class "input"
              yield A._type typeAttr
              yield A._placeholder defaults.Placeholder
              yield A._name defaults.HtmlName
              yield A._value defaults.Value
              
              if defaults.Required then
                  yield A._required
                  
              match defaults.Max with
              | Some x ->
                  yield A._max (string x)
              | None ->
                  ()
                  
              match defaults.Min with
              | Some x ->
                  yield A._min (string x)
              | None ->
                  ()
                  
              match defaults.Step with
              | Some x ->
                  yield A._step (string x)
              | None ->
                  () ]
        
        control [
            label defaults.HtmlName defaults.Label
            G.input inputAttrs
            defaults.HelpText
            |> Option.map (fun text -> G.p [A._class "help"] [G.str text])
            |> S.maybeEl 
        ]
        
    let private textInput = inputControl "text"
    
    let private passwordInput = inputControl "password"
    
    let private dateInput = inputControl "date"
    
    let private numberInput = inputControl "number"
    
    let private selectBox options : G.XmlNode =
        let defaults = SelectOption.Defaults options
        let props = [
            yield A._name defaults.HtmlName
            yield A._value defaults.Value
            
            if defaults.Required then
                yield A._required
        ]
        let options =
            defaults.Options
            |> List.map (fun o ->
                G.option
                  [ yield A._value o.Value
                    if o.Selected then
                        yield A._selected ]
                  [G.str o.Label]
            )
        
        control [
            label defaults.HtmlName defaults.LabelText
            // Select containers must specifically be set to fullwidth, as they do not naturally expand like
            // text inputs do. 
            G.div [A._class "select is-fullwidth"] [
                G.select props options
            ]
        ]
        
    let private checkboxInput options : G.XmlNode =
        let defaults = CheckboxOption.Defaults options
        let inputProps =
            let baseProps = [A._type "checkbox"; A._name defaults.HtmlName]
            match defaults.Checked with
            | true ->
                baseProps@[A._checked]
            | false ->
                baseProps
                
        control [
            G.label [A._class "checkbox"] [
                G.input inputProps
                G.str defaults.Label
            ]
        ]
        
    let private button options : G.XmlNode =
        let defaults =
            ButtonOption.Defaults options
        let buttonFn =
            match defaults.Type with
            | ButtonType.Link _ -> G.a
            | _ -> G.button
        let buttonAttrs =
            let addClassName attrs =
                let list = ["button"; defaults.Color.BulmaCssClass; defaults.Shade.BulmaCssClass]
                let className = System.String.Join(" ", list)
                attrs @ [A._class className ]
                
            let addExtraAttrs attrs =
                match defaults.Type with
                | ButtonType.Link href ->
                    attrs @ [A._href href]
                | _ ->
                    let extraAttrs = 
                        let baseAttrs = [A._type defaults.Type.HtmlTypeAttr]
                    
                        defaults.FormAction
                        |> Option.map (fun action -> baseAttrs @ [A._formaction action])
                        |> Option.defaultValue baseAttrs
                    attrs @ extraAttrs
                    
            let maybeAddOnClick attrs =
                defaults.OnClick
                |> Option.map (fun x -> attrs @ [A._onclick x])
                |> Option.defaultValue attrs
                
            [ A._name defaults.HtmlName ]
            |> addClassName
            |> addExtraAttrs
            |> maybeAddOnClick
                
        // Wrap the button in a div so we can align the text
        G.div [A._class defaults.Alignment.BulmaCssClass] [
            buttonFn buttonAttrs [
                G.str defaults.Label
            ]
        ]
        |> List.singleton
        |> control 
        
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
                | AutoComplete value:: rest ->
                    let str = if value then "on" else "off"
                    mapNextProp (current@[A._autocomplete str]) rest
            mapNextProp [] options
        let children =
            let rec mapNextChild rest (current : G.XmlNode list) =
                let next rest newNodes =
                    current @ newNodes
                    |> mapNextChild rest
                
                match rest with
                | [] ->
                    current
                | TextInput options :: rest ->
                    [textInput options]
                    |> next rest 
                | PasswordInput options :: rest ->
                    [passwordInput options]
                    |> next rest
                | DateInput options :: rest ->
                    [dateInput options]
                    |> next rest
                | NumberInput options :: rest ->
                    [numberInput options]
                    |> next rest
                | SelectBox options :: rest ->
                    [selectBox options]
                    |> next rest
                | CheckboxInput options :: rest ->
                    [checkboxInput options]
                    |> next rest
                | Button options :: rest ->
                    [button options]
                    |> next rest
                | MaybeError err :: rest ->
                    [ err
                      |> Option.map (fun err -> control [ S.error err ])
                      |> S.maybeEl ]
                    |> next rest
                | MaybeElement (Some el) :: rest ->
                    mapNextChild [el] []
                    |> next rest
                | MaybeElement None :: rest ->
                    []
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
                | EmptyDiv :: rest ->
                    [G.div [] []]
                    |> next rest 
                | Group els :: rest ->
                    // Recursively create another group of elements, but all of these will be wrapped in a Bulma r
                    // responsive column
                    let columns =
                        mapNextChild els []
                        |> List.map (fun el -> G.div [A._class "column"] [el])
                    
                    [G.div [A._class "columns"] columns]
                    |> next rest
                    
            mapNextChild children []
            
        G.form props children 
