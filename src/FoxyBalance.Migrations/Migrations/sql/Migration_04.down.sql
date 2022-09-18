ALTER TABLE [FoxyBalance_IncomeRecords]
DROP CONSTRAINT [FK_IncomeRecords_TaxYearId], [FK_IncomeRecords_UserId];

GO;

DROP TABLE [FoxyBalance_IncomeRecords];

GO;
    
