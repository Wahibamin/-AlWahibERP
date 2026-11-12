namespace Wahib.Domain;

/// <summary>All ledger/stock-value columns are integer 1/10000 units; quantity is integer 1/10000 units.</summary>
public static class Amounts
{
    public const long MoneyScale = 10000;
    public const long QuantityScale = 10000;

    public static long Money(decimal value) => checked((long)decimal.Round(value * MoneyScale, 0, MidpointRounding.AwayFromZero));
    public static long Quantity(decimal value)
    {
        if (value <= 0) throw new BusinessException("يجب أن تكون الكمية أكبر من الصفر.");
        return checked((long)decimal.Round(value * QuantityScale, 0, MidpointRounding.AwayFromZero));
    }
    public static long SignedQuantity(decimal value) => checked((long)decimal.Round(value * QuantityScale, 0, MidpointRounding.AwayFromZero));
    public static decimal AsMoney(long cents) => (decimal)cents / MoneyScale;
    public static decimal AsQuantity(long milli) => (decimal)milli / QuantityScale;
    public static long LineTotal(long priceCentsPerUnit, long milliQuantity) =>
        checked((long)decimal.Round((decimal)priceCentsPerUnit * milliQuantity / QuantityScale, 0, MidpointRounding.AwayFromZero));
    public static long MovingCost(long valueCents, long stockMilli, long issueMilli)
    {
        if (issueMilli <= 0 || stockMilli < issueMilli) throw new BusinessException("الرصيد المخزني غير كافٍ.");
        if (valueCents < 0) throw new BusinessException("قيمة مخزون سالبة.");
        if (issueMilli == stockMilli) return valueCents; // no leftover value without quantity
        return checked((long)decimal.Round((decimal)valueCents * issueMilli / stockMilli, 0, MidpointRounding.AwayFromZero));
    }
    public static void Balanced(IEnumerable<EntryLeg> legs)
    {
        var list = legs.ToArray();
        if (list.Length < 2) throw new BusinessException("القيد يحتاج سطرين على الأقل.");
        long dr=0, cr=0;
        foreach(var leg in list)
        {
            if (leg.Debit < 0 || leg.Credit < 0 || (leg.Debit == 0) == (leg.Credit == 0))
                throw new BusinessException("يجب أن يحتوي سطر القيد على مبلغ مدين أو دائن موجب واحد.");
            dr = checked(dr + leg.Debit); cr = checked(cr + leg.Credit);
        }
        if(dr != cr) throw new BusinessException($"القيد غير متوازن: مدين {AsMoney(dr)} ودائن {AsMoney(cr)}");
    }
}

public sealed class BusinessException(string message) : Exception(message);
public sealed record EntryLeg(string Account, long Debit, long Credit, long? PartyId = null);
public sealed record StockSnapshot(long Quantity, long Value);

public static class Accounting
{
    public const string Cash = "1010";
    public const string Bank = "1020";
    public const string Inventory = "1200";
    public const string Receivable = "1300";
    public const string Payable = "2100";
    public const string Capital = "3100";
    public const string Sales = "4100";
    public const string SalesReturn = "4110";
    public const string PurchaseVariance = "4190";
    public const string CostOfSales = "5100";
    public const string StockVariance = "5200";
    public const string GeneralExpenses = "5300";

    public static EntryLeg Dr(string code, long amount, long? partyId=null) => new(code, amount, 0, partyId);
    public static EntryLeg Cr(string code, long amount, long? partyId=null) => new(code, 0, amount, partyId);
}
