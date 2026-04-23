namespace FoxyBalance.Server.Api.Domain

open System
open FoxyBalance.Database.Models

/// HAL link representation
type HalLink =
    { Href: string
      Method: string option
      Templated: bool option }

/// Type-safe link relations for HATEOAS
type LinkRel =
    | Self
    | Collection
    | Item
    | Next
    | Prev
    | First
    | Last
    | Create
    | Update
    | Delete
    | Balance
    | Transactions
    | Transaction of TransactionId
    | Bills
    | Bill of RecurringBillId
    | MatchSuggestions
    | ExecuteMatch
    | ToggleActive
    | Import
    | TokenRefresh
    | ApiKeys

    member this.AsString() =
        match this with
        | Self -> "self"
        | Collection -> "collection"
        | Item -> "item"
        | Next -> "next"
        | Prev -> "prev"
        | First -> "first"
        | Last -> "last"
        | Create -> "create"
        | Update -> "update"
        | Delete -> "delete"
        | Balance -> "balance"
        | Transactions -> "transactions"
        | Transaction _ -> "transaction"
        | Bills -> "bills"
        | Bill _ -> "bill"
        | MatchSuggestions -> "match-suggestions"
        | ExecuteMatch -> "execute-match"
        | ToggleActive -> "toggle-active"
        | Import -> "import"
        | TokenRefresh -> "token-refresh"
        | ApiKeys -> "api-keys"

/// HAL links as a map from relation to link
type HalLinks = Map<string, HalLink>

/// HAL embedded resources
type HalEmbedded = Map<string, obj>

/// HAL resource wrapper with strong typing
type HalResource<'T> =
    { Data: 'T
      Links: HalLinks
      Embedded: HalEmbedded option }

/// Paginated HAL collection
type HalCollection<'T> =
    { Items: 'T list
      Page: int
      TotalPages: int
      TotalCount: int
      Links: HalLinks }

/// API Session representing an authenticated API request
type ApiSession =
    { UserId: UserId
      ApiKeyId: int64 option }
