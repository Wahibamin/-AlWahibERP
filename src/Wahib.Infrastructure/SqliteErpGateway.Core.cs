using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Infrastructure;

public sealed partial class SqliteErpGateway : IErpGateway
{
    private readonly SqliteConnection _db;
    public CompanyInfo Company { get; }
    public SqliteErpGateway(CompanyInfo company)
    {
        Company=company;
        _db=Sql.Connect(company.DatabasePath);
        Schema.Check(_db);
        Sql.Exec(_db,null,"INSERT OR IGNORE INTO Parties(id,name,type) VALUES (1,'حساب نظام داخلي','Both')");
    }
    public void Dispose() => _db.Dispose();

    private static string Day(DateTime dt) => dt.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    private static string Now() => DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
    private static string Id() => Guid.NewGuid().ToString("N");
    private static long Cents(decimal v)
    {
        if(v<=0) throw new BusinessException("المبلغ يجب أن يكون أكبر من الصفر.");
        return Amounts.Money(v);
    }
    private UserRole Authorized(LoginInfo actor,string action,SqliteTransaction? transaction=null)
    {
        var r=Sql.Query(_db,transaction,"SELECT role,enabled FROM Users WHERE id=@u",("@u",actor.UserId)).FirstOrDefault();
        if(r is null || !r.Bool("enabled")) throw new BusinessException("انتهت صلاحية المستخدم.");
        var role=Enum.Parse<UserRole>(r.Str("role"));
        Permissions.Require(role,action);
        return role;
    }
    private void CheckDate(SqliteTransaction tx,DateTime date)
    {
        if(date.Year<2000 || date.Year>2100) throw new BusinessException("تاريخ المستند خارج المدى المسموح.");
        if(date.Date>DateTime.Today) throw new BusinessException("لا يسمح بالترحيل المحاسبي بتاريخ مستقبلي.");
        string p=date.ToString("yyyy-MM",CultureInfo.InvariantCulture);
        if(Sql.Number(_db,tx,"SELECT COALESCE(MAX(closed),0) FROM PeriodLocks WHERE period=@p",("@p",p))==1)
            throw new BusinessException("الفترة المالية مغلقة: "+p);
    }
    private void CheckChronology(SqliteTransaction tx,long itemId,DateTime date)
    {
        string last=Sql.Text(_db,tx,"SELECT COALESCE(MAX(issued_on),'') FROM StockMovements WHERE item_id=@i",("@i",itemId));
        if(string.CompareOrdinal(last,Day(date))>0)
            throw new BusinessException("لا يسمح بترحيل حركة مخزون بتاريخ أقدم من آخر حركة للصنف؛ يلزم مسار إعادة تقييم محاسبي.");
    }
    private StockSnapshot Stock(SqliteTransaction? tx,long itemId)
    {
        var r=Sql.Query(_db,tx,"SELECT COALESCE(SUM(qty),0) q,COALESCE(SUM(value),0) v FROM StockMovements WHERE item_id=@i",("@i",itemId))[0];
        return new StockSnapshot(r.Long("q"),r.Long("v"));
    }
    private string InsertJournal(SqliteTransaction tx,string source,string date,string description,long userId,List<EntryLeg> legs)
    {
        Amounts.Balanced(legs);
        foreach(var leg in legs)
        {
            if(Sql.Number(_db,tx,"SELECT COUNT(*) FROM Accounts WHERE code=@c AND active=1 AND postable=1",("@c",leg.Account))==0)
                throw new BusinessException("الحساب غير صالح للترحيل: "+leg.Account);
            if((leg.Account==Accounting.Receivable || leg.Account==Accounting.Payable) && leg.PartyId is null)
                throw new BusinessException("حساب الذمم يتطلب تعيين طرف.");
        }
        var id=Id();
        Sql.Exec(_db,tx,"INSERT INTO JournalEntries(id,source_id,entry_date,description,user_id,created_at) VALUES (@i,@s,@d,@n,@u,@t)",
            ("@i",id),("@s",source),("@d",date),("@n",description),("@u",userId),("@t",Now()));
        foreach(var leg in legs)
            Sql.Exec(_db,tx,"INSERT INTO JournalLines(entry_id,account_code,party_id,debit,credit) VALUES (@e,@a,@p,@d,@c)",
                ("@e",id),("@a",leg.Account),("@p",leg.PartyId),("@d",leg.Debit),("@c",leg.Credit));
        return id;
    }
    private void Movement(SqliteTransaction tx,string doc,long? line,long item,long quantity,long value,DateTime date)
    {
        if(quantity==0) throw new BusinessException("حركة المخزون لا يمكن أن تكون صفرية.");
        Sql.Exec(_db,tx,"INSERT INTO StockMovements(document_id,line_id,item_id,qty,value,issued_on,created_at) VALUES (@d,@l,@i,@q,@v,@dt,@t)",
            ("@d",doc),("@l",line),("@i",item),("@q",quantity),("@v",value),("@dt",Day(date)),("@t",Now()));
    }
    private string NextNumber(SqliteTransaction tx,string prefix,DateTime date)
    {
        string key=prefix+"-"+date.ToString("yyyy",CultureInfo.InvariantCulture);
        Sql.Exec(_db,tx,"INSERT OR IGNORE INTO SequenceCounters(prefix,last_number) VALUES (@p,0)",("@p",key));
        Sql.Exec(_db,tx,"UPDATE SequenceCounters SET last_number=last_number+1 WHERE prefix=@p",("@p",key));
        long n=Sql.Number(_db,tx,"SELECT last_number FROM SequenceCounters WHERE prefix=@p",("@p",key));
        return key+"-"+n.ToString("D6",CultureInfo.InvariantCulture);
    }
    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private void AuditEvent(SqliteTransaction tx,long actorId,string action,string detail)
    {
        var r=Sql.Query(_db,tx,"SELECT seq,event_hash FROM AuditEvents ORDER BY seq DESC LIMIT 1").FirstOrDefault();
        long seq=(r?.Long("seq")??0)+1;
        string prev=r?.Str("event_hash")??new string('0',64);
        string stamp=Now();
        string actor=Sql.Text(_db,tx,"SELECT username FROM Users WHERE id=@u",("@u",actorId));
        string payload=$"{seq}\n{stamp}\n{actorId}\n{actor}\n{action}\n{detail}\n{prev}";
        string hash=Sha(payload);
        Sql.Exec(_db,tx,"INSERT INTO AuditEvents(created_at,actor_id,actor,action,detail,previous_hash,event_hash) VALUES (@t,@u,@n,@a,@d,@p,@h)",
            ("@t",stamp),("@u",actorId),("@n",actor),("@a",action),("@d",detail),("@p",prev),("@h",hash));
    }
    public IReadOnlyList<AuditRow> Audit(LoginInfo actor)
    {
        Authorized(actor,"Users");
        var records=Sql.Query(_db,null,"SELECT * FROM AuditEvents ORDER BY seq");
        var output=new List<AuditRow>(); string prev=new string('0',64);long seq=0;
        foreach(var r in records)
        {
            seq++;
            string expected=Sha($"{seq}\n{r.Str("created_at")}\n{r.Long("actor_id")}\n{r.Str("actor")}\n{r.Str("action")}\n{r.Str("detail")}\n{r.Str("previous_hash")}");
            bool ok = r.Long("seq")==seq && r.Str("previous_hash")==prev && r.Str("event_hash")==expected;
            output.Add(new AuditRow(r.Str("created_at"),r.Str("actor"),r.Str("action"),r.Str("detail"),ok));
            prev=r.Str("event_hash");
        }
        return output.OrderByDescending(x=>x.When).ToArray();
    }
    public LoginInfo Login(string username,string password)
    {
        var row=Sql.Query(_db,null,"SELECT * FROM Users WHERE username=@u COLLATE NOCASE",("@u",username.Trim())).FirstOrDefault();
        if(row is null) { Passwords.Verify(password,new byte[16],new byte[32]); throw new BusinessException("المستخدم أو كلمة المرور غير صحيحة."); }
        if(!row.Bool("enabled"))throw new BusinessException("هذا المستخدم معطل.");
        var until=row.Str("lock_until");
        if(DateTimeOffset.TryParse(until,out var t) && t>DateTimeOffset.UtcNow)
            throw new BusinessException("تم إيقاف المحاولات مؤقتًا. حاول لاحقًا.");
        var ok=Passwords.Verify(password,(byte[])GetBlob("salt",row.Long("id")),(byte[])GetBlob("password_hash",row.Long("id")));
        if(!ok)
        {
            long fails=row.Long("failed_attempts")+1;
            string lockUntil=fails>=5?DateTimeOffset.UtcNow.AddMinutes(2).ToString("O"):"";
            Sql.Exec(_db,null,"UPDATE Users SET failed_attempts=@f,lock_until=@l WHERE id=@i",("@f",fails),("@l",lockUntil),("@i",row.Long("id")));
            throw new BusinessException("المستخدم أو كلمة المرور غير صحيحة.");
        }
        Sql.Exec(_db,null,"UPDATE Users SET failed_attempts=0,lock_until='' WHERE id=@i",("@i",row.Long("id")));
        return new LoginInfo(row.Long("id"),row.Str("username"),Enum.Parse<UserRole>(row.Str("role")));
    }
    private object GetBlob(string column,long id)
    {
        if(column is not ("salt" or "password_hash"))throw new InvalidOperationException();
        return Sql.Scalar(_db,null,$"SELECT {column} FROM Users WHERE id=@i",("@i",id))!;
    }
    public void AddUser(LoginInfo actor,string username,string password,UserRole role)
    {
        Authorized(actor,"Users");
        username=username.Trim();
        if(username.Length<3) throw new BusinessException("اسم المستخدم قصير.");
        var (salt,hash)=Passwords.Hash(password);
        using var tx=_db.BeginTransaction();
        Sql.Exec(_db,tx,"INSERT INTO Users(username,salt,password_hash,role) VALUES (@u,@s,@h,@r)",
            ("@u",username),("@s",salt),("@h",hash),("@r",role.ToString()));
        AuditEvent(tx,actor.UserId,"UserCreated",username+":"+role);
        tx.Commit();
    }
    public IReadOnlyList<(string Username,string Role,bool Enabled)> Users(LoginInfo actor)
    {
        Authorized(actor,"Users");
        return Sql.Query(_db,null,"SELECT username,role,enabled FROM Users ORDER BY username")
            .Select(r=>(r.Str("username"),r.Str("role"),r.Bool("enabled"))).ToArray();
    }
    public IReadOnlyList<(string Code,string Name,string Kind)> Accounts() =>
        Sql.Query(_db,null,"SELECT code,name,kind FROM Accounts ORDER BY code")
            .Select(r=>(r.Str("code"),r.Str("name"),r.Str("kind"))).ToArray();
    public IReadOnlyList<ItemInfo> Items() => Sql.Query(_db,null,"""
        SELECT i.*,COALESCE(SUM(s.qty),0) q,COALESCE(SUM(s.value),0) v
        FROM Items i LEFT JOIN StockMovements s ON s.item_id=i.id
        WHERE i.active=1 GROUP BY i.id ORDER BY i.name
        """).Select(r=>new ItemInfo(r.Long("id"),r.Str("sku"),r.Str("name"),r.Str("barcode"),
        Amounts.AsQuantity(r.Long("q")),Amounts.AsMoney(r.Long("v")),Amounts.AsMoney(r.Long("sell_price")),
        Amounts.AsQuantity(r.Long("reorder_qty")),r.Str("unit"))).ToArray();
    public long AddItem(LoginInfo actor,string sku,string name,string barcode,string unit,decimal sellPrice,decimal reorder)
    {
        Authorized(actor,"Item");
        if(string.IsNullOrWhiteSpace(sku)||string.IsNullOrWhiteSpace(name))throw new BusinessException("رمز واسم الصنف إلزاميان.");
        if(sellPrice<0||reorder<0)throw new BusinessException("السعر وحد الطلب لا يجوز أن يكونا سالبين.");
        using var tx=_db.BeginTransaction();
        Sql.Exec(_db,tx,"INSERT INTO Items(sku,name,barcode,unit,sell_price,reorder_qty) VALUES (@s,@n,@b,@u,@p,@r)",
            ("@s",sku.Trim()),("@n",name.Trim()),("@b",barcode.Trim()),("@u",unit.Trim()),
            ("@p",Amounts.Money(sellPrice)),("@r",Amounts.SignedQuantity(reorder)));
        long id=Sql.Number(_db,tx,"SELECT last_insert_rowid()");
        AuditEvent(tx,actor.UserId,"ItemCreated",sku.Trim()); tx.Commit(); return id;
    }
    public IReadOnlyList<PartyInfo> Parties() => Sql.Query(_db,null,"""
       SELECT p.id,p.name,p.type,p.phone,
       COALESCE((SELECT SUM(l.debit-l.credit) FROM JournalLines l JOIN JournalEntries j ON j.id=l.entry_id
       WHERE l.party_id=p.id AND l.account_code='1300'),0) customer_balance,
       COALESCE((SELECT SUM(l.credit-l.debit) FROM JournalLines l JOIN JournalEntries j ON j.id=l.entry_id
       WHERE l.party_id=p.id AND l.account_code='2100'),0) supplier_balance
       FROM Parties p WHERE p.id<>1 AND p.active=1 ORDER BY p.name
       """).Select(r=>new PartyInfo(r.Long("id"),r.Str("name"),r.Str("type"),r.Str("phone"),
       Amounts.AsMoney(checked(r.Long("customer_balance")-r.Long("supplier_balance"))))).ToArray();
    public long AddParty(LoginInfo actor,string name,string type,string phone)
    {
        Authorized(actor,"Party");
        if(string.IsNullOrWhiteSpace(name)||type is not ("Customer" or "Supplier" or "Both"))throw new BusinessException("بيانات الطرف غير صحيحة.");
        using var tx=_db.BeginTransaction();
        Sql.Exec(_db,tx,"INSERT INTO Parties(name,type,phone) VALUES (@n,@t,@p)",
            ("@n",name.Trim()),("@t",type),("@p",phone.Trim()));
        long id=Sql.Number(_db,tx,"SELECT last_insert_rowid()");
        AuditEvent(tx,actor.UserId,"PartyCreated",name.Trim());tx.Commit();return id;
    }
    public IReadOnlyList<(string Period,bool Closed)> Periods()
    {
        return Sql.Query(_db,null,"SELECT period,closed FROM PeriodLocks ORDER BY period DESC")
            .Select(r=>(r.Str("period"),r.Bool("closed"))).ToArray();
    }
    public void SetPeriod(LoginInfo actor,string period,bool close)
    {
        Authorized(actor,"Period");
        if(!DateTime.TryParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw new BusinessException("الفترة بالشكل YYYY-MM.");
        using var tx=_db.BeginTransaction();
        Sql.Exec(_db,tx,"INSERT INTO PeriodLocks(period,closed) VALUES (@p,@c) ON CONFLICT(period) DO UPDATE SET closed=excluded.closed",
            ("@p",period),("@c",close?1:0));
        AuditEvent(tx,actor.UserId,close?"PeriodClosed":"PeriodReopened",period);tx.Commit();
    }
}
