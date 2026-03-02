namespace FoxyBalance.Server.Api

module HalBuilder =
    /// Create a simple link
    let link href : HalLink =
        { Href = href
          Method = None
          Templated = None }

    /// Create a link with HTTP method
    let linkWithMethod method href : HalLink =
        { Href = href
          Method = Some method
          Templated = None }

    /// Create a templated link
    let templatedLink href : HalLink =
        { Href = href
          Method = None
          Templated = Some true }

    /// Create a HAL resource with data and links
    let resource data (links: (LinkRel * HalLink) list) : HalResource<'T> =
        { Data = data
          Links = links |> List.map (fun (rel, link) -> (rel.AsString(), link)) |> Map.ofList
          Embedded = None }

    /// Create a HAL resource with embedded resources
    let resourceWithEmbedded data (links: (LinkRel * HalLink) list) (embedded: (string * obj) list) : HalResource<'T> =
        { Data = data
          Links = links |> List.map (fun (rel, link) -> (rel.AsString(), link)) |> Map.ofList
          Embedded = Some(Map.ofList embedded) }

    /// Create pagination links for a collection
    let paginationLinks
        (baseUrl: string)
        (page: int)
        (totalPages: int)
        (extraQuery: string)
        : (LinkRel * HalLink) list =
        let url p = $"{baseUrl}?page={p}{extraQuery}"

        [ yield Self, link (url page)
          if totalPages > 0 then
              yield First, link (url 1)
              yield Last, link (url totalPages)
          if page > 1 then
              yield Prev, link (url (page - 1))
          if page < totalPages then
              yield Next, link (url (page + 1)) ]

    /// Create a HAL collection response
    let collection
        (items: 'T list)
        (page: int)
        (totalPages: int)
        (totalCount: int)
        (links: (LinkRel * HalLink) list)
        : HalCollection<'T> =
        { Items = items
          Page = page
          TotalPages = totalPages
          TotalCount = totalCount
          Links = links |> List.map (fun (rel, link) -> (rel.AsString(), link)) |> Map.ofList }

    /// Add links for a transaction resource
    let transactionLinks (transactionId: int64) : (LinkRel * HalLink) list =
        [ Self, link $"/api/v1/transactions/{transactionId}"
          Update, linkWithMethod "PUT" $"/api/v1/transactions/{transactionId}"
          Delete, linkWithMethod "DELETE" $"/api/v1/transactions/{transactionId}"
          ExecuteMatch, linkWithMethod "POST" "/api/v1/bills/match" ]

    /// Add links for a recurring bill resource
    let billLinks (billId: int64) : (LinkRel * HalLink) list =
        [ Self, link $"/api/v1/bills/{billId}"
          Update, linkWithMethod "PUT" $"/api/v1/bills/{billId}"
          Delete, linkWithMethod "DELETE" $"/api/v1/bills/{billId}"
          ToggleActive, linkWithMethod "POST" $"/api/v1/bills/{billId}/toggle-active" ]

    /// Add links for balance resource
    let balanceLinks () : (LinkRel * HalLink) list =
        [ Self, link "/api/v1/balance"
          Transactions, link "/api/v1/transactions"
          Bills, link "/api/v1/bills" ]
