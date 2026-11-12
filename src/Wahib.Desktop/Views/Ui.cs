using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Wahib.Desktop.Views;

internal static class Ui
{
    public static TextBlock Text(string value,int size=14,bool bold=false)
        => new() {Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,
           Foreground=Brushes.DarkSlateGray,Margin=new Thickness(5,8,5,8),TextWrapping=TextWrapping.Wrap};
    public static Button Action(string title,Action handler)
    {
        var b=new Button{Content=title}; b.Click+=(_,_)=>Safe(handler);return b;
    }
    public static void Safe(Action action)
    {
        try { action(); }
        catch(Exception ex){MessageBox.Show(ex.Message,"الوهيب - تعذر التنفيذ",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    public static void Info(string message)=>MessageBox.Show(message,"الوهيب",MessageBoxButton.OK,MessageBoxImage.Information);
    public static bool Confirm(string message)=>MessageBox.Show(message,"تأكيد العملية",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    public static StackPanel Bar(params UIElement[] children)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,14)};
        foreach(var ch in children)row.Children.Add(ch);
        return row;
    }
    public static DockPanel Page(string heading,UIElement actions,DataGrid grid)
    {
        var root=new DockPanel{LastChildFill=true};
        var top=new StackPanel();top.Children.Add(Text(heading,20,true));top.Children.Add(actions);
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);root.Children.Add(grid);return root;
    }
    public static DataGrid Table(object source)
        =>new() {ItemsSource=(System.Collections.IEnumerable)source,Margin=new Thickness(5),SelectionMode=DataGridSelectionMode.Single};
    public static decimal Decimal(string? text)
    {
        text=(text??"").Trim().Replace('٫','.').Replace('٬',',');
        if(decimal.TryParse(text,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out decimal v))return v;
        if(decimal.TryParse(text,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.CurrentCulture,out v))return v;
        throw new ArgumentException("رقم غير صالح: "+text+". استعمل النقطة العشرية مثل 12.50");
    }
    public static DateTime Date(string? text)
    {
        if(DateTime.TryParseExact(text,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var dt))return dt;
        throw new ArgumentException("التاريخ بصيغة yyyy-MM-dd مطلوب.");
    }
}

internal sealed record SelectChoice(string Value,string Label);
internal sealed record Field(string Key,string Title,string Default="",IReadOnlyList<SelectChoice>? Options=null,bool Secret=false);

internal static class Forms
{
    public static Dictionary<string,string>? Ask(string title,params Field[] fields)
    {
        var dialog=new Window{Title=title,Width=540,MinHeight=260,SizeToContent=SizeToContent.Height,
            WindowStartupLocation=WindowStartupLocation.CenterScreen,FlowDirection=FlowDirection.RightToLeft,
            Background=Brushes.White,MaxHeight=740,ResizeMode=ResizeMode.NoResize};
        var inputs=new Dictionary<string,Control>();
        var root=new StackPanel{Margin=new Thickness(24)};
        root.Children.Add(Ui.Text(title,21,true));
        foreach(var f in fields)
        {
            root.Children.Add(Ui.Text(f.Title));
            Control input;
            if(f.Options is not null)
            {
                var cb=new ComboBox{DisplayMemberPath="Label",SelectedValuePath="Value",ItemsSource=f.Options};
                if(f.Options.Count>0)cb.SelectedValue=f.Default.Length>0?f.Default:f.Options[0].Value;
                input=cb;
            }
            else if(f.Secret)input=new PasswordBox{Margin=new Thickness(4),Padding=new Thickness(8),MinHeight=35};
            else input=new TextBox{Text=f.Default};
            root.Children.Add(input);inputs[f.Key]=input;
        }
        var bar=Ui.Bar();bar.HorizontalAlignment=HorizontalAlignment.Left;
        var cancel=Ui.Action("إلغاء",()=>dialog.DialogResult=false);
        var save=Ui.Action("موافق",()=>dialog.DialogResult=true);
        bar.Children.Add(save);bar.Children.Add(cancel);root.Children.Add(bar);
        dialog.Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        if(dialog.ShowDialog()!=true)return null;
        var result=new Dictionary<string,string>();
        foreach(var f in fields)
        {
            var c=inputs[f.Key];
            result[f.Key]=c switch
            {
                TextBox t=>t.Text,PasswordBox p=>p.Password,ComboBox cb=>Convert.ToString(cb.SelectedValue)??"",_=>""
            };
        }
        return result;
    }
    public static Field Select(string key,string title,IEnumerable<(string Value,string Label)> choices,string defaultValue="")
        =>new(key,title,defaultValue,choices.Select(x=>new SelectChoice(x.Value,x.Label)).ToArray());
}
