namespace FoxyBalance.Database.Interfaces

open FoxyBalance.Database.Models
open System.Threading.Tasks

type ITransactionDatabase =
    abstract member GetStatusAsync : userId: UserId * transactionId: TransactionId -> Task<TransactionStatus>
    abstract member GetAsync : userId: UserId * transactionId: TransactionId -> Task<Transaction option>
    abstract member ExistsAsync : userId: UserId * transactionId: TransactionId -> Task<bool>
    abstract member BulkCreateAsync : userId: UserId * partialTransactions: PartialTransaction list -> Task<BulkCreationCount>
    abstract member CreateAsync : userId: UserId * partialTransaction: PartialTransaction -> Task<Transaction>
    abstract member UpdateAsync : userId: UserId * transactionId: TransactionId * partialTransaction: PartialTransaction -> Task<Transaction>
    abstract member ListAsync : userId: UserId * listOptions: ListOptions -> Task<Transaction seq>
    abstract member DeleteAsync : userId: UserId * transactionId: TransactionId -> Task
    abstract member CountAsync : userId: UserId * statusFilter: StatusFilter -> Task<int>
    abstract member SumAsync : userId: UserId -> Task<TransactionSum>
