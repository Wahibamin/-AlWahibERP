using Microsoft.Data.Sqlite;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Infrastructure;

public sealed partial class SqliteErpGateway
{
    private static string KindName(DocumentKind kind) => kind switch
    {
        DocumentKind.Sale=>"Sale",DocumentKind.Purchase=>"Purchase",
        DocumentKind.SalesReturn=>"SalesReturn",_=>"PurchaseReturn"
    };
    private static string Prefix(string kind) => kind switch
    {
        "Sale"=>"SAL","Purchase"=>"PUR","SalesReturn"=>"SRT", "PurchaseReturn"=>"PRT",
        "ADJUST"=>"ADJ","Payment"=>"PAY",_=>"DOC"
    };
    private static bool IsCustomer(string type) => type is "Sale" or "SalesReturn";
    private static bool IsReturn(string type) => type is "SalesReturn" or "PurchaseReturn";
    private long Outstanding(SqliteTransaction? tx,string docId)
    {
        var r=Sql.Query(_db,tx,"SELECT type,total FROM Documents WHERE id=@i AND status='Posted'",("@i",docId)).FirstOrDefault();
        if(r is null)return 0;
        long paid=Sql.Number(_db,tx,"SELECT COALESCE(SUM(amount),0) FROM Allocations WHERE invoice_id=@i",("@i",docId));
        long credit=IsReturn(r.Str("type"))
            ? Sql.Number(_db,tx,"SELECT COALESCE(SUM(amount),0) FROM ReturnApplications WHERE return_id=@i",("@i",docId))
            : Sql.Number(_db,tx,"SELECT COALESCE(SUM(amount),0) FROM ReturnApplications WHERE origin_id=@i",("@i",docId));
        return checked(r.Long("total")-paid-credit);
    }
    public IReadOnlyList<DocumentInfo> Documents() => Sql.Query(_db,null,"""
       SELECT d.id,d.doc_no,d.type,d.status,d.issued_on,d.total,COALESCE(p.name,'') party
       FROM Documents d LEFT JOIN Parties p ON p.id=d.party_id ORDER BY d.issued_on DESC,d.rowid DESC LIMIT 2000
       """).Select(r=>new DocumentInfo(r.Str("id"),r.Str("doc_no"),r.Str("type"),r.Str("status"),
            r.Str("party"),r.Str("issued_on"),Amounts.AsMoney(r.Long("total")),
            Amounts.AsMoney(Outstanding(null,r.Str("id"))))).ToArray();
    public IReadOnlyList<(long LineId,long ItemId,string Name,decimal Quantity,decimal UnitPrice,decimal Returned)> DocumentLines(string documentId)
    {
        return Sql.Query(_db,null,"""
          SELECT dl.*,i.name,
          COALESCE((SELECT SUM(r.qty) FROM DocumentLines r JOIN Documents rd ON rd.id=r.document_id
          WHERE r.origin_line_id=dl.id AND rd.status='Posted'),0) returned
          FROM DocumentLines dl JOIN Items i ON i.id=dl.item_id WHERE dl.document_id=@d ORDER BY dl.id
          """,("@d",documentId))
          .Select(r=>(r.Long("id"),r.Long("item_id"),r.Str("name"),Amounts.AsQuantity(r.Long("qty")),
          Amounts.AsMoney(r.Long("price")),Amounts.AsQuantity(r.Long("returned")))).ToArray();
    }
    public string CreateDocument(LoginInfo actor,DocumentDraft draft)
    {
        var type=KindName(draft.Kind);
        Authorized(actor,IsCustomer(type)?"Sale":"Purchase");
        if(draft.Lines.Count==0)throw new BusinessException("الفاتورة تحتاج إلى صنف واحد على الأقل.");
        if(draft.Lines.Count>250)throw new BusinessException("الحد الأقصى 250 سطرًا.");
        if(draft.PaymentMode is not ("Cash" or "Credit"))throw new BusinessException("طريقة الدفع غير صحيحة.");
        if(IsReturn(type) && string.IsNullOrWhiteSpace(draft.OriginId))throw new BusinessException("المرتجع يحتاج فاتورة أصلية.");
        string id=Id();
        using var tx=_db.BeginTransaction();
        var party=Sql.Query(_db,tx,"SELECT type,active FROM Parties WHERE id=@p",("@p",draft.PartyId)).FirstOrDefault();
        if(party is null || !party.Bool("active") || draft.PartyId==1) throw new BusinessException("اختر عميلًا أو موردًا صحيحًا.");
        if(IsCustomer(type) && party.Str("type")=="Supplier" || !IsCustomer(type) && party.Str("type")=="Customer")
            throw new BusinessException("نوع الطرف لا يناسب نوع الفاتورة.");
        if(IsReturn(type))
        {
            var origin=Sql.Query(_db,tx,"SELECT type,party_id,status FROM Documents WHERE id=@i",("@i",draft.OriginId)).FirstOrDefault();
            string expected=type=="SalesReturn"?"Sale":"Purchase";
            if(origin is null||origin.Str("type")!=expected||origin.Str("status")!="Posted"||origin.Long("party_id")!=draft.PartyId)
                throw new BusinessException("فاتورة الأصل لا تتوافق مع المرتجع.");
        }
        else if(draft.OriginId is not null)throw new BusinessException("مرجع الأصل متاح للمرتجعات فقط.");
        Sql.Exec(_db,tx,"""
             INSERT INTO Documents(id,doc_no,type,status,party_id,origin_id,issued_on,payment_mode,supplier_ref,total,user_id)
             VALUES (@i,@n,@k,'Draft',@p,@o,@date,@m,@r,0,@u)
             """,("@i",id),("@n","DRAFT-"+id),("@k",type),("@p",draft.PartyId),
             ("@o",draft.OriginId),("@date",Day(draft.Date)),("@m",draft.PaymentMode),
             ("@r",string.IsNullOrWhiteSpace(draft.SupplierReference)?null:draft.SupplierReference.Trim()),("@u",actor.UserId));
        long total=0;
        foreach(var line in draft.Lines)
        {
            var item=Sql.Query(_db,tx,"SELECT active FROM Items WHERE id=@i",("@i",line.ItemId)).FirstOrDefault();
            if(item is null||!item.Bool("active"))throw new BusinessException("صنف غير موجود أو معطل.");
            long q=Amounts.Quantity(line.Quantity);
            long price=Amounts.Money(line.UnitPrice);
            if(price<0)throw new BusinessException("السعر لا يجوز أن يكون سالبًا.");
            if(IsReturn(type))
            {
                if(line.OriginLineId is null)throw new BusinessException("حدد السطر الأصلي للمرتجع.");
                var original=Sql.Query(_db,tx,"SELECT * FROM DocumentLines WHERE id=@l AND document_id=@d",("@l",line.OriginLineId),("@d",draft.OriginId)).FirstOrDefault();
                if(original is null || original.Long("item_id")!=line.ItemId)
                    throw new BusinessException("سطر المرتجع لا يطابق سطر الفاتورة الأصلية.");
                price=original.Long("price"); // historical price, cannot be overwritten
            }
            long sum=Amounts.LineTotal(price,q);
            total=checked(total+sum);
            Sql.Exec(_db,tx,"INSERT INTO DocumentLines(document_id,item_id,qty,price,amount,origin_line_id) VALUES (@d,@i,@q,@p,@a,@o)",
                ("@d",id),("@i",line.ItemId),("@q",q),("@p",price),("@a",sum),("@o",line.OriginLineId));
        }
        if(total<=0)throw new BusinessException("إجمالي الفاتورة يجب أن يكون موجبًا.");
        Sql.Exec(_db,tx,"UPDATE Documents SET total=@t WHERE id=@i",("@t",total),("@i",id));
        AuditEvent(tx,actor.UserId,"DocumentDraftCreated",type+":"+id); tx.Commit();
        return id;
    }
    public void PostDocument(LoginInfo actor,string documentId)
    {
        using var tx=_db.BeginTransaction();
        var doc=Sql.Query(_db,tx,"SELECT * FROM Documents WHERE id=@d",("@d",documentId)).FirstOrDefault()
            ?? throw new BusinessException("الفاتورة غير موجودة.");
        string type=doc.Str("type");
        Authorized(actor,IsCustomer(type)?"Sale":"Purchase",tx);
        if(doc.Str("status")=="Posted") {tx.Commit(); return;} // idempotent
        if(doc.Str("status")!="Draft" || type is not ("Sale" or "Purchase" or "SalesReturn" or "PurchaseReturn"))
            throw new BusinessException("حالة الفاتورة غير صالحة.");
        DateTime date=DateTime.ParseExact(doc.Str("issued_on"),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        CheckDate(tx,date);
        long party=doc.Long("party_id"), amount=doc.Long("total");
        var lines=Sql.Query(_db,tx,"SELECT * FROM DocumentLines WHERE document_id=@i ORDER BY id",("@i",documentId));
        if(lines.Count==0)throw new BusinessException("المستند بلا بنود.");
        string originValue=doc.Str("origin_id");
        string? originId=originValue.Length>0?originValue:null;
        if(IsReturn(type))
        {
            var origin=Sql.Query(_db,tx,"SELECT type,party_id,status FROM Documents WHERE id=@i",("@i",originId)).FirstOrDefault();
            if(origin is null || origin.Str("status")!="Posted" || origin.Long("party_id")!=party ||
                 origin.Str("type")!=(type=="SalesReturn"?"Sale":"Purchase"))
                throw new BusinessException("الفاتورة الأصلية غير صحيحة.");
        }
        if(IsReturn(type))
        {
            long adjustedTotal=0;
            var quantityInThisDraft=new Dictionary<long,long>();
            var amountsInThisDraft=new Dictionary<long,long>();
            foreach(var line in lines)
            {
                long originalLine=line.NullableLong("origin_line_id")??throw new BusinessException("المرتجع يحتاج سطرًا أصليًا.");
                var original=Sql.Query(_db,tx,"SELECT * FROM DocumentLines WHERE id=@l AND document_id=@o",("@l",originalLine),("@o",originId)).FirstOrDefault()
                    ?? throw new BusinessException("سطر المرتجع غير صالح.");
                if(original.Long("item_id")!=line.Long("item_id"))throw new BusinessException("الصنف لا يطابق الأصل.");
                long previousQty=Sql.Number(_db,tx,"""
                  SELECT COALESCE(SUM(dl.qty),0) FROM DocumentLines dl JOIN Documents d ON d.id=dl.document_id
                  WHERE dl.origin_line_id=@l AND d.status='Posted'
                  """,("@l",originalLine));
                long previousAmount=Sql.Number(_db,tx,"""
                  SELECT COALESCE(SUM(dl.amount),0) FROM DocumentLines dl JOIN Documents d ON d.id=dl.document_id
                  WHERE dl.origin_line_id=@l AND d.status='Posted'
                  """,("@l",originalLine));
                long alreadyQty=quantityInThisDraft.GetValueOrDefault(originalLine);
                long alreadyAmount=amountsInThisDraft.GetValueOrDefault(originalLine);
                long remaining=checked(original.Long("qty")-previousQty-alreadyQty);
                if(line.Long("qty")>remaining)throw new BusinessException("مجموع الكميات المرتجعة أكبر من الأصل.");
                long exactAmount=line.Long("qty")==remaining
                    ? checked(original.Long("amount")-previousAmount-alreadyAmount)
                    : Amounts.LineTotal(original.Long("price"),line.Long("qty"));
                if(exactAmount<0)throw new BusinessException("فارق تقريب في قيمة المرتجع.");
                quantityInThisDraft[originalLine]=checked(alreadyQty+line.Long("qty"));
                amountsInThisDraft[originalLine]=checked(alreadyAmount+exactAmount);
                adjustedTotal=checked(adjustedTotal+exactAmount);
                Sql.Exec(_db,tx,"UPDATE DocumentLines SET amount=@a WHERE id=@i",("@a",exactAmount),("@i",line.Long("id")));
            }
            amount=adjustedTotal;
            Sql.Exec(_db,tx,"UPDATE Documents SET total=@a WHERE id=@d",("@a",amount),("@d",documentId));
        }
        var legs=new List<EntryLeg>();
        if(type=="Sale") {legs.Add(Accounting.Dr(Accounting.Receivable,amount,party));legs.Add(Accounting.Cr(Accounting.Sales,amount));}
        if(type=="Purchase") {legs.Add(Accounting.Dr(Accounting.Inventory,amount));legs.Add(Accounting.Cr(Accounting.Payable,amount,party));}
        if(type=="SalesReturn") {legs.Add(Accounting.Dr(Accounting.SalesReturn,amount));legs.Add(Accounting.Cr(Accounting.Receivable,amount,party));}
        long stockValue=0;
        foreach(var line in lines)
        {
            long item=line.Long("item_id"),qty=line.Long("qty"),price=line.Long("price");
            CheckChronology(tx,item,date);
            var stock=Stock(tx,item);
            long movementQty=0,movementValue=0;
            if(type=="Purchase") { movementQty=qty; movementValue=line.Long("amount"); }
            else if(type=="Sale" || type=="PurchaseReturn")
            {
                if(stock.Quantity<qty)throw new BusinessException($"المخزون غير كافٍ للصنف رقم {item}: المتوفر {Amounts.AsQuantity(stock.Quantity)}");
                movementQty=-qty;
                movementValue=-Amounts.MovingCost(stock.Value,stock.Quantity,qty);
            }
            else
            {
                long? originLine=line.NullableLong("origin_line_id");
                if(originLine is null)throw new BusinessException("المرتجع لا يحتوي سطرًا أصليًا.");
                var orig=Sql.Query(_db,tx,"SELECT * FROM DocumentLines WHERE id=@l AND document_id=@d",("@l",originLine),("@d",originId)).FirstOrDefault();
                if(orig is null||orig.Long("item_id")!=item)throw new BusinessException("مرجع السطر الأصلي غير صالح.");
                long returned=Sql.Number(_db,tx,"""
                    SELECT COALESCE(SUM(dl.qty),0) FROM DocumentLines dl JOIN Documents d ON d.id=dl.document_id
                    WHERE d.status='Posted' AND dl.origin_line_id=@l
                    """,("@l",originLine));
                if(checked(returned+qty)>orig.Long("qty"))throw new BusinessException("كمية المرتجع تتجاوز المباعة/المشتراة.");
                // Split historical total without creating value on full return beyond original cost.
                long originalCost=orig.Long("cost");
                long historicalReturnedCost=Sql.Number(_db,tx,"""
                  SELECT COALESCE(SUM(dl.cost),0) FROM DocumentLines dl JOIN Documents d ON d.id=dl.document_id
                  WHERE d.status='Posted' AND dl.origin_line_id=@l
                  """,("@l",originLine));
                long cost=qty==orig.Long("qty")-returned
                    ? checked(originalCost-historicalReturnedCost)
                    : checked((long)decimal.Round((decimal)originalCost*qty/orig.Long("qty"),0,MidpointRounding.AwayFromZero));
                movementQty=qty;
                movementValue=cost;
            }
            if(type=="PurchaseReturn")
            {
                long? originLine=line.NullableLong("origin_line_id");
                if(originLine is null)throw new BusinessException("المرتجع يحتاج سطرًا أصليًا.");
                var orig=Sql.Query(_db,tx,"SELECT * FROM DocumentLines WHERE id=@l AND document_id=@d",("@l",originLine),("@d",originId)).FirstOrDefault();
                if(orig is null||orig.Long("item_id")!=item)throw new BusinessException("مرجع المرتجع غير صالح.");
                long returned=Sql.Number(_db,tx,"""
                  SELECT COALESCE(SUM(dl.qty),0) FROM DocumentLines dl JOIN Documents d ON d.id=dl.document_id
                  WHERE d.status='Posted' AND dl.origin_line_id=@l
                  """,("@l",originLine));
                if(checked(returned+qty)>orig.Long("qty"))throw new BusinessException("كمية مرتجع الشراء تتجاوز الأصل.");
            }
            Movement(tx,documentId,line.Long("id"),item,movementQty,movementValue,date);
            long costValue=checked(Math.Abs(movementValue));
            stockValue=checked(stockValue+costValue);
            if(type is "Sale" or "SalesReturn")
                Sql.Exec(_db,tx,"UPDATE DocumentLines SET cost=@c WHERE id=@l",("@c",costValue),("@l",line.Long("id")));
        }
        if(type=="Sale" && stockValue>0) {legs.Add(Accounting.Dr(Accounting.CostOfSales,stockValue));legs.Add(Accounting.Cr(Accounting.Inventory,stockValue));}
        if(type=="SalesReturn" && stockValue>0){legs.Add(Accounting.Dr(Accounting.Inventory,stockValue));legs.Add(Accounting.Cr(Accounting.CostOfSales,stockValue));}
        if(type=="PurchaseReturn")
        {
            legs.Add(Accounting.Dr(Accounting.Payable,amount,party));
            legs.Add(Accounting.Cr(Accounting.Inventory,stockValue));
            if(amount>stockValue)legs.Add(Accounting.Cr(Accounting.PurchaseVariance,checked(amount-stockValue)));
            if(amount<stockValue)legs.Add(Accounting.Dr(Accounting.PurchaseVariance,checked(stockValue-amount)));
        }
        if(doc.Str("payment_mode")=="Cash" && !IsReturn(type))
        {
            if(type=="Purchase" && CashBalance(tx)<amount)
                throw new BusinessException("رصيد الصندوق غير كافٍ لشراء نقدي.");
            if(type=="Sale") {legs.Add(Accounting.Dr(Accounting.Cash,amount)); legs.Add(Accounting.Cr(Accounting.Receivable,amount,party));}
            else {legs.Add(Accounting.Dr(Accounting.Payable,amount,party)); legs.Add(Accounting.Cr(Accounting.Cash,amount));}
        }
        string no=NextNumber(tx,Prefix(type),date);
        InsertJournal(tx,"DOC:"+documentId,Day(date),type+" "+no,actor.UserId,legs);
        Sql.Exec(_db,tx,"UPDATE Documents SET status='Posted',posted_at=@t,doc_no=@n WHERE id=@i",
            ("@t",Now()),("@n",no),("@i",documentId));
        if(doc.Str("payment_mode")=="Cash" && !IsReturn(type))
        {
            var payment=Id();
            Sql.Exec(_db,tx,"INSERT INTO Payments(id,doc_id,kind,party_id,amount,created_at) VALUES (@i,@d,@k,@p,@a,@t)",
                ("@i",payment),("@d",documentId),("@k",type=="Sale"?"CustomerReceipt":"SupplierPayment"),
                ("@p",party),("@a",amount),("@t",Now()));
            Sql.Exec(_db,tx,"INSERT INTO Allocations(payment_id,invoice_id,amount) VALUES (@p,@i,@a)",("@p",payment),("@i",documentId),("@a",amount));
        }
        if(IsReturn(type))
        {
            long outstanding=Outstanding(tx,originId!);
            long applied=Math.Min(Math.Max(outstanding,0),amount);
            Sql.Exec(_db,tx,"INSERT INTO ReturnApplications(return_id,origin_id,amount) VALUES (@r,@o,@a)",
               ("@r",documentId),("@o",originId),("@a",applied));
        }
        AuditEvent(tx,actor.UserId,"DocumentPosted",type+":"+no);
        tx.Commit();
    }
}
