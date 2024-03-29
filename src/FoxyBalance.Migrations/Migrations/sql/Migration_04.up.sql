CREATE TABLE [FoxyBalance_IncomeRecords] (
    [Id] BIGINT PRIMARY KEY IDENTITY(1, 1) NOT NULL,
    [UserId] INT NOT NULL CONSTRAINT [FK_IncomeRecords_UserId] FOREIGN KEY REFERENCES FoxyBalance_Users(Id),
    [TaxYearId] INT NOT NULL CONSTRAINT [FK_IncomeRecords_TaxYearId] FOREIGN KEY REFERENCES FoxyBalance_TaxYears(Id),
    [SaleDate] DATETIMEOFFSET NOT NULL,
    [SourceType] NVARCHAR(18) NOT NULL,
    [SourceTransactionId] NVARCHAR(510) NULL INDEX [IDX_IncomeRecords_SourceTransactionId],
    [SourceTransactionDescription] NVARCHAR(1000) NULL,
    [SourceTransactionCustomerDescription] NVARCHAR(510) NULL,
    [SaleAmount] INT NOT NULL,
    [PlatformFee] INT NOT NULL,
    [ProcessingFee] INT NOT NULL,
    [NetShare] INT NOT NULL,
    [Ignored] BIT NOT NULL
)
