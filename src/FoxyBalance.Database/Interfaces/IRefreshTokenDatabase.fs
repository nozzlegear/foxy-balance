namespace FoxyBalance.Database.Interfaces

open System
open System.Threading.Tasks
open FoxyBalance.Database.Models

type RefreshTokenId = int64

type RefreshTokenInfo =
    { Id: RefreshTokenId
      UserId: UserId
      ExpiresAt: DateTimeOffset
      Used: bool
      DateCreated: DateTimeOffset }

type IRefreshTokenDatabase =
    /// Creates a new refresh token and returns its ID
    abstract member CreateAsync: userId: UserId * tokenHash: string * expiresAt: DateTimeOffset -> Task<RefreshTokenId>

    /// Gets refresh token info by token hash
    abstract member GetByHashAsync: tokenHash: string -> Task<RefreshTokenInfo option>

    /// Marks a refresh token as used (single-use tokens)
    abstract member MarkAsUsedAsync: tokenId: RefreshTokenId -> Task

    /// Atomically consumes a refresh token - marks it as used and returns token info only if successful.
    /// Returns None if token doesn't exist, is already used, or is expired.
    abstract member ConsumeRefreshTokenAsync: tokenHash: string -> Task<RefreshTokenInfo option>

    /// Deletes all expired and used refresh tokens (cleanup)
    abstract member DeleteExpiredAsync: unit -> Task<int>

    /// Deletes all refresh tokens for a user (logout all sessions)
    abstract member DeleteAllForUserAsync: userId: UserId -> Task
