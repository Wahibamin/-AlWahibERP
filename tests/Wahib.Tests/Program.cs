using Wahib.Application;
using Wahib.Domain;
using Wahib.Infrastructure;

static class Check
{
    private static int _count;
    public static void Equal<T>(T actual,T expected,string description)
    {
        _count++;
        if(!EqualityComparer<T>.Default.Equals(actual,expected))
            throw new Exception($"FAILED [{description}]: actual={actual} expected={expected}");
        Console.WriteLine("PASS "+description);
    }
    public static void Throws(Action action,string description)
    {
        _count++;
        try{action();}catch(BusinessException){Console.WriteLine("PASS "+description);return;}
        throw new Exception("Expected failure: "+description);
    }
    public static void Run()
    {
        Equal(Amounts.LineTotal(1250000,80000),10000000L,"fixed-point line total 8 x 125");
        Equal(Amounts.MovingCost(42000000,400000,50000),5250000L,"moving average cost 5 x 105");
        Equal(Amounts.MovingCost(1,30000,30000),1L,"last item zeroes residual stock");
        Throws(()=>Amounts.Balanced([Accounting.Dr(Accounting.Cash,100),Accounting.Cr(Accounting.Capital,90)]),"reject unbalanced journal");
        var dir=Path.Combine(Path.GetTempPath(),"wahib-tests-"+Guid.NewGuid().ToString("N"));
        CompanyStore.Root=dir;
        try
        {
            var store=new CompanyStore();
            var company=store.Create("Test Company","YER","StrongTestPass123!");
            using var db=store.Open(company.Id);
            var actor=db.Login("owner","StrongTestPass123!");
            var today=DateTime.Today;
            long supplier=db.AddParty(actor,"Supplier","Supplier","111");
            long customer=db.AddParty(actor,"Customer","Customer","222");
            long item=db.AddItem(actor,"TEST-01","A","barcode-1","حبة",170,5);
            db.PostJournal(actor,today,"رأس مال افتتاحي",[
               new JournalRow(Accounting.Cash,100000,0),new JournalRow(Accounting.Capital,0,100000)]);
            string Invoice(DocumentKind kind,long party,decimal q,decimal price,string mode="Credit",string? original=null,long? originLine=null)
                =>db.CreateDocument(actor,new DocumentDraft(kind,party,today,mode,null,original,[new LineDraft(item,q,price,originLine)]));
            var p1=Invoice(DocumentKind.Purchase,supplier,50,100);db.PostDocument(actor,p1);
            Equal(db.Items().Single().Quantity,50m,"purchase 50 stock");
            Equal(db.Items().Single().Value,5000m,"purchase valuation 5000");
            var s1=Invoice(DocumentKind.Sale,customer,20,150);db.PostDocument(actor,s1);
            db.PostDocument(actor,s1); // repeat must be no-op
            Equal(db.Items().Single().Quantity,30m,"sale once quantity 30");
            Equal(db.Items().Single().Value,3000m,"COGS 2000 leaves value 3000");
            db.MakePayment(actor,PaymentKind.CustomerReceipt,customer,1000,s1,today,"partial");
            var originLine=db.DocumentLines(s1).Single().LineId;
            var r1=Invoice(DocumentKind.SalesReturn,customer,2,150,"Credit",s1,originLine);
            db.PostDocument(actor,r1);
            Equal(db.Items().Single().Quantity,32m,"sales return adds 2");
            Equal(db.Items().Single().Value,3200m,"historical returned cost 200");
            var p2=Invoice(DocumentKind.Purchase,supplier,8,125);db.PostDocument(actor,p2);
            Equal(db.Items().Single().Value,4200m,"moving average 105");
            var s2=Invoice(DocumentKind.Sale,customer,5,170);db.PostDocument(actor,s2);
            Equal(db.Items().Single().Quantity,35m,"after second sale qty 35");
            Equal(db.Items().Single().Value,3675m,"after second sale stock 3675");
            db.AdjustStock(actor,item,34,today,"عجز وحدة واحدة");
            var summary=db.Summary();
            Equal(summary.Cash,101000m,"cash after customer collection");
            Equal(summary.Inventory,3570m,"ending inventory value");
            Equal(summary.CustomersDue,2550m,"receivables after return");
            Equal(summary.SuppliersDue,6000m,"supplier liabilities");
            Equal(summary.Profit,1120m,"correct accounting profit");
            var balance=db.BalanceSheet(today).Last();
            Equal(balance.First,107120m,"assets 107120");
            Equal(balance.Second,107120m,"liabilities + equity 107120");
            Equal(db.VerifyIntegrity().StartsWith("OK"),true,"SQLite/GL/Stock consistent");
            Throws(()=>{var over=Invoice(DocumentKind.Sale,customer,1000,999);db.PostDocument(actor,over);},"reject negative stock with transaction rollback");
            Equal(db.VerifyIntegrity().StartsWith("OK"),true,"after denied sale integrity remains");
            // Purchase return after a later higher-cost receipt: book moving cost and vendor price variance.
            long bItem=db.AddItem(actor,"TEST-02","B","barcode-2","حبة",30,0);
            string PurchaseB(decimal qty,decimal price)
                =>db.CreateDocument(actor,new DocumentDraft(DocumentKind.Purchase,supplier,today,"Credit",null,null,
                    [new LineDraft(bItem,qty,price)]));
            string b1=PurchaseB(5,10);db.PostDocument(actor,b1);
            string b2=PurchaseB(5,20);db.PostDocument(actor,b2);
            var bLine=db.DocumentLines(b1).Single().LineId;
            string bReturn=db.CreateDocument(actor,new DocumentDraft(DocumentKind.PurchaseReturn,supplier,today,"Credit",null,b1,
                [new LineDraft(bItem,1,10,bLine)]));
            db.PostDocument(actor,bReturn);
            Equal(db.Items().Single(x=>x.Id==bItem).Value,135m,"purchase return uses moving stock cost, not vendor price");
            Equal(db.VerifyIntegrity().StartsWith("OK"),true,"purchase price variance balances stock and GL");
            db.AddUser(actor,"cashier","TestCashierPass123!",UserRole.Cashier);
            var cashier=db.Login("cashier","TestCashierPass123!");
            Throws(()=>db.PostJournal(cashier,today,"unauthorized",[
                new JournalRow(Accounting.Cash,10,0),new JournalRow(Accounting.Capital,0,10)]),"cashier cannot post manual journal");
            Throws(()=>db.PostJournal(actor,today,"illegal inventory posting",[
                new JournalRow(Accounting.Inventory,100,0),new JournalRow(Accounting.Capital,0,100)]),"deny manual posting controlled inventory account");
            var backup=Path.Combine(dir,"backup.db");
            db.Backup(actor,backup);
            Equal(File.Exists(backup+".manifest.json"),true,"backup manifest created");
            db.SetPeriod(actor,today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),true);
            Throws(()=>db.PostJournal(actor,today,"closed period",[
               new JournalRow(Accounting.Cash,50,0),new JournalRow(Accounting.Capital,0,50)]),"closed period rejects posting");
            db.SetPeriod(actor,today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),false);
            Equal(db.Audit(actor).All(x=>x.ChainValid),true,"audit chain verifies");
            Console.WriteLine($"ALL {_count} CHECKS PASSED");
        }
        finally {try{Directory.Delete(dir,true);}catch{}}
    }
}

internal static class Program
{
    private static int Main()
    {
        try{Check.Run();return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}

