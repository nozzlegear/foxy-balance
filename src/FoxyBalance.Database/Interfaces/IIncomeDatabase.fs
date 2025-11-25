namespace FoxyBalance.Database.Interfaces

open System.Threading.Tasks
open FoxyBalance.Database.Models

type IIncomeDatabase =
    abstract member ImportAsync : userId: UserId * records: PartialIncomeRecord seq -> Task<IncomeImportSummary>
    abstract member ListAsync : userId: UserId * taxYear: int * options: ListIncomeOptions -> Task<IncomeRecord seq>
    abstract member SummarizeAsync : userId: UserId * taxYear : int -> Task<IncomeSummary option>
    abstract member SetIgnoreAsync : userId: UserId * IncomeId * shouldIgnore: bool -> Task
    abstract member DeleteAsync : userId: UserId * IncomeId -> Task
    abstract member ListTaxYearsAsync : userId: UserId -> Task<TaxYear seq>
    abstract member GetTaxYearAsync : userId: UserId * taxYear : int -> Task<TaxYear option>
    abstract member SetTaxYearRateAsync : userId: UserId * taxYear: int * rate: int -> Task
    abstract member GetAsync : userId: UserId * incomeRecordId: IncomeId -> Task<IncomeRecord option>
