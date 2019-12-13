namespace FoxyBalance.Database.Interfaces

open System.Threading.Tasks
open FoxyBalance.Database.Models

type IUserDatabase =
    abstract member CreateAsync : PartialUser -> Task<User>
    abstract member ExistsAsync : UserIdentifier -> Task<bool>
    abstract member GetAsync : UserIdentifier -> Task<User>

