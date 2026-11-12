using System.Windows;
using System.Windows.Controls;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Desktop.Views;

internal sealed class ReportPanel : DockPanel
{
    private readonly IErpGateway _erp;
    private readonly LoginInfo _actor;
    private readonly ComboBox _selection=new(){MinWidth=235};
    private readonly TextBox _from=new(){Text=new DateTime(DateTime.Today.Year,1,1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),Width=128};
    private readonly TextBox _to=new(){Text=DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),Width=128};
    private readonly DataGrid _table=new();
    private string[] _headers=[];
    private List<string[]> _rows=[];
    public ReportPanel(IErpGateway erp,LoginInfo actor)
    {
        _erp=erp;_actor=actor;
        LastChildFill=true;
        var top=new StackPanel();
        top.Children.Add(Ui.Text("التقارير من القيود المحاسبية وحركات المخزون",20,true));
        var first=Ui.Bar();
        foreach(string s in new[]{"ميزان المراجعة","دفتر الأستاذ","قائمة الدخل","المركز المالي","كميات وقيم المخزون","الفواتير"})_selection.Items.Add(s);
        _selection.SelectedIndex=actor.Role is UserRole.Owner or UserRole.Accountant?0:5;
        first.Children.Add(_selection);first.Children.Add(Ui.Text("من"));first.Children.Add(_from);
        first.Children.Add(Ui.Text("إلى"));first.Children.Add(_to);
        top.Children.Add(first);
        top.Children.Add(Ui.Bar(Ui.Action("عرض التقرير",Load),Ui.Action("تصدير CSV",Export),Ui.Action("طباعة / حفظ PDF",Print)));
        DockPanel.SetDock(top,Dock.Top);Children.Add(top);
        _table.IsReadOnly=true;
        Children.Add(_table);
        Load();
    }
    private void Load()
    {
        var start=Ui.Date(_from.Text);var end=Ui.Date(_to.Text);
        if(start>end)throw new ArgumentException("الفترة غير صحيحة.");
        string type=Convert.ToString(_selection.SelectedItem)??"";
        if(_actor.Role is not (UserRole.Owner or UserRole.Accountant) && type!="الفواتير")
            throw new BusinessException("هذا التقرير المالي يتطلب صلاحية محاسب أو مالك.");
        if(type=="دفتر الأستاذ")
        {
            _headers=["التاريخ","رقم القيد","الحساب","البيان","مدين","دائن"];
            _rows=_erp.Ledger(start,end).Select(x=>new[]{x.Date,x.EntryId[..Math.Min(8,x.EntryId.Length)],x.Account+" "+x.AccountName,x.Description,
                x.Debit.ToString("N4"),x.Credit.ToString("N4")}).ToList();
        }
        else if(type=="كميات وقيم المخزون")
        {
            _headers=["رمز","الصنف","الكمية الحالية","قيمة المخزون"];
            _rows=_erp.Items().Select(x=>new[]{x.Sku,x.Name,x.Quantity.ToString("N3"),x.Value.ToString("N4")}).ToList();
        }
        else if(type=="الفواتير")
        {
            _headers=["رقم","النوع","الطرف","التاريخ","الحالة","الإجمالي","المستحق"];
            _rows=_erp.Documents().Where(x=>((_actor.Role is UserRole.Owner or UserRole.Accountant) || (x.Type is "Sale" or "SalesReturn")) &&
                 string.CompareOrdinal(x.Date,start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))>=0 &&
                 string.CompareOrdinal(x.Date,end.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))<=0)
                .Select(x=>new[]{x.Number,x.Type,x.Party,x.Date,x.Status,x.Total.ToString("N4"),x.Outstanding.ToString("N4")}).ToList();
        }
        else
        {
            _headers=["الحساب / البند","المبلغ 1","المبلغ 2"];
            var records=type switch
            {
                "قائمة الدخل"=>_erp.IncomeStatement(start,end),
                "المركز المالي"=>_erp.BalanceSheet(end),
                _=>_erp.TrialBalance(end)
            };
            _rows=records.Select(x=>new[]{x.Label,x.First.ToString("N4"),x.Second.ToString("N4")}).ToList();
        }
        // DataTable supplies user-facing Arabic column names without anonymous reflection.
        var dt=new System.Data.DataTable();foreach(var h in _headers)dt.Columns.Add(h);
        foreach(var r in _rows)dt.Rows.Add(r.Cast<object>().ToArray());
        _table.ItemsSource=dt.DefaultView;
    }
    private void Export()
    {
        if(_headers.Length==0)Load();
        var save=new Microsoft.Win32.SaveFileDialog{Title="حفظ التقرير",Filter="CSV UTF-8 (*.csv)|*.csv",FileName="AlWahib-Report.csv"};
        if(save.ShowDialog()==true){InvoicePrinter.Csv(save.FileName,_headers,_rows);Ui.Info("تم تصدير الملف.");}
    }
    private void Print()
    {
        if(_headers.Length==0)Load();
        InvoicePrinter.PrintTable("الوهيب ERP | "+_selection.SelectedItem,_headers,_rows,_erp.Company.Name+" - "+_erp.Company.Currency);
    }
}
