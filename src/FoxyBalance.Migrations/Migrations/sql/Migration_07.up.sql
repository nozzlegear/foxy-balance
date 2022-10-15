CREATE TYPE dbo.tp_PartialIncomeRecord AS TABLE
(
    [SaleDate] DATETIMEOFFSET NOT NULL,
    [SourceType] NVARCHAR(18) NOT NULL,
    [SourceTransactionId] NVARCHAR(510) NULL,
    [SourceTransactionDescription] NVARCHAR(1000) NULL,
    [SaleAmount] INT NOT NULL,
    [PlatformFee] INT NOT NULL,
    [ProcessingFee] INT NOT NULL,
    [NetShare] INT NOT NULL
)

GO;

CREATE PROC [dbo].[sp_BatchImportIncomeRecords]
(
    @userId INT,
    @partialIncomeRecords tp_PartialIncomeRecord readonly
)
AS
BEGIN
    if (@userId is null)
        RAISERROR('@userId cannot be null', 18, 0)
    
    declare @defaultTaxRate INT = 33;
    -- Create a table to hold a list of tax years that don't exist for this user
    declare @taxYears TABLE (Year INT);
    
    -- Find unique years that don't yet exist for this user
    INSERT INTO @taxYears(Year)
    SELECT DISTINCT YEAR(P.SaleDate)
    FROM @partialIncomeRecords AS P
    WHERE NOT EXISTS (
        SELECT 1 
        FROM [FoxyBalance_TaxYears] AS T 
        WHERE T.UserId = @userId 
          AND YEAR(P.SaleDate) = T.TaxYear
    );
    
    -- Create new tax years for those that don't exist
    INSERT INTO [FoxyBalance_TaxYears] (UserId, TaxYear, TaxRate) 
    SELECT @userId, [Year], @defaultTaxRate FROM @taxYears
    
    -- Create a table to hold all of the newly inserted income record ids
    declare @recordIds TABLE (Id BIGINT);
    
    BEGIN TRANSACTION
        INSERT INTO [FoxyBalance_IncomeRecords] (
            UserId, 
            TaxYearId, 
            SaleDate,
            SourceType, 
            SourceTransactionId, 
            SourceTransactionDescription, 
            SaleAmount, 
            PlatformFee, 
            ProcessingFee, 
            NetShare, 
            Ignored
        ) 
        OUTPUT 
            INSERTED.Id INTO @recordIds
        SELECT 
            @userId,
            (SELECT TOP 1 [Id] FROM [FoxyBalance_TaxYears] WHERE [UserId] = @userId AND [TaxYear] = YEAR(P.SaleDate)),
            P.SaleDate,
            P.SourceType,
            P.SourceTransactionId,
            P.SourceTransactionDescription,
            P.SaleAmount,
            P.PlatformFee,
            P.ProcessingFee,
            P.NetShare,
            0
        FROM
             @partialIncomeRecords AS P
        
    COMMIT
            
    -- Create a summary of what was just imported
    declare @totalSalesImported INT = 0
    declare @totalFeesImported INT = 0
    declare @totalNetShareImported INT = 0
    declare @totalEstimatedTaxesImported INT = 0
    
    SELECT
        @totalSalesImported = SUM(V.SaleAmount),
        @totalFeesImported = SUM(V.ProcessingFee + V.PlatformFee),
        @totalNetShareImported = SUM(V.NetShare),
        @totalEstimatedTaxesImported = SUM(V.EstimatedTax)
    FROM 
        @recordIds AS R
    INNER JOIN
        FoxyBalance_IncomeRecordsView AS V
    ON
        R.Id = V.Id
    
    -- Return the summary
    SELECT
        (SELECT COUNT(Id) FROM @recordIds) AS TotalNewRecordsImported,
        @totalSalesImported AS TotalSalesImported,
        @totalFeesImported AS TotalFeesImported,
        @totalNetShareImported AS TotalNetShareImported,
        @totalEstimatedTaxesImported AS TotalEstimatedTaxesImported
END