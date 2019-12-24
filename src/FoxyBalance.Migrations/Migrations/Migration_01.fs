namespace FoxyBalance.Migrations

open SimpleMigrations

type Migration_01() =
    inherit Migration()
    override this.Down() =
        this.Execute "DROP TABLE FoxyBalance_Transactions"
        this.Execute "DROP TABLE FoxyBalance_Users"
        
    override this.Up() =
        this.Execute
            """
            BEGIN
            create table FoxyBalance_Users (
                Id int identity(1,1) primary key,
                EmailAddress nvarchar(500) not null,
                DateCreated datetime2 not null,
                HashedPassword nvarchar(max) not null
            );
            
            create index idx_emailaddress on FoxyBalance_Users (EmailAddress);
            END
            """
        this.Execute
            """
            BEGIN
            create table FoxyBalance_Transactions (
                Id bigint identity(1,1) primary key,
                UserId int not null constraint [fk_userid] references FoxyBalance_Users(Id),
                Name nvarchar(500) not null,
                Amount decimal(18,2) not null,
                DateCreated datetime2 not null,
                Type nvarchar(75) not null,
                Recurring bit not null,
                CheckNumber nvarchar(25) null,
                Status nvarchar(75) not null,
                ExpectedChargeDate datetime2 null,
                CompletedDate datetime2 null
            );
            
            create index idx_userid on FoxyBalance_Transactions(UserId);
            END
            """

