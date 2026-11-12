using Microsoft.Data.Sqlite;
namespace Wahib.Infrastructure;

internal static class Schema
{
    internal const int Version = 1;
    private const string Ddl = """
    CREATE TABLE IF NOT EXISTS Settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS Users(
      id INTEGER PRIMARY KEY, username TEXT NOT NULL UNIQUE COLLATE NOCASE,
      salt BLOB NOT NULL,password_hash BLOB NOT NULL,role TEXT NOT NULL,
      enabled INTEGER NOT NULL DEFAULT 1,failed_attempts INTEGER NOT NULL DEFAULT 0,
      lock_until TEXT NOT NULL DEFAULT '');
    CREATE TABLE IF NOT EXISTS Accounts(
      code TEXT PRIMARY KEY,name TEXT NOT NULL,kind TEXT NOT NULL,
      postable INTEGER NOT NULL DEFAULT 1,active INTEGER NOT NULL DEFAULT 1);
    CREATE TABLE IF NOT EXISTS Parties(
      id INTEGER PRIMARY KEY,name TEXT NOT NULL,type TEXT NOT NULL CHECK(type IN ('Customer','Supplier','Both')),
      phone TEXT NOT NULL DEFAULT '',active INTEGER NOT NULL DEFAULT 1);
    CREATE TABLE IF NOT EXISTS Items(
      id INTEGER PRIMARY KEY,sku TEXT NOT NULL UNIQUE,name TEXT NOT NULL,
      barcode TEXT NOT NULL DEFAULT '',unit TEXT NOT NULL DEFAULT 'حبة',
      sell_price INTEGER NOT NULL DEFAULT 0 CHECK(sell_price>=0),
      reorder_qty INTEGER NOT NULL DEFAULT 0 CHECK(reorder_qty>=0),
      active INTEGER NOT NULL DEFAULT 1);
    CREATE UNIQUE INDEX IF NOT EXISTS ux_items_barcode ON Items(barcode) WHERE barcode<>'';
    CREATE TABLE IF NOT EXISTS Documents(
      id TEXT PRIMARY KEY,doc_no TEXT NOT NULL UNIQUE,type TEXT NOT NULL,
      status TEXT NOT NULL CHECK(status IN ('Draft','Posted')),
      party_id INTEGER NOT NULL REFERENCES Parties(id) ON DELETE RESTRICT,
      origin_id TEXT REFERENCES Documents(id) ON DELETE RESTRICT,
      issued_on TEXT NOT NULL,payment_mode TEXT NOT NULL CHECK(payment_mode IN ('Cash','Credit')),
      supplier_ref TEXT, total INTEGER NOT NULL DEFAULT 0 CHECK(total>=0),
      posted_at TEXT,user_id INTEGER NOT NULL REFERENCES Users(id));
    CREATE UNIQUE INDEX IF NOT EXISTS ux_supplier_number ON Documents(party_id,supplier_ref)
      WHERE type='Purchase' AND supplier_ref IS NOT NULL AND supplier_ref<>'';
    CREATE TABLE IF NOT EXISTS DocumentLines(
      id INTEGER PRIMARY KEY,document_id TEXT NOT NULL REFERENCES Documents(id) ON DELETE RESTRICT,
      item_id INTEGER NOT NULL REFERENCES Items(id) ON DELETE RESTRICT,
      qty INTEGER NOT NULL CHECK(qty>0),price INTEGER NOT NULL CHECK(price>=0),
      amount INTEGER NOT NULL CHECK(amount>=0),cost INTEGER NOT NULL DEFAULT 0 CHECK(cost>=0),
      origin_line_id INTEGER REFERENCES DocumentLines(id) ON DELETE RESTRICT);
    CREATE INDEX IF NOT EXISTS idx_doclines_doc ON DocumentLines(document_id);
    CREATE TABLE IF NOT EXISTS StockMovements(
      id INTEGER PRIMARY KEY,document_id TEXT NOT NULL REFERENCES Documents(id),
      line_id INTEGER REFERENCES DocumentLines(id),item_id INTEGER NOT NULL REFERENCES Items(id),
      qty INTEGER NOT NULL CHECK(qty<>0),value INTEGER NOT NULL,issued_on TEXT NOT NULL,
      created_at TEXT NOT NULL);
    CREATE INDEX IF NOT EXISTS idx_stock_item ON StockMovements(item_id,issued_on);
    CREATE TABLE IF NOT EXISTS JournalEntries(
      id TEXT PRIMARY KEY,source_id TEXT NOT NULL UNIQUE,entry_date TEXT NOT NULL,
      description TEXT NOT NULL,user_id INTEGER NOT NULL REFERENCES Users(id),
      created_at TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS JournalLines(
      id INTEGER PRIMARY KEY,entry_id TEXT NOT NULL REFERENCES JournalEntries(id) ON DELETE RESTRICT,
      account_code TEXT NOT NULL REFERENCES Accounts(code) ON DELETE RESTRICT,
      party_id INTEGER REFERENCES Parties(id) ON DELETE RESTRICT,
      debit INTEGER NOT NULL DEFAULT 0 CHECK(debit>=0),
      credit INTEGER NOT NULL DEFAULT 0 CHECK(credit>=0),
      CHECK ((debit>0 AND credit=0) OR (credit>0 AND debit=0)));
    CREATE INDEX IF NOT EXISTS idx_gl_account ON JournalLines(account_code,entry_id);
    CREATE INDEX IF NOT EXISTS idx_gl_party ON JournalLines(party_id,account_code);
    CREATE TABLE IF NOT EXISTS Payments(
      id TEXT PRIMARY KEY,doc_id TEXT UNIQUE NOT NULL REFERENCES Documents(id) ON DELETE RESTRICT,
      kind TEXT NOT NULL,party_id INTEGER NOT NULL REFERENCES Parties(id),
      amount INTEGER NOT NULL CHECK(amount>0),created_at TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS Allocations(
      payment_id TEXT NOT NULL REFERENCES Payments(id) ON DELETE RESTRICT,
      invoice_id TEXT NOT NULL REFERENCES Documents(id) ON DELETE RESTRICT,
      amount INTEGER NOT NULL CHECK(amount>0),
      PRIMARY KEY(payment_id,invoice_id));
    CREATE TABLE IF NOT EXISTS ReturnApplications(
      return_id TEXT PRIMARY KEY REFERENCES Documents(id) ON DELETE RESTRICT,
      origin_id TEXT NOT NULL REFERENCES Documents(id) ON DELETE RESTRICT,
      amount INTEGER NOT NULL CHECK(amount>=0));
    CREATE TABLE IF NOT EXISTS PeriodLocks(period TEXT PRIMARY KEY,closed INTEGER NOT NULL CHECK(closed IN (0,1)));
    CREATE TABLE IF NOT EXISTS SequenceCounters(prefix TEXT PRIMARY KEY,last_number INTEGER NOT NULL DEFAULT 0);
    CREATE TABLE IF NOT EXISTS AuditEvents(
      seq INTEGER PRIMARY KEY AUTOINCREMENT,created_at TEXT NOT NULL,
      actor_id INTEGER,actor TEXT NOT NULL,action TEXT NOT NULL,detail TEXT NOT NULL,
      previous_hash TEXT NOT NULL,event_hash TEXT NOT NULL);
    CREATE INDEX IF NOT EXISTS idx_documents_party ON Documents(party_id,status,issued_on);
    CREATE INDEX IF NOT EXISTS idx_journal_date ON JournalEntries(entry_date);
    """;

