namespace FoxyBalance.Database.Tests

open System
open Faqt
open Faqt.Operators
open FoxyBalance.Database
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Database.Tests.Domain
open Npgsql
open Xunit

[<Collection("IncomeDatabase")>]
type IncomeDatabaseTests(fixture: DbContainerFixture) =
    let database: IIncomeDatabase = IncomeDatabase(TestDatabaseOptions fixture)
    let userDatabase: IUserDatabase = UserDatabase(TestDatabaseOptions fixture)
    let bogus = Bogus.Faker()

    let createUser () =
        userDatabase.CreateAsync { EmailAddress = bogus.Internet.Email(); HashedPassword = bogus.Internet.Password() }

    [<Fact>]
    member _.``ImportAsync should create income records and return a summary``() =
        task {
            // Setup
            let! user = createUser()
            let partialIncomeRecords: PartialIncomeRecord list = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = bogus.Date.RecentOffset()
                  SaleAmount = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000))
                  PlatformFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 5))
                  ProcessingFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 2))
                  NetShare = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000)) }

                { Source = IncomeSource.Shopify
                    { TransactionId = bogus.Random.AlphaNumeric(12)
                      CustomerDescription = bogus.Company.CompanyName()
                      Description = bogus.Commerce.ProductName() }
                  SaleDate = bogus.Date.RecentOffset()
                  SaleAmount = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000))
                  PlatformFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 5))
                  ProcessingFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 2))
                  NetShare = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000)) }
            ]

            // Act
            let! result = database.ImportAsync(user.Id, partialIncomeRecords)

            // Assert
            %result.TotalNewRecordsImported.Should().Be(2)
            %result.TotalEstimatedTaxesImported.Should().BeGreaterThan(0)
            %result.TotalSalesImported.Should().Be(partialIncomeRecords |> Seq.sumBy _.SaleAmount)
            %result.TotalNetShareImported.Should().Be(partialIncomeRecords |> Seq.sumBy _.NetShare)
            %result.TotalFeesImported.Should().Be(partialIncomeRecords |> Seq.sumBy (fun x -> x.PlatformFee + x.ProcessingFee))
        }


    [<Fact>]
    member _.``ImportAsync should fail if the user does not exist``() =
        task {
            // Setup
            let partialIncomeRecords: PartialIncomeRecord list = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = bogus.Date.RecentOffset()
                  SaleAmount = Decimal.ToInt32 (100M * bogus.Finance.Amount())
                  PlatformFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(max = 5))
                  ProcessingFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(max = 2))
                  NetShare = Decimal.ToInt32 (100M * bogus.Finance.Amount()) }
            ]

            // Act
            let act () =
                database.ImportAsync(-1, partialIncomeRecords)
                |> Async.AwaitTask
                |> Async.RunSynchronously

            // Assert
            %act.Should().ThrowInner<PostgresException, _>()
                 .Whose
                 .Message.Should().Contain("violates foreign key constraint")
        }


    [<Fact>]
    member _.``ListAsync should return income records for a user and tax year``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000))
                  PlatformFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 5))
                  ProcessingFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 2))
                  NetShare = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000)) }
                { Source = IncomeSource.Stripe
                    { TransactionId = bogus.Random.AlphaNumeric(12)
                      CustomerDescription = bogus.Company.CompanyName()
                      Description = bogus.Commerce.ProductName() }
                  SaleDate = DateTimeOffset(DateTime(year, 2, 20), TimeSpan.Zero)
                  SaleAmount = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000))
                  PlatformFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 5))
                  ProcessingFee = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 1, max = 2))
                  NetShare = Decimal.ToInt32 (100M * bogus.Finance.Amount(min = 10, max = 1000)) }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()

            // Act
            let! result = database.ListAsync(user.Id, year, { Limit = 10; Offset = 0; Order = Order.Descending })

            // Assert
            let resultList = result |> Seq.toList
            %resultList.Length.Should().Be(2)
            %resultList[0].SaleDate.Should().Be(records[1].SaleDate)  // Descending order
            %resultList[1].SaleDate.Should().Be(records[0].SaleDate)
        }


    [<Fact>]
    member _.``ListAsync should return empty list for different tax year``() =
        task {
            // Setup
            let! user = createUser()
            let currentYear = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(currentYear, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()

            // Act
            let! result = database.ListAsync(user.Id, currentYear - 1, { Limit = 10; Offset = 0; Order = Order.Ascending })

            // Assert
            %result.Should().BeEmpty()
        }


    [<Fact>]
    member _.``SummarizeAsync should return summary for a tax year``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
                { Source = IncomeSource.Shopify
                    { TransactionId = bogus.Random.AlphaNumeric(12)
                      CustomerDescription = bogus.Company.CompanyName()
                      Description = bogus.Commerce.ProductName() }
                  SaleDate = DateTimeOffset(DateTime(year, 2, 20), TimeSpan.Zero)
                  SaleAmount = 20000
                  PlatformFee = 200
                  ProcessingFee = 100
                  NetShare = 19700 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()

            // Act
            let! result = database.SummarizeAsync(user.Id, year)

            // Assert
            %result.Should().BeSome()
            let summary = result.Value
            %summary.TotalRecords.Should().Be(2)
            %summary.TotalSales.Should().Be(30000)
            %summary.TotalFees.Should().Be(450)
            %summary.TotalNetShare.Should().Be(29550)
            %summary.TotalEstimatedTax.Should().BeGreaterThan(0)
        }


    [<Fact>]
    member _.``SummarizeAsync should return None for tax year with no records``() =
        task {
            // Setup
            let! user = createUser()

            // Act
            let! result = database.SummarizeAsync(user.Id, 2020)

            // Assert
            %result.Should().BeNone()
        }


    [<Fact>]
    member _.``GetAsync should return income record by ID``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.Stripe
                    { TransactionId = bogus.Random.AlphaNumeric(12)
                      CustomerDescription = bogus.Company.CompanyName()
                      Description = "Test Product" }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()
            let! allRecords = database.ListAsync(user.Id, year, { Limit = 10; Offset = 0; Order = Order.Ascending })
            let firstRecord = allRecords |> Seq.head

            // Act
            let! result = database.GetAsync(user.Id, firstRecord.Id)

            // Assert
            %result.Should().BeSome()
            let record = result.Value
            %record.Id.Should().Be(firstRecord.Id)
            %record.SaleAmount.Should().Be(10000)
        }


    [<Fact>]
    member _.``GetAsync should return None for non-existent ID``() =
        task {
            // Setup
            let! user = createUser()

            // Act
            let! result = database.GetAsync(user.Id, 999999L)

            // Assert
            %result.Should().BeNone()
        }


    [<Fact>]
    member _.``SetIgnoreAsync should toggle ignore flag on income record``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()
            let! allRecords = database.ListAsync(user.Id, year, { Limit = 10; Offset = 0; Order = Order.Ascending })
            let record = allRecords |> Seq.head

            // Act - Set to ignored
            do! database.SetIgnoreAsync(user.Id, record.Id, true)
            let! afterIgnore = database.GetAsync(user.Id, record.Id)

            // Assert
            %afterIgnore.Should().BeSome()
            %afterIgnore.Value.Ignored.Should().BeTrue()

            // Act - Set back to not ignored
            do! database.SetIgnoreAsync(user.Id, record.Id, false)
            let! afterUnignore = database.GetAsync(user.Id, record.Id)

            // Assert
            %afterUnignore.Should().BeSome()
            %afterUnignore.Value.Ignored.Should().BeFalse()
        }


    [<Fact>]
    member _.``DeleteAsync should remove income record``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()
            let! allRecords = database.ListAsync(user.Id, year, { Limit = 10; Offset = 0; Order = Order.Ascending })
            let record = allRecords |> Seq.head

            // Act
            do! database.DeleteAsync(user.Id, record.Id)
            let! afterDelete = database.GetAsync(user.Id, record.Id)

            // Assert
            %afterDelete.Should().BeNone()
        }


    [<Fact>]
    member _.``ListTaxYearsAsync should return all tax years for a user``() =
        task {
            // Setup
            let! user = createUser()
            let year1 = DateTime.UtcNow.Year
            let year2 = year1 - 1
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year1, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
                { Source = IncomeSource.Shopify
                    { TransactionId = bogus.Random.AlphaNumeric(12)
                      CustomerDescription = bogus.Company.CompanyName()
                      Description = bogus.Commerce.ProductName() }
                  SaleDate = DateTimeOffset(DateTime(year2, 6, 20), TimeSpan.Zero)
                  SaleAmount = 20000
                  PlatformFee = 200
                  ProcessingFee = 100
                  NetShare = 19700 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()

            // Act
            let! result = database.ListTaxYearsAsync(user.Id)

            // Assert
            let years = result |> Seq.map _.TaxYear |> Seq.toList
            %years.Should().Contain(year1)
            %years.Should().Contain(year2)
            %years.Length.Should().BeGreaterThanOrEqualTo(2)
        }


    [<Fact>]
    member _.``GetTaxYearAsync should return tax year details``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()

            // Act
            let! result = database.GetTaxYearAsync(user.Id, year)

            // Assert
            %result.Should().BeSome()
            let taxYear = result.Value
            %taxYear.TaxYear.Should().Be(year)
            %taxYear.TaxRate.Should().Be(33)  // Default tax rate from the SQL function
        }


    [<Fact>]
    member _.``GetTaxYearAsync should return None for non-existent tax year``() =
        task {
            // Setup
            let! user = createUser()

            // Act
            let! result = database.GetTaxYearAsync(user.Id, 1999)

            // Assert
            %result.Should().BeNone()
        }


    [<Fact>]
    member _.``SetTaxYearRateAsync should update tax rate for a tax year``() =
        task {
            // Setup
            let! user = createUser()
            let year = DateTime.UtcNow.Year
            let records = [
                { Source = IncomeSource.ManualTransaction
                    { Description = bogus.Commerce.ProductName()
                      CustomerDescription = Some (bogus.Company.CompanyName()) }
                  SaleDate = DateTimeOffset(DateTime(year, 1, 15), TimeSpan.Zero)
                  SaleAmount = 10000
                  PlatformFee = 100
                  ProcessingFee = 50
                  NetShare = 9850 }
            ]
            let! _ = database.ImportAsync(user.Id, records)
            ()

            // Act
            do! database.SetTaxYearRateAsync(user.Id, year, 25)
            let! result = database.GetTaxYearAsync(user.Id, year)

            // Assert
            %result.Should().BeSome()
            %result.Value.TaxRate.Should().Be(25)
        }
