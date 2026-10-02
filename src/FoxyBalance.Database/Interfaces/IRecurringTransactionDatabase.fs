namespace FoxyBalance.Database.Interfaces

open System
open FoxyBalance.Database.Models
open System.Threading.Tasks

type IRecurringTransactionDatabase =
    abstract member GetAsync : userId: UserId * recurringTransactionId: RecurringTransactionId -> Task<RecurringTransaction option>
    abstract member ListAsync : userId: UserId * activeOnly: bool -> Task<RecurringTransaction seq>
    abstract member CreateAsync : userId: UserId * recurringTransaction: PartialRecurringTransaction -> Task<RecurringTransaction>
    abstract member UpdateAsync : userId: UserId * recurringTransactionId: RecurringTransactionId * recurringTransaction: PartialRecurringTransaction -> Task<RecurringTransaction>
    abstract member UpdateLastAppliedDateAsync : userId: UserId * recurringTransactionId: RecurringTransactionId * appliedDate: DateTimeOffset -> Task<unit>
    abstract member SetActiveAsync : userId: UserId * recurringTransactionId: RecurringTransactionId * active: bool -> Task<unit>
    abstract member DeleteAsync : userId: UserId * recurringTransactionId: RecurringTransactionId -> Task<unit>
    abstract member GetRecurringTransactionsDueForApplicationAsync : currentDate: DateTimeOffset -> Task<(UserId * RecurringTransaction) seq>