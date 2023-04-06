module FoxyBalance.Sync.Tests.PaypalTransactionParserTests

open System
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Xunit

let [<Literal>] HeaderRow = """Date,Time,TimeZone,Name,Type,Status,Currency,Gross,Fee,Net,From Email Address,To Email Address,Transaction ID,CounterParty Status,Shipping Address,Address Status,Item Title,Item ID,Shipping and Handling Amount,Insurance Amount,Sales Tax,Option 1 Name,Option 1 Value,Option 2 Name,Option 2 Value,Escrow Id,Reference Txn ID,Invoice Number,Custom Number,Quantity,Receipt ID,Balance,Address Line 1,Address Line 2/District/Neighborhood,Town/City,State/Province/Region/County/Territory/Prefecture/Republic,Zip/Postal Code,Country,Contact Phone Number,Subject,Note,Payment Source,Card Type,Transaction Event Code,Payment Tracking ID,Bank Reference ID,Transaction Buyer Country Code,Item Details,Country Code,Balance Impact,Buyer Wallet,Comment 1,Comment 2,Invoice Number_2,PO Number,Customer Reference Number,Payflow Transaction ID (PNREF),Tip,Discount,Seller ID,Risk Filter,Credit Transactional Fee,Credit Promotional Fee,Credit Term,Credit Offer Type,Original Invoice ID,Payment Source Subtype,Decline Code"""

[<Fact>]
let ``Should parse invoices`` () =
    let parser = PaypalTransactionParser()
    let result = parser.FromCsv($"""{HeaderRow}
01/05/2022,06:53:17,PST,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,20112865T63472043,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-TWHM-DSNR-9333-6JMQ,1297,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,""")
   
    Assert.NotNull(result) 
    Assert.True(Seq.length result = 1)

    let invoice = Seq.head result
    let expectedDate = DateTimeOffset.Parse("2022-01-05 06:53:17 -08:00")

    Assert.Equal(123400, invoice.Gross)
    Assert.Equal(0, invoice.Discount)
    Assert.Equal(23300, invoice.Fee)
    Assert.Equal(123300, invoice.Net)
    Assert.Equal("1297", invoice.InvoiceNumber)
    Assert.Equal("Tomorrow Corporation", invoice.Customer)
    Assert.Equal("Invoice 1297", invoice.Description)
    Assert.Equal("INV2-TWHM-DSNR-9333-6JMQ", invoice.Id)
    Assert.Equal(expectedDate, invoice.DateCreated)
    
[<Fact>]
let ``Should parse express invoices`` () =
    // Express invoices have the invoice transaction type of T0006
    let parser = PaypalTransactionParser()
    let result = parser.FromCsv($"""{HeaderRow}
01/05/2022,06:53:17,PST,Tomorrow Corporation,Express Checkout Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,20112865T63472043,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-TWHM-DSNR-9333-80YN,1308,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0006,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,""")
   
    Assert.NotNull(result) 
    Assert.True(Seq.length result = 1)

    let invoice = Seq.head result
    let expectedDate = DateTimeOffset.Parse("2022-01-05 06:53:17 -08:00")

    Assert.Equal(123400, invoice.Gross)
    Assert.Equal(0, invoice.Discount)
    Assert.Equal(23300, invoice.Fee)
    Assert.Equal(123300, invoice.Net)
    Assert.Equal("1308", invoice.InvoiceNumber)
    Assert.Equal("Tomorrow Corporation", invoice.Customer)
    Assert.Equal("Invoice 1308", invoice.Description)
    Assert.Equal("INV2-TWHM-DSNR-9333-80YN", invoice.Id)
    Assert.Equal(expectedDate, invoice.DateCreated)

[<Fact>]
let ``Should parse multiple invoices`` () =
    let parser = PaypalTransactionParser()
    let result = parser.FromCsv($"""{HeaderRow}
01/05/2022,06:53:17,PST,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,20112865T63472043,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-TWHM-DSNR-9333-6JMQ,1297,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
01/05/2022,06:53:17,PST,Tomorrow Corporation,Express Checkout Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,20112865T63472043,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-TWHM-DSNR-9333-80YN,1308,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0006,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,""")
   
    Assert.NotNull(result) 
    Assert.True(Seq.length result = 2)

    let gross = Seq.sumBy (fun invoice -> invoice.Gross) result

    Assert.Equal(246800, gross)

