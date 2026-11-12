using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wahib.Application;
using Wahib.Domain;
using Wahib.Infrastructure;
using Wahib.Desktop.Views;
namespace Wahib.Desktop;

public partial class MainWindow : Window
{
    private readonly CompanyStore _store=new();
    private IErpGateway? _erp;
    private LoginInfo? _actor;
    private DataGrid? _documentGrid;
    public MainWindow()
    {
        InitializeComponent();
        Loaded+=(_,_)=>Ui.Safe(Initialize);
        Closed+=(_,_)=>_erp?.Dispose();
        PreviewKeyDown+=Shortcuts;
    }
    private void Initialize()
    {
        var companies=_store.Companies();
        CompanyInfo? company=null;
        if(companies.Count==0)
        {
            var result=Forms.Ask("تهيئة الشركة الأولى",
               new Field("name","اسم الشركة / المحل"),
               Forms.Select("currency","عملة دفتر الأستاذ",[("YER","ريال يمني YER"),("SAR","ريال سعودي SAR"),("USD","دولار أمريكي USD")],"YER"),
               new Field("password","كلمة مرور المالك (10 أحرف على الأقل)",Secret:true));
            if(result is null){Close();return;}
            company=_store.Create(result["name"],result["currency"],result["password"]);
            Ui.Info("تم إنشاء الشركة. اسم المستخدم الأول: owner");
        }
        else if(companies.Count==1)company=companies[0];
        else
        {
            var choices=companies.Select(c=>(c.Id,c.Name+" | "+c.Currency)).ToArray();
            var result=Forms.Ask("اختر الشركة",Forms.Select("company","الشركة",choices));
            if(result is null){Close();return;}
            company=companies.First(x=>x.Id==result["company"]);
        }
        _erp=_store.Open(company!.Id);
        for(int attempt=0;attempt<3 && _actor is null;attempt++)
        {
            var log=Forms.Ask("دخول المستخدم - "+company.Name,
                new Field("username","اسم المستخدم","owner"),
                new Field("password","كلمة المرور",Secret:true));
            if(log is null){Close();return;}
            try{_actor=_erp.Login(log["username"],log["password"]);}
            catch(BusinessException ex){Ui.Info(ex.Message);}
        }
        if(_actor is null){Close();return;}
        CompanyLabel.Text=company.Name+"\n"+company.Currency;
        CurrentUser.Text=_actor.Name+"  •  "+_actor.Role;
        CreateNavigation(); Dashboard();
    }
    private IErpGateway Erp => _erp??throw new InvalidOperationException("لا توجد شركة مفتوحة.");
    private LoginInfo Actor => _actor??throw new InvalidOperationException("لم يتم تسجيل الدخول.");
    private bool IsCostVisible => Actor.Role is UserRole.Owner or UserRole.Accountant or UserRole.Inventory;
    private void CreateNavigation()
    {
        NavPanel.Children.Clear();
        AddNav("⌂   لوحة التحكم",Dashboard);
        AddNav("▣   الأصناف والمخزون",Items);
        AddNav("♧   العملاء والموردون",Parties);
        AddNav("▤   الفواتير والمرتجعات",Documents);
        AddNav("◉   الصندوق والسداد",Payments);
        AddNav("≡   الأستاذ والقيود",Accounting);
        AddNav("▥   التقارير المالية",Reports);
        AddNav("⚙   المستخدمون والنسخ",Administration);
        AddNav("⟲   تحديث الواجهة",Dashboard);
    }
    private void AddNav(string title,Action onClick)
    {
        var b=new Button{Content=title,HorizontalContentAlignment=HorizontalAlignment.Right,Margin=new Thickness(4,3,4,3),
            Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(14,10,14,10)};
        b.Click+=(_,_)=>Ui.Safe(onClick);NavPanel.Children.Add(b);
    }
    private void Show(string title,UIElement page)
    {
        PageTitle.Text=title; Workspace.Content=page; _documentGrid=null;
        Footer.Text="الوهيب ERP • "+Erp.Company.Name+" • عملة الدفتر "+Erp.Company.Currency+" • وضع دون إنترنت";
    }
    private static FrameworkElement Scroll(UIElement content) => new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private void Dashboard()
    {
        var data=Erp.Summary();
        var items=Erp.Items();
        var root=new StackPanel{Margin=new Thickness(14)};
        root.Children.Add(Ui.Text("أرقام فعلية مشتقة من دفتر الأستاذ المرحّل",16,true));
        var wrap=new WrapPanel();
        void Stat(string label,decimal value)
        {
            var stack=new StackPanel{Margin=new Thickness(14),Width=190};
            stack.Children.Add(Ui.Text(label,15));
            stack.Children.Add(Ui.Text(value.ToString("N4")+" "+Erp.Company.Currency,22,true));
            var b=new Border{BorderBrush=Brushes.LightSteelBlue,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Margin=new Thickness(6),Child=stack};
            wrap.Children.Add(b);
        }
        Stat("صافي المبيعات",data.Sales);Stat("الصندوق",data.Cash);Stat("ذمم العملاء",data.CustomersDue);
        Stat("ديون الموردين",data.SuppliersDue);
        if(IsCostVisible){Stat("قيمة المخزون",data.Inventory);Stat("الربح التراكمي",data.Profit);}
        root.Children.Add(wrap);
        root.Children.Add(Ui.Text("الأصناف التي وصلت لحد إعادة الطلب",18,true));
        var low=items.Where(x=>x.Quantity<=x.Reorder).Select(x=>new{x.Sku,x.Name,x.Quantity,x.Reorder}).ToList();
        root.Children.Add(Ui.Table(low));
        root.Children.Add(Ui.Text("هذه نسخة تشغيلية أولية لا تستخدم الضريبة أو فروق الصرف. لا تعتبر بديلًا عن مراجعة محاسب.",13));
        Show("لوحة التحكم",Scroll(root));
    }
    private void Items()
    {
        var data=Erp.Items();
        var grid=Ui.Table(IsCostVisible?data.Select(x=>new{x.Id,x.Sku,x.Name,x.Barcode,x.Unit,x.Quantity,x.Value,x.Price,x.Reorder}).ToArray():
            (object)data.Select(x=>new{x.Id,x.Sku,x.Name,x.Barcode,x.Unit,x.Quantity,x.Price,x.Reorder}).ToArray());
        var bar=Ui.Bar(
            Ui.Action("+ صنف جديد",()=>
            {
                var r=Forms.Ask("إضافة صنف",
                    new Field("sku","رمز الصنف SKU"),new Field("name","اسم الصنف"),
                    new Field("barcode","الباركود (اختياري)"),new Field("unit","الوحدة الأساسية","حبة"),
                    new Field("price","سعر البيع لكل وحدة","0"),new Field("reorder","حد إعادة الطلب","0"));
                if(r is null)return;
                Erp.AddItem(Actor,r["sku"],r["name"],r["barcode"],r["unit"],Ui.Decimal(r["price"]),Ui.Decimal(r["reorder"]));
                Items();
            }),
            Ui.Action("جرد وتسوية",()=>
            {
                if(grid.SelectedItem is null)throw new BusinessException("اختر صنفًا من الجدول.");
                var selectedId=ReadId(grid.SelectedItem);
                var r=Forms.Ask("جرد صنف",new Field("qty","الكمية التي وُجدت فعليًا"),
                    new Field("date","تاريخ الجرد",DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),new Field("reason","سبب الفرق"));
                if(r is null)return;
                Erp.AdjustStock(Actor,selectedId,Ui.Decimal(r["qty"]),Ui.Date(r["date"]),r["reason"]);Items();
            }),
            Ui.Action("تحديث",Items));
        Show("الأصناف والمخزون",Ui.Page("كمية الوحدة الأساسية • القيمة محفوظة بسنتات صحيحة",bar,grid));
    }
    private static long ReadId(object data)
    {
        var prop=data.GetType().GetProperty("Id") ?? throw new InvalidOperationException("لا يوجد حقل Id.");
        return Convert.ToInt64(prop.GetValue(data));
    }
    private void Parties()
    {
        var grid=Ui.Table(Erp.Parties());
        var bar=Ui.Bar(Ui.Action("+ طرف جديد",()=>
        {
            var r=Forms.Ask("عميل / مورد",
                new Field("name","الاسم"),Forms.Select("type","التصنيف",[("Customer","عميل"),("Supplier","مورد"),("Both","عميل ومورد")]),
                new Field("phone","الهاتف"));
            if(r is null)return;
            Erp.AddParty(Actor,r["name"],r["type"],r["phone"]);Parties();
        }),Ui.Action("تحديث",Parties));
        Show("العملاء والموردون",Ui.Page("أرصدة الأطراف مشتقة من القيود، وليست حقولًا قابلة للتحرير",bar,grid));
    }
    private void Documents()
    {
        var docs=Erp.Documents().Where(x=>IsCostVisible || (x.Type is "Sale" or "SalesReturn")).ToArray();
        var grid=Ui.Table(docs);_documentGrid=grid;
        void Create(DocumentKind k,DocumentInfo? original=null)
        {
            var draft=new InvoiceDialog(Erp,k,original).Collect();
            if(draft is null)return;
            Erp.CreateDocument(Actor,draft);
            Documents();
        }
        var bar=Ui.Bar(
            Ui.Action("+ بيع",()=>Create(DocumentKind.Sale)),
            Ui.Action("+ شراء",()=>Create(DocumentKind.Purchase)),
            Ui.Action("مرتجع بيع",()=>Create(DocumentKind.SalesReturn,grid.SelectedItem as DocumentInfo)),
            Ui.Action("مرتجع شراء",()=>Create(DocumentKind.PurchaseReturn,grid.SelectedItem as DocumentInfo)),
            Ui.Action("✓ ترحيل المسودة",()=>
            {
                var doc=grid.SelectedItem as DocumentInfo??throw new BusinessException("اختر مسودة أولًا.");
                if(!Ui.Confirm("هل تريد ترحيل المستند؟ لا يمكن تعديل المرحلة مباشرة."))return;
                Erp.PostDocument(Actor,doc.Id);Documents();
            }),
            Ui.Action("طباعة A4 / PDF",()=>
            {
                var doc=grid.SelectedItem as DocumentInfo??throw new BusinessException("اختر مستندًا للطباعة.");
                InvoicePrinter.Print(Erp,doc);
            }),
            Ui.Action("تحديث",Documents));
        Show("الفواتير والمرتجعات",Ui.Page("احفظ المسودة أولًا، ثم حددها واضغط ترحيل. المبالغ غير المرحّلة لا تدخل القيود.",bar,grid));
        _documentGrid=grid;
    }
    private void Shortcuts(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.F9 && _documentGrid?.SelectedItem is DocumentInfo doc)
        {
            e.Handled=true;
            Ui.Safe(()=>{if(Ui.Confirm("ترحيل "+doc.Number+"؟")){Erp.PostDocument(Actor,doc.Id);Documents();}});
        }
    }
    private void Payments()
    {
        var docs=Erp.Documents().Where(x=>x.Status=="Posted" && x.Outstanding>0 &&
            (x.Type is "Sale" or "Purchase" or "SalesReturn" or "PurchaseReturn") &&
            (IsCostVisible || x.Type=="Sale")).ToArray();
        var grid=Ui.Table(docs);
        var bar=Ui.Bar(Ui.Action("+ سند قبض / صرف",()=>
        {
            var doc=grid.SelectedItem as DocumentInfo??throw new BusinessException("حدد فاتورة ذات رصيد من الجدول.");
            var kind=doc.Type switch
            {
                "Sale"=>PaymentKind.CustomerReceipt,"Purchase"=>PaymentKind.SupplierPayment,
                "SalesReturn"=>PaymentKind.CustomerRefund,_=>PaymentKind.SupplierRefund
            };
            var selectedParty=Erp.Parties().FirstOrDefault(p=>p.Name==doc.Party)
                ??throw new BusinessException("لم يُعثر على الطرف.");
            var r=Forms.Ask("سداد الفاتورة "+doc.Number,new Field("amount","المبلغ",doc.Outstanding.ToString("0.0000")),
                new Field("date","تاريخ السداد",DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),new Field("note","البيان"));
            if(r is null)return;
            Erp.MakePayment(Actor,kind,selectedParty.Id,Ui.Decimal(r["amount"]),doc.Id,Ui.Date(r["date"]),r["note"]);
            Payments();
        }),Ui.Action("تحديث",Payments));
        Show("الصندوق والسداد",Ui.Page("اختر فاتورة مستحقة ثم نفّذ سند قبض/صرف مخصص لها",bar,grid));
    }
    private void Accounting()
    {
        Permissions.Require(Actor.Role,"Ledger");
        var root=new DockPanel();
        var bar=Ui.Bar(Ui.Action("+ قيد يومية من حسابين",()=>
        {
            var accounts=Erp.Accounts().Where(a=>a.Code is not ("1200" or "1300" or "2100" or "4100" or "4110" or "4190" or "5100" or "5200"))
                .Select(a=>(a.Code,a.Code+" • "+a.Name)).ToArray();
            var r=Forms.Ask("قيد يومية يدوي (حساب مدين / حساب دائن)",
                Forms.Select("dr","حساب مدين",accounts,"1010"),Forms.Select("cr","حساب دائن",accounts,"3100"),
                new Field("amount","المبلغ"),new Field("date","تاريخ القيد",DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                new Field("note","بيان القيد"));
            if(r is null)return;
            decimal money=Ui.Decimal(r["amount"]);
            Erp.PostJournal(Actor,Ui.Date(r["date"]),r["note"],[new JournalRow(r["dr"],money,0),new JournalRow(r["cr"],0,money)]);
            Accounting();
        }),Ui.Action("تحديث",Accounting));
        DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        var tb=Ui.Table(Erp.TrialBalance(DateTime.Today));root.Children.Add(tb);
        Show("الأستاذ والقيود",root);
    }
    private void Reports()
    {
        Show("التقارير المالية",new ReportPanel(Erp,Actor));
    }
    private void Administration()
    {
        Show("الإدارة والصيانة",new AdminPanel(Erp,Actor));
    }
}

