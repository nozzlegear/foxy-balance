namespace FoxyBalance.Database.Interfaces

open System
open System.Threading.Tasks
open FoxyBalance.Database.Models

type ApiKeyId = int64

type ApiKeyInfo =
    { Id: ApiKeyId
      UserId: UserId
      KeyValue: string
      Name: string
      DateCreated: DateTimeOffset
      LastUsed: DateTimeOffset option
      Active: bool }

type IApiKeyDatabase =
    /// Creates a new API key and returns its ID
    abstract member CreateAsync: userId: UserId * keyValue: string * secretHash: string * name: string -> Task<ApiKeyId>

    /// Gets API key info by key value (for authentication)
    abstract member GetByKeyValueAsync: keyValue: string -> Task<(UserId * ApiKeyId * string) option>

    /// Lists all API keys for a user (without secrets)
    abstract member ListForUserAsync: userId: UserId -> Task<ApiKeyInfo seq>

    /// Updates the last used timestamp for an API key
    abstract member UpdateLastUsedAsync: keyId: ApiKeyId -> Task

    /// Revokes an API key (sets active = false)
    abstract member RevokeAsync: userId: UserId * keyId: ApiKeyId -> Task

    /// Permanently deletes an API key
    abstract member DeleteAsync: userId: UserId * keyId: ApiKeyId -> Task
