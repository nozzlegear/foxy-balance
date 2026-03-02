namespace FoxyBalance.Server.Api

open System
open System.Security.Cryptography
open FoxyBalance.Database.Models

/// Service for API key operations
type ApiKeyService(apiKeyDb: FoxyBalance.Database.Interfaces.IApiKeyDatabase, hashingKey: string) =

    let generateRandomKey (prefix: string) =
        let randomBytes = Array.zeroCreate<byte> 24
        use rng = RandomNumberGenerator.Create()
        rng.GetBytes(randomBytes)

        let base64 =
            Convert.ToBase64String(randomBytes).Replace("+", "").Replace("/", "").Replace("=", "")

        $"{prefix}{base64[..31]}"

    let hashSecret (secret: string) =
        use hmac = new HMACSHA256(Text.Encoding.UTF8.GetBytes(hashingKey))
        let hashBytes = hmac.ComputeHash(Text.Encoding.UTF8.GetBytes(secret))
        BitConverter.ToString(hashBytes).Replace("-", "")

    /// Create a new API key pair for a user
    member _.CreateApiKeyPair(userId: UserId, name: string) =
        task {
            let apiKey = generateRandomKey "fbk_"
            let apiSecret = generateRandomKey "fbs_"
            let secretHash = hashSecret apiSecret

            let! keyId = apiKeyDb.CreateAsync(userId, apiKey, secretHash, name)

            return (keyId, apiKey, apiSecret)
        }

    /// Validate an API key and secret, returning the user ID if valid
    member _.ValidateApiKey(apiKey: string, apiSecret: string) =
        task {
            match! apiKeyDb.GetByKeyValueAsync(apiKey) with
            | None -> return None
            | Some(userId, keyId, storedSecretHash) ->
                let providedSecretHash = hashSecret apiSecret

                // Use constant-time comparison to prevent timing attacks
                let storedBytes = Text.Encoding.UTF8.GetBytes(storedSecretHash)
                let providedBytes = Text.Encoding.UTF8.GetBytes(providedSecretHash)

                if CryptographicOperations.FixedTimeEquals(ReadOnlySpan(storedBytes), ReadOnlySpan(providedBytes)) then
                    do! apiKeyDb.UpdateLastUsedAsync(keyId)
                    return Some(userId, keyId)
                else
                    return None
        }

    /// List all API keys for a user
    member _.ListKeysForUser(userId: UserId) = apiKeyDb.ListForUserAsync(userId)

    /// Revoke an API key
    member _.RevokeKey(userId: UserId, keyId: int64) = apiKeyDb.RevokeAsync(userId, keyId)

    /// Delete an API key
    member _.DeleteKey(userId: UserId, keyId: int64) = apiKeyDb.DeleteAsync(userId, keyId)