[<Fact>]
let ``Should not parse rows that are not invoice income transactions`` () =
    let parser = PaypalTransactionParser()
    // Some transactions in paypal have the invoice transaction type (T0007) but are actually payments to other accounts.
    // Most often this appears to be payments to services like GoFundMe or other Paypal invoices.
    // The difference is that they don't have invoice numbers and the amount is negative.
    let result = parser.FromCsv($"""{HeaderRow}
01/05/2022,06:53:17,PST,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,20112865T63472043,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-TWHM-DSNR-9333-6JMQ,1297,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
02/01/2022,06:45:50,PST,Tomorrow Corporation,Website Payment,Completed,USD,"4,200.00",-210.07,"3,989.93",jane.doe@example.com,john.doe@example.com,64M12689BA393135J,Unverified,"Marion Duchesne, 7250 Mile End, Suite 301, Montreal, MN, H2R3A4, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-3A3D-VSMF-Q4EL-QXW2,1298,,3,,"3,989.93",7250 Mile End,Suite 301,Montreal,MN,H2R3A4,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
03/16/2022,17:13:40,PDT,Payment to Umbrella Corporation,Website Payment,Completed,EUR,-57.50,0.00,-57.50,john.doe@example.com,,9P625722TL4699942,Verified,,Non-Confirmed,,,,,,,,,,,0849469556932215K,,,,,-57.50,,,,,,,,,,PayPal,,T0007,,,DE,,,Debit,,,,,,,,,,,,,,,,,,
03/27/2022,08:31:51,PDT,Payment to Umbrella Corporation,Website Payment,Completed,USD,-110.00,0.00,-110.00,john.doe@example.com,,6X750286YU152750T,Verified,,Non-Confirmed,,,,,,,,,,,6B707829DL176572V,,,,,-110.00,,,,,,,,,,PayPal,,T0007,,,US,,,Debit,,,,,,,,,,,,,,,,,,
03/01/2022,09:09:37,PST,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,6VJ37038VW648422X,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-UU7L-77K7-VHLE-9469,1299,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
04/04/2022,06:12:28,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,9661470998011963S,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-QGWV-UKM9-A2XV-RH7D,1300,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
05/02/2022,07:55:10,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,8UM447445M018623Y,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-NE9Z-MNKR-MEUD-U62C,1301,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
06/01/2022,07:22:33,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,2GW065530J515370D,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-X2UC-XK4E-ZVWK-TBND,1302,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
07/04/2022,07:36:06,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,29145599P0649083B,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-LSLK-NNV4-QZZ6-675X,1303,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
08/01/2022,08:22:58,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"4,200.00",-210.07,"3,989.93",jane.doe@example.com,john.doe@example.com,06T11250EX326061B,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-LU5K-9BKT-WXQU-ET7P,1304,,3,,"3,989.93",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
09/06/2022,05:09:46,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,2WT023446R168061T,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-G9V5-NFKG-N4BE-BY42,1305,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,
10/04/2022,05:27:50,PDT,Tomorrow Corporation,Website Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,4L599772DF651945T,Unverified,,Non-Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-XC2G-5EK9-YUYG-ZYWY,1306,,4,,"1,233.00",,,,,,,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0007,,,US,,,Credit,,,,,,,,,0.00,,,,,,,,,
01/05/2022,06:53:17,PST,Tomorrow Corporation,Express Checkout Payment,Completed,USD,"1,234.00",-233.00,"1,233.00",jane.doe@example.com,john.doe@example.com,20112865T63472043,Unverified,"Jane Doe, 123 4th ST, Minneapolis, MN, 55555, United States",Confirmed,Payment to Tomorrow Corporation for Invoice #1234,,,,,,,,,,INV2-TWHM-DSNR-9333-80YN,1308,,4,,"1,233.00",123 4th ST,,Minneapolis,MN,55555,United States,5145103300,Payment to Tomorrow Corporation for Invoice #1234,,PayPal,,T0006,,,US,,US,Credit,,,,,,,,,0.00,,,,,,,,,""")

    Assert.Equal(11, Seq.length result)
     
    let gross = Seq.sumBy (fun invoice -> invoice.Gross) result

    Assert.Equal(1950600, gross)
