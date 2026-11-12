namespace Wahib.Domain;

public enum DocumentKind { Sale, Purchase, SalesReturn, PurchaseReturn }
public enum PaymentKind { CustomerReceipt, SupplierPayment, CustomerRefund, SupplierRefund }
public enum UserRole { Owner, Accountant, Cashier, Inventory, Viewer }

public static class Permissions
{
    public static void Require(UserRole role, string operation)
    {
        bool allowed = role == UserRole.Owner || operation switch
        {
            "Read" => true,
            "Sale" => role is UserRole.Cashier or UserRole.Accountant,
            "Purchase" => role is UserRole.Accountant or UserRole.Inventory,
            "Inventory" => role is UserRole.Inventory or UserRole.Accountant,
            "Payment" or "Ledger" or "Period" => role == UserRole.Accountant,
            "Cost" => role is UserRole.Accountant or UserRole.Inventory,
            "Backup" or "Users" or "Restore" => false,
            "Party" or "Item" => role is UserRole.Accountant or UserRole.Inventory,
            _ => false
        };
        if(!allowed) throw new BusinessException("لا تملك صلاحية تنفيذ هذا الإجراء.");
    }
}
