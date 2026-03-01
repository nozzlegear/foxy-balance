namespace FoxyBalance.Database.Interfaces

open System
open FoxyBalance.Database.Models
open System.Threading.Tasks

type IRecurringBillDatabase =
    abstract member GetAsync : userId: UserId * billId: RecurringBillId -> Task<RecurringBill option>
    abstract member ListAsync : userId: UserId * activeOnly: bool -> Task<RecurringBill seq>
    abstract member CreateAsync : userId: UserId * bill: PartialRecurringBill -> Task<RecurringBill>
    abstract member UpdateAsync : userId: UserId * billId: RecurringBillId * bill: PartialRecurringBill -> Task<RecurringBill>
    abstract member UpdateLastAppliedDateAsync : userId: UserId * billId: RecurringBillId * appliedDate: DateTimeOffset -> Task<unit>
    abstract member SetActiveAsync : userId: UserId * billId: RecurringBillId * active: bool -> Task<unit>
    abstract member DeleteAsync : userId: UserId * billId: RecurringBillId -> Task<unit>
    abstract member GetBillsDueForApplicationAsync : currentDate: DateTimeOffset -> Task<(UserId * RecurringBill) seq>
