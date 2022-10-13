ALTER TABLE [FoxyBalance_IncomeRecords]
DROP CONSTRAINT [FK_IncomeRecords_TaxYearId], [FK_IncomeRecords_UserId], [UN_IncomeRecords_SourceTransactionId]

GO;

DROP TABLE [FoxyBalance_IncomeRecords];

GO;
    
