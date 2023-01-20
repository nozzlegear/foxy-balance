module FoxyBalance.Sync.Tests.PaypalTransactionParserTests

open System
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Xunit

[<Fact>]
let ``Should parse invoices`` () =
    let parser = PaypalTransactionParser()
    let result = parser.FromCsv("""Date,Time,TimeZone,Name,Gross,Discount,Fee,Net,Invoice Number,Reference Txn ID,Transaction ID,Transaction Type
01/05/2022,06:53:17,PST,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1297,INV2-TBRR-DSNR-9333-6JMQ,20112865T63777043,T0007""")
   
    Assert.NotNull(result) 
    Assert.True(Seq.length result = 1)

    let invoice = Seq.head result
    let expectedDate = DateTimeOffset.Parse("2022-01-05 06:53:17 -08:00")

    Assert.Equal(560000, invoice.Gross)
    Assert.Equal(0, invoice.Discount)
    Assert.Equal(27993, invoice.Fee)
    Assert.Equal(532007, invoice.Net)
    Assert.Equal("1297", invoice.InvoiceNumber)
    Assert.Equal("Tomorrow Corporation", invoice.Customer)
    Assert.Equal("Invoice 1297 to Tomorrow Corporation", invoice.Description)
    Assert.Equal("INV2-TBRR-DSNR-9333-6JMQ", invoice.Id)
    Assert.Equal(expectedDate, invoice.DateCreated)

[<Fact>]
let ``Should parse multiple invoices`` () =
    let parser = PaypalTransactionParser()
    let result = parser.FromCsv("""Date,Time,TimeZone,Name,Gross,Discount,Fee,Net,Invoice Number,Reference Txn ID,Transaction ID,Transaction Type
01/05/2022,06:53:17,PST,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1297,INV2-TBRR-DSNR-9333-6JMQ,20112865T63777043,T0007
02/01/2022,06:45:50,PST,Tomorrow Corporation,"4,200.00",0.00,-210.07,"3,989.93",1298,INV2-3B3D-VSMF-Q4EL-QXW2,64M12689BA373735J,T0007
03/01/2022,09:09:37,PST,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1299,INV2-US7R-76K7-VHLE-9469,6VJ37038VW678472X,T0007""")
   
    Assert.NotNull(result) 
    Assert.True(Seq.length result = 3)

    let gross = Seq.sumBy (fun invoice -> invoice.Gross) result

    Assert.Equal(1540000, gross)

[<Fact>]
let ``Should not parse rows that are not invoice income transactions`` () =
    let parser = PaypalTransactionParser()
    // Some transactions in paypal have the invoice transaction type (T0007) but are actually paymetns to other accounts.
    // Most often this appears to be payments to services like GoFundme or other Paypal invoices.
    // The difference is that they don't have invoice numbers and the amount is negative.
    let result = parser.FromCsv("""Date,Time,TimeZone,Name,Gross,Discount,Fee,Net,Invoice Number,Reference Txn ID,Transaction ID,Transaction Type
01/05/2022,06:53:17,PST,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1297,INV2-TBRR-DSNR-9333-6JMQ,20112865T63777043,T0007
02/01/2022,06:45:50,PST,Tomorrow Corporation,"4,200.00",0.00,-210.07,"3,989.93",1298,INV2-3B3D-VSMF-Q4EL-QXW2,64M12689BA373735J,T0007
03/01/2022,09:09:37,PST,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1299,INV2-US7R-76K7-VHLE-9469,6VJ37038VW678472X,T0007
03/16/2022,17:13:40,PDT,Payment to Jane Doe,-57.50,,0.00,-57.50,,0849469556932215K,9P623122TL4697742,T0007
03/27/2022,08:31:51,PDT,Payment to Jane Doe,-110.00,,0.00,-110.00,,6B707829DL176572V,6X640286YU157757T,T0007
04/04/2022,06:12:28,PDT,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1300,INV2-QGWV-U6M9-A2XV-RH7D,9661470978011963S,T0007
05/02/2022,07:55:10,PDT,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1301,INV2-6E9Z-QCKR-MEUD-U62C,8UM477445M018623Y,T0007
06/01/2022,07:22:33,PDT,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1302,INV2-X6UC-XK4E-ZVWK-TBND,2GW075570J515370D,T0007
07/04/2022,07:36:06,PDT,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1303,INV2-6SLK-6NV4-QZZ6-675X,29145797P0649083B,T0007
08/01/2022,08:22:58,PDT,Tomorrow Corporation,"4,200.00",0.00,-210.07,"3,989.93",1304,INV2-7U5K-6BKT-WXQU-ET7P,06T71750EX326061B,T0007
09/06/2022,05:09:46,PDT,Tomorrow Corporation,"5,600.00",0.00,-279.93,"5,320.07",1305,INV2-79V5-7FKG-N4BE-BY42,2WT723446R168071T,T0007""")

    Assert.Equal(9, Seq.length result)
     
    let gross = Seq.sumBy (fun invoice -> invoice.Gross) result

    Assert.Equal(4760000, gross)