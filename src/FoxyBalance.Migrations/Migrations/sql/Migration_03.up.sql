CREATE TABLE [FoxyBalance_TaxYears] (
    [Id] INT PRIMARY KEY IDENTITY(1, 1) NOT NULL,
    [UserId] INT NOT NULL CONSTRAINT [FK_TaxYears_UserId] FOREIGN KEY REFERENCES [FoxyBalance_Users](Id),
    [TaxYear] INT NOT NULL,
    -- The tax rate is stored as a whole number, e.g. 10% is stored as 10 and 25% is stored as 25
    [TaxRate] INT NOT NULL
)

GO;