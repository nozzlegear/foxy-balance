CREATE VIEW FoxyBalance_IncomeRecordsView
AS
SELECT
    FBIR.Id,
    FBIR.UserId,
    FBIR.TaxYearId,
    FBTY.TaxYear,
    FBIR.SaleDate,
    FBIR.SourceType,
    FBIR.SourceTransactionId,
    FBIR.SourceTransactionDescription,
    FBIR.SourceTransactionCustomerDescription,
    FBIR.SaleAmount,
    FBIR.PlatformFee,
    FBIR.ProcessingFee,
    FBIR.NetShare,
    FBIR.NetShare * FBTY.TaxRate / 100 AS EstimatedTax,
    FBIR.Ignored
FROM FoxyBalance_IncomeRecords AS FBIR
INNER JOIN FoxyBalance_TaxYears FBTY on FBIR.TaxYearId = FBTY.Id