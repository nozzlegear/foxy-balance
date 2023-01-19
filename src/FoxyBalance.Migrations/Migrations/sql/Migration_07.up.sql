CREATE TYPE dbo.tp_PartialIncomeRecord AS TABLE
(
    [SaleDate] DATETIMEOFFSET NOT NULL,
    [SourceType] NVARCHAR(18) NOT NULL,
    [SourceTransactionId] NVARCHAR(510) NULL,
    [SourceTransactionDescription] NVARCHAR(1000) NULL,
    [SourceTransactionCustomerDescription] NVARCHAR(510) NULL,
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
    declare @mergedRecords TABLE (Id BIGINT, Action nvarchar(255));

    BEGIN TRANSACTION
        
        -- Use a SQL MERGE command to insert or update income records in one query
        MERGE [FoxyBalance_IncomeRecords] Target
        USING @partialIncomeRecords Source
        ON Target.SourceTransactionId = Source.SourceTransactionId AND Target.UserId = @userId
        WHEN MATCHED THEN
        UPDATE SET
            SourceType = Source.SourceType,
            SourceTransactionDescription = Source.SourceTransactionDescription,
            SourceTransactionCustomerDescription = Source.SourceTransactionCustomerDescription,
            SaleDate = Source.SaleDate,
            SaleAmount = Source.SaleAmount,
            PlatformFee = Source.PlatformFee,
            ProcessingFee = Source.ProcessingFee,
            NetShare = Source.NetShare
        WHEN NOT MATCHED BY TARGET THEN
        INSERT (
            UserId,
            TaxYearId,
            SaleDate,
            SourceType,
            SourceTransactionId,
            SourceTransactionDescription,
            SourceTransactionCustomerDescription,
            SaleAmount,
            PlatformFee,
            ProcessingFee,
            NetShare,
            Ignored
        )
        VALUES (
            @userId,
            (SELECT TOP 1 [Id] FROM [FoxyBalance_TaxYears] WHERE [UserId] = @userId AND [TaxYear] = YEAR(Source.SaleDate)),
            Source.SaleDate,
            Source.SourceType,
            Source.SourceTransactionId,
            Source.SourceTransactionDescription,
            Source.SourceTransactionCustomerDescription,
            Source.SaleAmount,
            Source.PlatformFee,
            Source.ProcessingFee,
            Source.NetShare,
            0
        )
        OUTPUT inserted.Id, $action AS [Action] INTO @mergedRecords;

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
        @mergedRecords AS R
    INNER JOIN
        FoxyBalance_IncomeRecordsView AS V
    ON
        R.Id = V.Id
    WHERE
        R.Action = 'INSERT'
    
    -- Return the summary
    SELECT
        (SELECT COUNT(Id) FROM @mergedRecords WHERE [Action] = 'INSERT') AS TotalNewRecordsImported,
        COALESCE(@totalSalesImported, 0) AS TotalSalesImported,
        COALESCE(@totalFeesImported, 0) AS TotalFeesImported,
        COALESCE(@totalNetShareImported, 0) AS TotalNetShareImported,
        COALESCE(@totalEstimatedTaxesImported, 0) AS TotalEstimatedTaxesImported
END
