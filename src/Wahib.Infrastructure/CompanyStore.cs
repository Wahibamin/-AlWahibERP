using System.Text.Json;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Infrastructure;

public sealed class CompanyStore : ICompanyStore
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AlWahibERP");
    private static string Registry => Path.Combine(Root,"companies.json");
    public IReadOnlyList<CompanyInfo> Companies()
    {
        if(!File.Exists(Registry)) return [];
        return JsonSerializer.Deserialize<List<CompanyInfo>>(File.ReadAllText(Registry)) ?? [];
    }
    public CompanyInfo Create(string name,string currency,string ownerPassword)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new BusinessException("أدخل اسم الشركة.");
        currency=currency.ToUpperInvariant().Trim();
        if(currency is not ("YER" or "SAR" or "USD")) throw new BusinessException("العملات المدعومة كعملة أساس: YER وSAR وUSD.");
        var (salt,hash)=Passwords.Hash(ownerPassword);
        Directory.CreateDirectory(Root);
        var id=Guid.NewGuid().ToString("N");
        var path=Path.Combine(Root,"companies",id+".db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using(var db=Sql.Connect(path))
            {
                Schema.Initialize(db,name.Trim(),currency);
                Sql.Exec(db,null,"INSERT INTO Users(username,salt,password_hash,role) VALUES ('owner',@s,@h,'Owner')",("@s",salt),("@h",hash));
            }
            var entry=new CompanyInfo(id,name.Trim(),currency,path);
            var list=Companies().ToList();list.Add(entry);
            var temp=Registry+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(list,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temp,Registry,true);
            return entry;
        }
        catch
        {
            try{File.Delete(path);}catch{}
            throw;
        }
    }
    public IErpGateway Open(string companyId)
    {
        var company=Companies().FirstOrDefault(x=>x.Id==companyId) ?? throw new BusinessException("الشركة غير موجودة.");
        if(!File.Exists(company.DatabasePath))throw new BusinessException("ملف قاعدة بيانات الشركة غير موجود.");
        return new SqliteErpGateway(company);
    }
}
