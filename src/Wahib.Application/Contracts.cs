using Wahib.Domain;
namespace Wahib.Application;

public record CompanyInfo(string Id, string Name, string Currency, string DatabasePath);
public record LoginInfo(long UserId, string Name, UserRole Role);
public record ItemInfo(long Id, string Sku, string Name, string Barcode, decimal Quantity, decimal Value, decimal Price, decimal Reorder, string Unit);
public record PartyInfo(long Id, string Name, string Type, string Phone, decimal Balance);
public record DocumentInfo(string Id, string Number, string Type, string Status, string Party, string Date, decimal Total, decimal Outstanding);
public record LineDraft(long ItemId, decimal Quantity, decimal UnitPrice, long? OriginLineId = null);
public record DocumentDraft(DocumentKind Kind, long PartyId, DateTime Date, string PaymentMode, string? SupplierReference, string? OriginId, IReadOnlyList<LineDraft> Lines);
public record LedgerRow(string Date, string EntryId, string Account, string AccountName, string Description, decimal Debit, decimal Credit);
public record JournalRow(string Account, decimal Debit, decimal Credit, long? PartyId = null);
public record ReportRow(string Label, decimal First, decimal Second);
public record AuditRow(string When, string Actor, string Action, string Detail, bool ChainValid);
public record SummaryInfo(decimal Sales, decimal Purchases, decimal Cash, decimal Inventory, decimal CustomersDue, decimal SuppliersDue, decimal Profit);

public interface IErpGateway : IDisposable
{
    CompanyInfo Company { get; }
    LoginInfo Login(string username, string password);
    void AddUser(LoginInfo actor, string username, string password, UserRole role);
    IReadOnlyList<(string Username,string Role,bool Enabled)> Users(LoginInfo actor);
    IReadOnlyList<ItemInfo> Items();
    long AddItem(LoginInfo actor,string sku,string name,string barcode,string unit,decimal sellPrice,decimal reorder);
    IReadOnlyList<PartyInfo> Parties();
    long AddParty(LoginInfo actor,string name,string type,string phone);
    IReadOnlyList<DocumentInfo> Documents();
    string CreateDocument(LoginInfo actor, DocumentDraft draft);
    void PostDocument(LoginInfo actor,string documentId);
    IReadOnlyList<(long LineId,long ItemId,string Name,decimal Quantity,decimal UnitPrice,decimal Returned)> DocumentLines(string documentId);
    void AdjustStock(LoginInfo actor,long itemId,decimal counted,DateTime date,string reason);
    void MakePayment(LoginInfo actor,PaymentKind kind,long partyId,decimal amount,string? documentId,DateTime date,string note);
    void PostJournal(LoginInfo actor,DateTime date,string explanation,IReadOnlyList<JournalRow> lines);
    IReadOnlyList<(string Code,string Name,string Kind)> Accounts();
    IReadOnlyList<LedgerRow> Ledger(DateTime from,DateTime to);
    IReadOnlyList<ReportRow> TrialBalance(DateTime to);
    IReadOnlyList<ReportRow> IncomeStatement(DateTime from,DateTime to);
    IReadOnlyList<ReportRow> BalanceSheet(DateTime to);
    SummaryInfo Summary();
    IReadOnlyList<AuditRow> Audit(LoginInfo actor);
    IReadOnlyList<(string Period,bool Closed)> Periods();
    void SetPeriod(LoginInfo actor,string period,bool close);
    string VerifyIntegrity();
    string Backup(LoginInfo actor,string destination);
    void Restore(LoginInfo actor,string source);
}
public interface ICompanyStore
{
    IReadOnlyList<CompanyInfo> Companies();
    CompanyInfo Create(string name,string currency,string ownerPassword);
    IErpGateway Open(string companyId);
}
