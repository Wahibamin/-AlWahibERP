using System.Windows;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Desktop.Views;

internal sealed class InvoiceDialog(IErpGateway erp,DocumentKind kind,DocumentInfo? origin)
{
    public DocumentDraft? Collect()
    {
        bool returning=kind is DocumentKind.SalesReturn or DocumentKind.PurchaseReturn;
        bool customer=kind is DocumentKind.Sale or DocumentKind.SalesReturn;
        if(returning)
        {
            string expected=customer?"Sale":"Purchase";
            if(origin is null||origin.Type!=expected||origin.Status!="Posted")
                throw new BusinessException("اختر أولًا فاتورة "+(customer?"بيع":"شراء")+" مرحلة من الجدول قبل طلب المرتجع.");
        }
        var parties=erp.Parties().Where(p=>customer?p.Type!="Supplier":p.Type!="Customer").ToArray();
        if(parties.Length==0)throw new BusinessException("أنشئ عميلًا أو موردًا أولًا.");
        var selected=returning?parties.FirstOrDefault(x=>x.Name==origin!.Party):null;
        if(returning && selected is null)throw new BusinessException("لم يُعثر على طرف الفاتورة الأصلية.");
        var info=Forms.Ask("فاتورة "+kind,
            Forms.Select("party",customer?"العميل":"المورد",parties.Select(p=>(p.Id.ToString(),p.Name)),selected?.Id.ToString()??""),
            new Field("date","التاريخ",DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            Forms.Select("mode","طريقة السداد",[("Credit","آجل"),("Cash","نقدي")],returning?"Credit":"Credit"),
            new Field("ref","رقم فاتورة المورد (اختياري)"));
        if(info is null)return null;
        long partyId=long.Parse(info["party"]);
        if(returning&&partyId!=selected!.Id)throw new BusinessException("يجب أن يبقى الطرف مطابقًا للفاتورة الأصلية.");
        var draftLines=new List<LineDraft>();
        var items=erp.Items();
        if(items.Count==0)throw new BusinessException("أضف صنفًا أولًا.");
        IReadOnlyList<(long LineId,long ItemId,string Name,decimal Quantity,decimal UnitPrice,decimal Returned)> orig=[];
        if(returning) orig=erp.DocumentLines(origin!.Id);
        bool again;
        do
        {
            Dictionary<string,string>? r;
            if(returning)
            {
                var eligible=orig.Where(x=>x.Quantity>x.Returned).ToArray();
                if(eligible.Length==0)throw new BusinessException("لا توجد بنود متاحة للإرجاع.");
                r=Forms.Ask("إضافة بند مرتجع",Forms.Select("line","السطر من الفاتورة الأصلية",
                       eligible.Select(x=>(x.LineId.ToString(),x.Name+" | باقي "+(x.Quantity-x.Returned)+" | سعر "+x.UnitPrice))),
                       new Field("qty","الكمية التي سيتم إرجاعها","1"));
                if(r is null) { if(draftLines.Count==0)return null; break; }
                var src=eligible.Single(x=>x.LineId==long.Parse(r["line"]));
                decimal qty=Ui.Decimal(r["qty"]);
                if(qty<=0||qty>src.Quantity-src.Returned)throw new BusinessException("الكمية أكبر من المسموح.");
                if(draftLines.Where(x=>x.OriginLineId==src.LineId).Sum(x=>x.Quantity)+qty>src.Quantity-src.Returned)
                    throw new BusinessException("مجموع سطور المرتجع يتجاوز الأصل.");
                draftLines.Add(new LineDraft(src.ItemId,qty,src.UnitPrice,src.LineId));
            }
            else
            {
                r=Forms.Ask("إضافة بند فاتورة",Forms.Select("item","الصنف",items.Select(x=>(x.Id.ToString(),x.Sku+" • "+x.Name+" • رصيد "+x.Quantity))),
                    new Field("qty","الكمية بالوحدة الأساسية","1"),
                    new Field("price","سعر الوحدة - اتركه فارغًا لاستخدام سعر البيع الافتراضي"));
                if(r is null) { if(draftLines.Count==0)return null; break; }
                var item=items.Single(x=>x.Id==long.Parse(r["item"]));
                decimal price=string.IsNullOrWhiteSpace(r["price"])?item.Price:Ui.Decimal(r["price"]);
                draftLines.Add(new LineDraft(item.Id,Ui.Decimal(r["qty"]),price));
            }
            again=Ui.Confirm("تمت إضافة البند رقم "+draftLines.Count+". هل تريد إضافة صنف آخر إلى الفاتورة نفسها؟ اختر لا للانتقال إلى حفظ المسودة.");
        }while(again);
        if(!Ui.Confirm("حفظ مسودة "+kind+" تحتوي على "+draftLines.Count+" بند؟"))return null;
        return new DocumentDraft(kind,partyId,Ui.Date(info["date"]),returning?"Credit":info["mode"],
            info["ref"],returning?origin!.Id:null,draftLines);
    }
}
