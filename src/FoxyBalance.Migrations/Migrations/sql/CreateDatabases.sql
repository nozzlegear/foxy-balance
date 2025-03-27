-- IMPORTANT: Override foxyBalanceServerPassword with the -v switch when using sqlcmd
:SETVAR foxyBalanceServerPassword "a-BAD_passw0rd"

USE master;
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'FoxyBalance')
    CREATE DATABASE FoxyBalance;
GO

IF SUSER_ID(N'FoxyBalance_Server') IS NULL
    CREATE LOGIN [FoxyBalance_Server] WITH PASSWORD = N'$(foxyBalanceServerPassword)';
GO

USE FoxyBalance;
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'FoxyBalance_Server')
BEGIN
    CREATE USER [FoxyBalance_Server] FOR LOGIN [FoxyBalance_Server];
    ALTER ROLE [db_owner] ADD MEMBER [FoxyBalance_Server];
END
GO
