namespace FoxyBalance.Database.Interfaces

open FoxyBalance.Database.Models
open System.Threading.Tasks

type ITransactionDatabase =
    abstract member GetStatusAsync : UserId -> TransactionId -> Task<TransactionStatus>
    abstract member GetAsync : UserId -> TransactionId -> Task<Transaction>
    abstract member ExistsAsync : UserId -> TransactionId -> Task<bool>
    abstract member CreateAsync : UserId -> PartialTransaction -> Task<Transaction>
    abstract member UpdateAsync : UserId -> TransactionId -> PartialTransaction -> Task<Transaction>
    abstract member ListAsync : UserId -> ListOptions -> Task<Transaction seq>
    abstract member DeleteAsync : UserId -> TransactionId -> Task
    abstract member CountAsync : UserId -> StatusFilter -> Task<int>
    abstract member SumAsync : UserId -> Task<TransactionSum> 
