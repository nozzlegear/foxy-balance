namespace FoxyBalance.Server.Views

open FoxyBalance.Database.Interfaces
open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels
open Giraffe.ViewEngine
module G = HtmlElements
module A = Attributes

module ApiKeys =
    let private formatLastUsed (lastUsed: System.DateTimeOffset option) =
        match lastUsed with
        | Some date -> Format.date date
        | None -> "Never"

    let private formatStatus (active: bool) =
        if active then "Active" else "Revoked"

    let listApiKeysPage (model: ApiKeysListViewModel) : XmlNode =
        let title = "API Keys"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                    Shared.LevelItem.Element (G.a [A._href "/api-keys/new"; A._class "button is-success"] [G.str "Create API Key"])
                ]
            ]

            G.div [A._class "content"] [
                G.p [] [
                    G.str "API keys allow you to access your balance data via the REST API. "
                    G.str "Each key has a secret that is only shown once when created."
                ]
            ]

            if Seq.isEmpty model.ApiKeys then
                G.p [A._class "has-text-centered has-text-grey"] [G.str "No API keys found. Create one to get started!"]
            else
                Shared.table [
                    Shared.TableHead [
                        Shared.TableCell (G.str "Name")
                        Shared.TableCell (G.str "Key")
                        Shared.TableCell (G.str "Created")
                        Shared.TableCell (G.str "Last Used")
                        Shared.TableCell (G.str "Status")
                        Shared.TableCell (G.str "Actions")
                    ]
                    Shared.TableBody [
                        for key in model.ApiKeys do
                            yield Shared.TableRow [
                                Shared.TableCell (G.str key.Name)
                                Shared.TableCell (
                                    G.code [A._class "has-text-grey"] [G.str (key.KeyValue.Substring(0, 12) + "...")]
                                )
                                Shared.TableCell (Format.date key.DateCreated |> G.str)
                                Shared.TableCell (formatLastUsed key.LastUsed |> G.str)
                                Shared.TableCell (
                                    let statusClass = if key.Active then "has-text-success" else "has-text-danger"
                                    G.span [A._class statusClass] [G.str (formatStatus key.Active)]
                                )
                                Shared.TableCell (
                                    G.div [A._class "buttons are-small"] [
                                        if key.Active then
                                            G.form [A._method "post"; A._action (sprintf "/api-keys/%d/revoke" key.Id); A._style "display: inline;"] [
                                                G.button [A._type "submit"; A._class "button is-warning is-small"] [G.str "Revoke"]
                                            ]
                                        G.form [A._method "post"; A._action (sprintf "/api-keys/%d/delete" key.Id); A._style "display: inline;"] [
                                            G.button [A._type "submit"; A._class "button is-danger is-small"; A._onclick "return confirm('Are you sure you want to permanently delete this API key? This action cannot be undone.')"] [G.str "Delete"]
                                        ]
                                    ]
                                )
                            ]
                    ]
                ]
        ]

    let newApiKeyPage (model: NewApiKeyViewModel) : XmlNode =
        let title = "Create API Key"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/api-keys"; A._class "button"] [G.str "Cancel"]
                     |> Shared.LevelItem.Element
                ]
            ]

            G.div [A._class "content"] [
                G.p [] [
                    G.str "Create a new API key to access your balance data via the REST API. "
                    G.str "The secret will only be shown once after creation, so make sure to copy it somewhere safe."
                ]
            ]

            Form.create [Form.Method Form.Post; Form.Action "/api-keys/new"; Form.AutoComplete false] [
                Form.Element.TextInput [
                    Form.Placeholder "My App Integration"
                    Form.LabelText "Key Name"
                    Form.HtmlName "name"
                    Form.Required
                    Form.Value model.Name ]

                Form.Element.MaybeError model.Error

                Form.Element.Button [
                    Form.ButtonText "Create API Key"
                    Form.Alignment Form.ButtonAlignment.Right
                    Form.Color Form.ButtonColor.Success
                    Form.Type Form.Submit ]
            ]
        ]

    let apiKeyCreatedPage (model: ApiKeyCreatedViewModel) : XmlNode =
        let title = "API Key Created"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
            ]

            G.article [A._class "message is-success"] [
                G.div [A._class "message-header"] [
                    G.p [] [G.str "API Key Created Successfully"]
                ]
                G.div [A._class "message-body"] [
                    G.p [] [
                        G.strong [] [G.str "Important: "]
                        G.str "Copy the API secret below now. You will not be able to see it again!"
                    ]
                ]
            ]

            G.div [A._class "box"] [
                G.div [A._class "field"] [
                    G.label [A._class "label"] [G.str "Name"]
                    G.div [A._class "control"] [
                        G.input [A._class "input"; A._type "text"; A._value model.Name; A._readonly]
                    ]
                ]

                G.div [A._class "field"] [
                    G.label [A._class "label"] [G.str "API Key"]
                    G.div [A._class "control"] [
                        G.input [A._class "input is-family-monospace"; A._type "text"; A._value model.ApiKey; A._readonly]
                    ]
                    G.p [A._class "help"] [G.str "Use this key to identify your application."]
                ]

                G.div [A._class "field"] [
                    G.label [A._class "label"] [G.str "API Secret"]
                    G.div [A._class "control"] [
                        G.input [A._class "input is-family-monospace"; A._type "text"; A._value model.ApiSecret; A._readonly]
                    ]
                    G.p [A._class "help has-text-danger"] [
                        G.strong [] [G.str "Copy this now! "]
                        G.str "This secret will not be shown again."
                    ]
                ]
            ]

            G.div [A._class "content"] [
                G.h4 [] [G.str "How to use your API key"]
                G.p [] [G.str "To authenticate with the API, first exchange your API key and secret for access tokens:"]
                G.pre [] [
                    G.code [] [
                        G.str (sprintf """curl -X POST %s/api/v1/auth/token \
  -H "Content-Type: application/json" \
  -d '{"apiKey": "%s", "apiSecret": "YOUR_SECRET"}'""" model.BaseUrl model.ApiKey)
                    ]
                ]
                G.p [] [G.str "Then use the returned access token for subsequent requests:"]
                G.pre [] [
                    G.code [] [
                        G.str (sprintf """curl %s/api/v1/balance \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN" """ model.BaseUrl)
                    ]
                ]
            ]

            G.div [A._class "has-text-centered"] [
                G.a [A._href "/api-keys"; A._class "button is-primary"] [G.str "Back to API Keys"]
            ]
        ]
