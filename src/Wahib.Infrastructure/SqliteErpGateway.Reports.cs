using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Infrastructure;

public sealed partial class SqliteErpGateway
{
    private static decimal Money(long cents) => Amounts.AsMoney(cents);
    public IReadOnlyList<LedgerRow> Ledger(DateTime from,DateTime to)
    {
        if(from>to)throw new BusinessException("بداية الفترة بعد نهايتها.");
        return Sql.Query(_db,null,"""
          SELECT e.entry_date,e.id,l.account_code,a.name,e.description,l.debit,l.credit
          FROM JournalLines l JOIN JournalEntries e ON e.id=l.entry_id
          JOIN Accounts a ON a.code=l.account_code
          WHERE e.entry_date>=@s AND e.entry_date<=@e
          ORDER BY e.entry_date,e.created_at,l.id
          """,("@s",Day(from)),("@e",Day(to)))
          .Select(r=>new LedgerRow(r.Str("entry_date"),r.Str("id"),r.Str("account_code"),
          r.Str("name"),r.Str("description"),Money(r.Long("debit")),Money(r.Long("credit")))).ToArray();
    }
    public IReadOnlyList<ReportRow> TrialBalance(DateTime to)
    {
        return Sql.Query(_db,null,"""
          SELECT a.code,a.name,COALESCE(SUM(CASE WHEN e.entry_date<=@d THEN l.debit ELSE 0 END),0) dr,
          COALESCE(SUM(CASE WHEN e.entry_date<=@d THEN l.credit ELSE 0 END),0) cr
          FROM Accounts a LEFT JOIN JournalLines l ON l.account_code=a.code
          LEFT JOIN JournalEntries e ON e.id=l.entry_id
          GROUP BY a.code ORDER BY a.code
          """,("@d",Day(to)))
          .Select(r=>new ReportRow(r.Str("code")+" - "+r.Str("name"),Money(r.Long("dr")),Money(r.Long("cr")))).ToArray();
    }
    public IReadOnlyList<ReportRow> IncomeStatement(DateTime from,DateTime to)
    {
        var accounts=Sql.Query(_db,null,"""
          SELECT a.code,a.name,a.kind,COALESCE(SUM(l.debit),0) dr,COALESCE(SUM(l.credit),0) cr
          FROM Accounts a LEFT JOIN JournalLines l ON a.code=l.account_code
          LEFT JOIN JournalEntries e ON e.id=l.entry_id AND e.entry_date>=@s AND e.entry_date<=@t
          WHERE a.kind IN ('Income','ContraIncome','Expense')
          GROUP BY a.code ORDER BY a.code
          """,("@s",Day(from)),("@t",Day(to)));
        // Use filtered ledger entries to avoid LEFT JOIN leakage from outside-period lines.
        var ledger=Ledger(from,to);
        var rows=new List<ReportRow>();
        decimal profit=0;
        foreach(var a in accounts)
        {
            decimal net=ledger.Where(x=>x.Account==a.Str("code"))
                .Sum(x=>x.Credit-x.Debit);
            if(a.Str("kind") is "Expense" or "ContraIncome") net=-net;
            if(a.Str("kind")=="Income")profit+=net;else profit-=net;
            rows.Add(new ReportRow(a.Str("code")+" - "+a.Str("name"),net,0));
        }
        rows.Add(new ReportRow("صافي الربح / الخسارة",profit,0));
        return rows;
    }
    public IReadOnlyList<ReportRow> BalanceSheet(DateTime to)
    {
        var tb=TrialBalance(to);
        var rows=new List<ReportRow>();
        decimal assets=0, liabilities=0,equity=0;
        var accounts=Accounts();
        for(int i=0;i<accounts.Count;i++)
        {
            var (code,name,kind)=accounts[i];
            decimal debit=tb[i].First,credit=tb[i].Second;
            if(kind=="Asset") {var val=debit-credit;assets+=val;rows.Add(new ReportRow(code+" - "+name,val,0));}
            else if(kind=="Liability") {var val=credit-debit;liabilities+=val;rows.Add(new ReportRow(code+" - "+name,0,val));}
            else if(kind=="Equity") {var val=credit-debit;equity+=val;rows.Add(new ReportRow(code+" - "+name,0,val));}
        }
        decimal profit=IncomeStatement(new DateTime(2000,1,1),to).Last().First;
        rows.Add(new ReportRow("الأرباح المتراكمة (غير مقفلة لحساب حقوق الملكية)",0,profit));
        rows.Add(new ReportRow("الإجمالي: الأصول / الخصوم وحقوق الملكية",assets,liabilities+equity+profit));
        return rows;
    }
    public SummaryInfo Summary()
    {
        var ledger=Ledger(new DateTime(2000,1,1),DateTime.Today);
        decimal Balance(string code)=>ledger.Where(x=>x.Account==code).Sum(x=>x.Debit-x.Credit);
        decimal sales=-Balance(Accounting.Sales)-Balance(Accounting.SalesReturn);
        decimal cogs=Balance(Accounting.CostOfSales), general=Balance(Accounting.GeneralExpenses),stockVariance=Balance(Accounting.StockVariance);
        decimal profit=sales-cogs-general-stockVariance+(-Balance(Accounting.PurchaseVariance));
        decimal purchases=Documents().Where(x=>x.Type=="Purchase"&&x.Status=="Posted").Sum(x=>x.Total);
        return new SummaryInfo(sales,purchases,Balance(Accounting.Cash),Balance(Accounting.Inventory),
            Balance(Accounting.Receivable),-Balance(Accounting.Payable),profit);
    }
    public string VerifyIntegrity()
    {
        var check=Sql.Text(_db,null,"PRAGMA integrity_check");
        if(check!="ok")return "قاعدة البيانات: فشل integrity_check: "+check;
        if(Sql.Query(_db,null,"PRAGMA foreign_key_check").Count!=0) return "فشل فحص المفاتيح الخارجية";
        var entries=Sql.Query(_db,null,"""
          SELECT e.id,COALESCE(SUM(l.debit),0) dr,COALESCE(SUM(l.credit),0) cr
          FROM JournalEntries e LEFT JOIN JournalLines l ON l.entry_id=e.id
          GROUP BY e.id HAVING dr<>cr OR dr<=0
          """);
        if(entries.Count>0)return "توجد قيود غير متوازنة: "+entries.Count;
        var sum=Sql.Query(_db,null,"SELECT COALESCE(SUM(value),0) v FROM StockMovements")[0].Long("v");
        var stockGl=Sql.Number(_db,null,"SELECT COALESCE(SUM(debit-credit),0) FROM JournalLines WHERE account_code='1200'");
        if(sum!=stockGl)return $"فارق جرد مقابل الأستاذ: {Money(sum-stockGl)}";
        var negatives=Sql.Query(_db,null,"""
           SELECT item_id,COALESCE(SUM(qty),0) q,COALESCE(SUM(value),0) v
           FROM StockMovements GROUP BY item_id HAVING q<0 OR v<0 OR (q=0 AND v<>0)
           """);
        if(negatives.Count>0)return "رصيد كمية/قيمة صنف غير سليم: "+negatives.Count;
        return "OK | SQLite + قيود متوازنة + مخزون مطابق لدفتر الأستاذ";
    }
}
