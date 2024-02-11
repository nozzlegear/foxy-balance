namespace FoxyBalance.Server

open System.Security.Cryptography
open System.Text
open System

module Hashes =
    type UnhashedString = | Unhashed of string
    type HashedString = | Hashed of string
    
    /// Hashes the value with HMAC SHA256.
    let CreateHmacSha256Hash (salt : string) (value : UnhashedString) : HashedString =
        use hmac = new HMACSHA256(Encoding.UTF8.GetBytes salt)
        match value with
        | Unhashed value ->
            Encoding.UTF8.GetBytes value
            |> hmac.ComputeHash
            |> BitConverter.ToString
            |> Hashed

    /// Checks whether an unhashed string and hashed string are matches using HMAC SHA256.
    let VerifyHmacSha256Hash (salt : string) (hashed : HashedString) (unhashed : UnhashedString) : bool =
        hashed = CreateHmacSha256Hash salt unhashed

    /// Creates a random salt with 32 bytes of random hex characters using the RandomNumberGenerator.
    let CreateSalt () =
        let byteCount = 32
        RandomNumberGenerator.GetHexString(byteCount, lowercase = true)
