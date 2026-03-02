namespace FoxyBalance.Server.Api.Routes

open Giraffe
open FoxyBalance.Server.Api

module ApiRouteUtils =
    /// Return a JSON response with HAL+JSON content type
    let halJson (resource: 'T) : HttpHandler =
        setHttpHeader "Content-Type" "application/hal+json" >=> json resource

    /// Return an error response
    let apiError (statusCode: int) (message: string) : HttpHandler =
        setStatusCode statusCode >=> json { Error = message; Details = None }

    /// Return an error response with details
    let apiErrorWithDetails (statusCode: int) (message: string) (details: string list) : HttpHandler =
        setStatusCode statusCode
        >=> json
                { Error = message
                  Details = Some details }

    /// Return a 404 Not Found response
    let notFound (resourceType: string) : HttpHandler =
        apiError 404 $"{resourceType} not found"

    /// Return a 422 Unprocessable Entity response for validation errors
    let validationError (message: string) : HttpHandler = apiError 422 message

    /// Return a 201 Created response with HAL resource
    let created (resource: 'T) : HttpHandler = setStatusCode 201 >=> halJson resource

    /// Return a 204 No Content response
    let noContent: HttpHandler = setStatusCode 204 >=> text ""