    internal static void Initialize(SqliteConnection db,string name,string currency)
    {
        using var tx=db.BeginTransaction();
        Sql.Exec(db,tx,Ddl);
        Sql.Exec(db,tx,"INSERT OR IGNORE INTO Settings(key,value) VALUES ('schema_version',@v),('company_name',@n),('currency',@c)",
            ("@v",Version.ToString()),("@n",name),("@c",currency));
        foreach(var (code,n,kind) in Accounts)
            Sql.Exec(db,tx,"INSERT OR IGNORE INTO Accounts(code,name,kind) VALUES (@c,@n,@k)",("@c",code),("@n",n),("@k",kind));
        tx.Commit();
    }
    internal static void Check(SqliteConnection db)
    {
        var schema=Sql.Text(db,null,"SELECT value FROM Settings WHERE key='schema_version'");
        if(schema!=Version.ToString()) throw new InvalidOperationException($"إصدار قاعدة البيانات {schema} لا يطابق إصدار البرنامج {Version}. لا تُجرى هجرة تلقائية.");
        var check=Sql.Text(db,null,"PRAGMA quick_check");
        if(check!="ok") throw new InvalidOperationException("فشل فحص سلامة قاعدة البيانات: "+check);
    }
    internal static readonly (string Code,string Name,string Kind)[] Accounts =
    [
        ("1010","الصندوق","Asset"),("1020","البنك","Asset"),
        ("1200","مخزون البضائع","Asset"),("1300","ذمم العملاء","Asset"),
        ("2100","ذمم الموردين","Liability"),("3100","رأس المال","Equity"),
        ("4100","المبيعات","Income"),("4110","مردودات المبيعات","ContraIncome"),
        ("4190","فروق أسعار المشتريات","Income"),
        ("5100","تكلفة البضاعة المباعة","Expense"),("5200","فروق الجرد","Expense"),
        ("5300","مصروفات عامة","Expense")
    ];
}
