using Microsoft.Data.Sqlite;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Infrastructure;

public sealed partial class SqliteErpGateway
{
    public void AdjustStock(LoginInfo actor,long itemId,decimal counted,DateTime date,string reason)
    {
        Authorized(actor,"Inventory");
        if(string.IsNullOrWhiteSpace(reason))throw new BusinessException("سبب التسوية إلزامي.");
        long target=Amounts.SignedQuantity(counted);
        if(target<0)throw new BusinessException("الجرد لا يمكن أن يكون سالبًا.");
        using var tx=_db.BeginTransaction();
        CheckDate(tx,date);CheckChronology(tx,itemId,date);
        if(Sql.Number(_db,tx,"SELECT COUNT(*) FROM Items WHERE id=@i AND active=1",("@i",itemId))==0)
            throw new BusinessException("الصنف غير موجود.");
        var stock=Stock(tx,itemId);
        long difference=checked(target-stock.Quantity);
        if(difference==0)throw new BusinessException("لا يوجد فرق يحتاج إلى تسوية.");
        long value;
        if(difference<0) value=-Amounts.MovingCost(stock.Value,stock.Quantity,-difference);
        else
        {
            // New stock with zero historical average requires explicit cost via purchase, not fabricated valuation.
            if(stock.Quantity==0)throw new BusinessException("أدخل رصيد بداية المخزون بإجراء شراء أو قيد افتتاحي معتمد؛ لا يمكن افتراض تكلفة الجرد.");
            value=checked((long)decimal.Round((decimal)stock.Value*difference/stock.Quantity,0,MidpointRounding.AwayFromZero));
        }
        var id=Id();string no=NextNumber(tx,"ADJ",date);
        Sql.Exec(_db,tx,"""
          INSERT INTO Documents(id,doc_no,type,status,party_id,issued_on,payment_mode,total,posted_at,user_id)
          VALUES (@i,@n,'ADJUST','Posted',1,@d,'Credit',0,@t,@u)
          """,("@i",id),("@n",no),("@d",Day(date)),("@t",Now()),("@u",actor.UserId));
        Movement(tx,id,null,itemId,difference,value,date);
        var legs=value>0
            ? new List<EntryLeg>{Accounting.Dr(Accounting.Inventory,value),Accounting.Cr(Accounting.StockVariance,value)}
            : new List<EntryLeg>{Accounting.Dr(Accounting.StockVariance,-value),Accounting.Cr(Accounting.Inventory,-value)};
        InsertJournal(tx,"DOC:"+id,Day(date),"جرد "+no+" | "+reason,actor.UserId,legs);
        AuditEvent(tx,actor.UserId,"StockAdjusted",no+":"+reason);
        tx.Commit();
    }
    public void PostJournal(LoginInfo actor,DateTime date,string explanation,IReadOnlyList<JournalRow> lines)
    {
        Authorized(actor,"Ledger");
        if(string.IsNullOrWhiteSpace(explanation))throw new BusinessException("وصف القيد إلزامي.");
        var restricted=new[]{Accounting.Inventory,Accounting.Receivable,Accounting.Payable,Accounting.Sales,
            Accounting.SalesReturn,Accounting.CostOfSales,Accounting.StockVariance,Accounting.PurchaseVariance};
        if(lines.Any(x=>restricted.Contains(x.Account)))
            throw new BusinessException("الحسابات الضابطة للمبيعات والمخزون والذمم تُرحّل فقط بمستندات أعمال؛ لا يسمح بقيد يدوي مباشر.");
        var legs=lines.Select(x=>new EntryLeg(x.Account,Amounts.Money(x.Debit),Amounts.Money(x.Credit),x.PartyId)).ToList();
        Amounts.Balanced(legs);
        using var tx=_db.BeginTransaction();
        CheckDate(tx,date);
        var id=Id();
        string no=NextNumber(tx,"JRN",date);
        InsertJournal(tx,"MANUAL:"+id,Day(date),no+" | "+explanation,actor.UserId,legs);
        AuditEvent(tx,actor.UserId,"ManualJournalPosted",no+":"+explanation);
        tx.Commit();
    }
    private long CashBalance(SqliteTransaction tx) => Sql.Number(_db,tx,"""
        SELECT COALESCE(SUM(l.debit-l.credit),0) FROM JournalLines l
        WHERE l.account_code='1010'
        """);
    public void MakePayment(LoginInfo actor,PaymentKind kind,long partyId,decimal amount,string? documentId,DateTime date,string note)
    {
        Authorized(actor,kind==PaymentKind.CustomerReceipt?"Sale":"Payment");
        long cents=Cents(amount);
        if(string.IsNullOrWhiteSpace(documentId))throw new BusinessException("اختر الفاتورة التي سيُخصص لها السداد.");
        using var tx=_db.BeginTransaction();
        CheckDate(tx,date);
        var target=Sql.Query(_db,tx,"SELECT * FROM Documents WHERE id=@d AND status='Posted'",("@d",documentId)).FirstOrDefault()
            ?? throw new BusinessException("الفاتورة غير موجودة أو غير مرحلة.");
        string expected=kind switch
        {
            PaymentKind.CustomerReceipt=>"Sale",PaymentKind.SupplierPayment=>"Purchase",
            PaymentKind.CustomerRefund=>"SalesReturn",_=>"PurchaseReturn"
        };
        if(target.Str("type")!=expected || target.Long("party_id")!=partyId)
            throw new BusinessException("جهة السداد لا تطابق نوع وطرف الفاتورة.");
        long due=Outstanding(tx,documentId!);
        if(cents>due)throw new BusinessException("المبلغ يتجاوز الرصيد القابل للسداد: "+Amounts.AsMoney(due));
        if(kind is PaymentKind.SupplierPayment or PaymentKind.CustomerRefund)
            if(CashBalance(tx)<cents)throw new BusinessException("رصيد الصندوق غير كافٍ لإتمام الصرف.");
        var legs=kind switch
        {
            PaymentKind.CustomerReceipt=>new List<EntryLeg>{Accounting.Dr(Accounting.Cash,cents),Accounting.Cr(Accounting.Receivable,cents,partyId)},
            PaymentKind.SupplierPayment=>new List<EntryLeg>{Accounting.Dr(Accounting.Payable,cents,partyId),Accounting.Cr(Accounting.Cash,cents)},
            PaymentKind.CustomerRefund=>new List<EntryLeg>{Accounting.Dr(Accounting.Receivable,cents,partyId),Accounting.Cr(Accounting.Cash,cents)},
            _=>new List<EntryLeg>{Accounting.Dr(Accounting.Cash,cents),Accounting.Cr(Accounting.Payable,cents,partyId)}
        };
        var id=Id(); string no=NextNumber(tx,"PAY",date);
        Sql.Exec(_db,tx,"""
            INSERT INTO Documents(id,doc_no,type,status,party_id,issued_on,payment_mode,total,posted_at,user_id)
            VALUES (@i,@n,'Payment','Posted',@p,@d,'Cash',@a,@t,@u)
            """,("@i",id),("@n",no),("@p",partyId),("@d",Day(date)),("@a",cents),("@t",Now()),("@u",actor.UserId));
        InsertJournal(tx,"DOC:"+id,Day(date),kind+" "+no+" "+note,actor.UserId,legs);
        var pay=Id();
        Sql.Exec(_db,tx,"INSERT INTO Payments(id,doc_id,kind,party_id,amount,created_at) VALUES (@p,@d,@k,@party,@a,@t)",
            ("@p",pay),("@d",id),("@k",kind.ToString()),("@party",partyId),("@a",cents),("@t",Now()));
        Sql.Exec(_db,tx,"INSERT INTO Allocations(payment_id,invoice_id,amount) VALUES (@p,@i,@a)",
            ("@p",pay),("@i",documentId),("@a",cents));
        AuditEvent(tx,actor.UserId,"PaymentPosted",no+":"+kind+":"+Amounts.AsMoney(cents));
        tx.Commit();
    }
}


