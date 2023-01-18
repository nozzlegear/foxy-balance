namespace FoxyBalance.Database.Interfaces

open System.Threading.Tasks
open FoxyBalance.Database.Models

type IIncomeDatabase =
    abstract member ImportAsync : UserId -> PartialIncomeRecord seq -> Task<IncomeImportSummary>
    abstract member ListAsync : UserId -> taxYear : int -> Task<IncomeRecord seq>
    abstract member SummarizeAsync : UserId -> taxYear : int -> Task<IncomeSummary option>
    abstract member IgnoreAsync : UserId -> IncomeId -> Task
    abstract member UnignoreAsync : UserId -> IncomeId -> Task
    abstract member DeleteAsync : UserId -> IncomeId -> Task
    abstract member ListTaxYearsAsync : UserId -> Task<TaxYear seq>
    abstract member GetAsync : UserId -> IncomeId -> Task<IncomeRecord option>