using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Wahib.Application;

namespace Wahib.Desktop.Views;

internal static class InvoicePrinter
{
    public static void Print(IErpGateway erp,DocumentInfo doc)
    {
        var lines=erp.DocumentLines(doc.Id);
        var rows=lines.Select(x=>new[]{x.Name,x.Quantity.ToString("N3"),x.UnitPrice.ToString("N4"),
            (x.Quantity*x.UnitPrice).ToString("N4")}).ToList();
        rows.Add(["إجمالي المستند","","",doc.Total.ToString("N4")+" "+erp.Company.Currency]);
        PrintTable(erp.Company.Name+" - فاتورة "+doc.Type+" - "+doc.Number,
            ["الصنف","الكمية","سعر الوحدة","الإجمالي"],rows,
            "الطرف: "+doc.Party+" | التاريخ: "+doc.Date+" | الحالة: "+doc.Status);
    }
    public static void PrintTable(string title,string[] headers,IReadOnlyList<string[]> rows,string? subtitle=null)
    {
        var print=new PrintDialog();
        if(print.ShowDialog()!=true)return;
        var flow=new FlowDocument
        {
            FlowDirection=FlowDirection.RightToLeft,FontFamily=new FontFamily("Segoe UI"),FontSize=12,
            PagePadding=new Thickness(38),ColumnWidth=800,PageWidth=780,PageHeight=1060
        };
        flow.Blocks.Add(new Paragraph(new Run(title)){FontSize=20,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center});
        if(subtitle is not null)flow.Blocks.Add(new Paragraph(new Run(subtitle)){FontSize=11,TextAlignment=TextAlignment.Center});
        var table=new System.Windows.Documents.Table{CellSpacing=0};
        for(int i=0;i<headers.Length;i++)table.Columns.Add(new TableColumn());
        var group=new TableRowGroup();table.RowGroups.Add(group);
        var header=new TableRow{Background=Brushes.LightSteelBlue};
        foreach(var field in headers)header.Cells.Add(Cell(field,true));group.Rows.Add(header);
        foreach(var row in rows)
        {
            var tr=new TableRow();
            foreach(var s in row)tr.Cells.Add(Cell(s));
            group.Rows.Add(tr);
        }
        flow.Blocks.Add(table);
        flow.Blocks.Add(new Paragraph(new Run("طُبع بواسطة الوهيب ERP • "+DateTime.Now.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture))){FontSize=10});
        print.PrintDocument(((IDocumentPaginatorSource)flow).DocumentPaginator,title);
    }
    private static TableCell Cell(string value,bool bold=false)
    {
        return new TableCell(new Paragraph(new Run(value))){BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(0,0,0,1),
           Padding=new Thickness(6),FontWeight=bold?FontWeights.Bold:FontWeights.Normal};
    }
    public static void Csv(string path,string[] headers,IReadOnlyList<string[]> rows)
    {
        static string Escape(string v)
        {
            // Excel formula injection protection for exported user-owned values.
            if(v.Length>0&&"=+-@".Contains(v[0]))v="'"+v;
            return "\""+v.Replace("\"","\"\"")+"\"";
        }
        var sb=new StringBuilder();sb.AppendLine(string.Join(",",headers.Select(Escape)));
        foreach(var row in rows)sb.AppendLine(string.Join(",",row.Select(Escape)));
        System.IO.File.WriteAllText(path,sb.ToString(),new UTF8Encoding(true));
    }
}

