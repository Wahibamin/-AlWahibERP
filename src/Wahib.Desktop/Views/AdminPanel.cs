using System.Windows;
using System.Windows.Controls;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Desktop.Views;

internal sealed class AdminPanel:DockPanel
{
    private readonly IErpGateway _erp;
    private readonly LoginInfo _actor;
    private readonly DataGrid _grid=new();
    public AdminPanel(IErpGateway erp,LoginInfo actor)
    {
        _erp=erp;_actor=actor;LastChildFill=true;
        var root=new StackPanel();root.Children.Add(Ui.Text("صيانة الشركة • صلاحيات المالك فقط",20,true));
        root.Children.Add(Ui.Bar(
            Ui.Action("مستخدم جديد",AddUser),Ui.Action("قائمة المستخدمين",ShowUsers),
            Ui.Action("سجل التدقيق",ShowAudit),Ui.Action("فحص السلامة",()=>Ui.Info(_erp.VerifyIntegrity()))));
        root.Children.Add(Ui.Bar(Ui.Action("نسخة احتياطية آمنة",Backup),Ui.Action("استعادة مع فحص",Restore),
            Ui.Action("قفل / فتح فترة",Period),Ui.Action("الفترات",ShowPeriods)));
        root.Children.Add(Ui.Text("احتفظ بنسخة احتياطية خارج القرص الرئيسي؛ الاستعادة تستبدل البيانات الحالية وتعيد تشغيل البرنامج.",12));
        DockPanel.SetDock(root,Dock.Top);Children.Add(root);Children.Add(_grid);
        ShowUsers();
    }
    private void AddUser()
    {
        var r=Forms.Ask("مستخدم جديد",new Field("name","اسم دخول المستخدم"),new Field("password","كلمة المرور",Secret:true),
            Forms.Select("role","الدور",[("Cashier","كاشير"),("Accountant","محاسب"),("Inventory","مدير مخزون"),("Viewer","مشاهدة فقط"),("Owner","مالك") ]));
        if(r is null)return;
        _erp.AddUser(_actor,r["name"],r["password"],Enum.Parse<UserRole>(r["role"]));ShowUsers();
    }
    private void ShowUsers() => _grid.ItemsSource=_erp.Users(_actor).Select(x=>new{x.Username,x.Role,x.Enabled}).ToArray();
    private void ShowAudit() => _grid.ItemsSource=_erp.Audit(_actor).ToArray();
    private void ShowPeriods() => _grid.ItemsSource=_erp.Periods().Select(x=>new{x.Period,x.Closed}).ToArray();
    private void Period()
    {
        var r=Forms.Ask("الفترة المالية",new Field("period","الفترة YYYY-MM",DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)),
            Forms.Select("action","الإجراء",[("close","إغلاق"),("open","إعادة فتح") ]));
        if(r is null)return;
        if(!Ui.Confirm("تأكيد "+r["action"]+" الفترة "+r["period"]+"؟"))return;
        _erp.SetPeriod(_actor,r["period"],r["action"]=="close");ShowPeriods();
    }
    private void Backup()
    {
        var save=new Microsoft.Win32.SaveFileDialog{Title="نسخة SQLite متسقة",
            Filter="AlWahib DB (*.db)|*.db",FileName="AlWahib-Backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".db"};
        if(save.ShowDialog()==true)Ui.Info("نسخة محفوظة: "+_erp.Backup(_actor,save.FileName)+"\nاحتفظ معها بملف manifest.json");
    }
    private void Restore()
    {
        var open=new Microsoft.Win32.OpenFileDialog{Title="اختر DB مع manifest المرافق",Filter="SQLite Backup (*.db)|*.db"};
        if(open.ShowDialog()!=true)return;
        if(!Ui.Confirm("سيتم استبدال بيانات الشركة ببيانات النسخة المختارة. هل لديك نسخة حديثة من الحالة الحالية؟"))return;
        _erp.Restore(_actor,open.FileName);
        Ui.Info("تمت الاستعادة، سيغلق التطبيق. افتحه من جديد وسجّل الدخول ببيانات النسخة المستعادة.");
        System.Windows.Application.Current.Shutdown();
    }
}


