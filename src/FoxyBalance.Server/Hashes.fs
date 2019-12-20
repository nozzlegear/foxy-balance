namespace FoxyBalance.Server

module Hashes =
    type UnhashedString = | Unhashed of string
    type HashedString = | Hashed of string
    
    /// Hashes the value with HMAC SHA256.
    let CreateHmacSha256Hash (secretKey : string) (value : UnhashedString) : HashedString =
        use hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secretKey))
        let computedBytes =
            match value with
            | Unhashed value -> 
                System.Text.Encoding.UTF8.GetBytes value 
                |> hmac.ComputeHash
            
        System.BitConverter.ToString(computedBytes)
        |> Hashed

    /// Checks whether an unhashed string and hashed string are matches using HMAC SHA256.
    let VerifyHmacSha256Hash (secretKey : string) (hashed : HashedString) (unhashed : UnhashedString) : bool = 
        hashed = CreateHmacSha256Hash secretKey unhashed

