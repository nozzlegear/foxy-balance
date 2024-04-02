namespace FoxyBalance.Database.Interfaces

open System.Threading.Tasks
open FoxyBalance.Database.Models

type IIncomeDatabase =
    abstract member ImportAsync : UserId -> PartialIncomeRecord seq -> Task<IncomeImportSummary>
    abstract member ListAsync : userId: UserId -> taxYear: int -> options: ListIncomeOptions-> Task<IncomeRecord seq>
    abstract member SummarizeAsync : UserId -> taxYear : int -> Task<IncomeSummary option>
    abstract member SetIgnoreAsync : UserId -> IncomeId -> shouldIgnore: bool -> Task
    abstract member DeleteAsync : UserId -> IncomeId -> Task
    abstract member ListTaxYearsAsync : UserId -> Task<TaxYear seq>
    abstract member GetTaxYearAsync : UserId -> taxYear : int -> Task<TaxYear option>
    abstract member SetTaxYearRateAsync : UserId -> taxYear : int -> rate : int -> Task
    abstract member GetAsync : UserId -> IncomeId -> Task<IncomeRecord option>
