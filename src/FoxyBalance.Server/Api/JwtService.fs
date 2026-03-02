namespace FoxyBalance.Server.Api

open System
open System.ComponentModel.DataAnnotations
open System.IdentityModel.Tokens.Jwt
open System.Security.Claims
open System.Security.Cryptography
open Microsoft.IdentityModel.Tokens
open FoxyBalance.Database.Models

/// Configuration for JWT token generation
type JwtConfig =
    { [<Required>]
      SecretKey: string
      [<Required>]
      Issuer: string
      [<Required>]
      Audience: string
      [<Required>]
      AccessTokenLifetime: TimeSpan
      [<Required>]
      RefreshTokenLifetime: TimeSpan }

/// Service for JWT token operations
type JwtService(config: JwtConfig) =
    let signingKey = SymmetricSecurityKey(Text.Encoding.UTF8.GetBytes(config.SecretKey))

    let signingCredentials =
        SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)

    let tokenHandler = JwtSecurityTokenHandler()

    /// Generate an access token for a user
    member _.GenerateAccessToken(userId: UserId) : string =
        let claims =
            [| Claim(JwtRegisteredClaimNames.Sub, string userId)
               Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
               Claim("userId", string userId) |]

        let tokenDescriptor =
            SecurityTokenDescriptor(
                Subject = ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.Add(config.AccessTokenLifetime),
                Issuer = config.Issuer,
                Audience = config.Audience,
                SigningCredentials = signingCredentials
            )

        let token = tokenHandler.CreateToken(tokenDescriptor)
        tokenHandler.WriteToken(token)

    /// Generate a cryptographically random refresh token
    member _.GenerateRefreshToken() : string =
        let randomBytes = Array.zeroCreate<byte> 32
        use rng = RandomNumberGenerator.Create()
        rng.GetBytes(randomBytes)
        Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')

    /// Hash a token for storage
    member _.HashToken(token: string) : string =
        use sha256 = SHA256.Create()
        let hashBytes = sha256.ComputeHash(Text.Encoding.UTF8.GetBytes(token))
        Convert.ToBase64String(hashBytes)

    /// Validate an access token and extract the user ID
    member _.ValidateAccessToken(token: string) : UserId option =
        let validationParameters =
            TokenValidationParameters(
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,
                ValidateAudience = true,
                ValidAudience = config.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            )

        try
            let mutable validatedToken: SecurityToken = null

            let principal =
                tokenHandler.ValidateToken(token, validationParameters, &validatedToken)

            let userIdClaim = principal.FindFirst("userId")

            if isNull userIdClaim then
                None
            else
                Some(int userIdClaim.Value)
        with _ ->
            None

    /// Get the refresh token expiration time
    member _.GetRefreshTokenExpiry() : DateTimeOffset =
        DateTimeOffset.UtcNow.Add(config.RefreshTokenLifetime)

    /// Get the access token lifetime in seconds
    member _.GetAccessTokenLifetimeSeconds() : int =
        int config.AccessTokenLifetime.TotalSeconds
