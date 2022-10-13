CREATE VIEW FoxyBalance_TaxYearSummaryView
AS
SELECT
    V.UserId,
    V.TaxYearId,
    FBTY.TaxYear,
    FBTY.TaxRate,
    SUM(V.SaleAmount) AS TotalSales,
    SUM(V.PlatformFee + V.ProcessingFee) AS TotalFees,
    SUM(V.NetShare) AS TotalNetShare,
    SUM(V.EstimatedTax) AS TotalEstimatedTax
FROM FoxyBalance_IncomeRecordsView AS V
INNER JOIN FoxyBalance_TaxYears FBTY on V.TaxYearId = FBTY.Id
WHERE V.Ignored = 0
GROUP BY V.UserId, V.TaxYearId, FBTY.TaxYear, FBTY.TaxRate