using System.Security.Cryptography;
using System.Text.Json;
using Wahib.Application;
using Wahib.Domain;
namespace Wahib.Infrastructure;

public sealed partial class SqliteErpGateway
{
    private sealed record BackupManifest(string Product,string CompanyId,int SchemaVersion,string UtcTime,string Sha256);
    private static string HashFile(string path)
    {
        using var input=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    public string Backup(LoginInfo actor,string destination)
    {
        Authorized(actor,"Backup");
        if(string.IsNullOrWhiteSpace(destination))throw new BusinessException("اختر مسار النسخ الاحتياطي.");
        if(Path.GetFullPath(destination).Equals(Path.GetFullPath(Company.DatabasePath),StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("لا تنسخ قاعدة البيانات فوق نفسها.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string tmp=destination+".partial";
        try
        {
            if(File.Exists(tmp))File.Delete(tmp);
            using(var target=Sql.Connect(tmp))
            {
                _db.BackupDatabase(target);
            }
            using(var verification=Sql.Connect(tmp,true))Schema.Check(verification);
            var manifest=new BackupManifest("AlWahibERP",Company.Id,Schema.Version,Now(),HashFile(tmp));
            File.Move(tmp,destination,true);
            File.WriteAllText(destination+".manifest.json",JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
            using(var tx=_db.BeginTransaction())
            {
                AuditEvent(tx,actor.UserId,"BackupCreated",Path.GetFileName(destination));tx.Commit();
            }
            return destination;
        }
        finally {try{if(File.Exists(tmp))File.Delete(tmp);}catch{}}
    }
    public void Restore(LoginInfo actor,string source)
    {
        Authorized(actor,"Restore");
        if(!File.Exists(source)||!File.Exists(source+".manifest.json"))throw new BusinessException("تتطلب الاستعادة ملف DB وملف manifest المرافق.");
        var manifest=JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(source+".manifest.json"))
            ?? throw new BusinessException("بيانات النسخة غير سليمة.");
        if(manifest.Product!="AlWahibERP"||manifest.CompanyId!=Company.Id || manifest.SchemaVersion!=Schema.Version)
            throw new BusinessException("هذه النسخة لشركة أخرى أو إصدار مختلف.");
        if(!string.Equals(manifest.Sha256,HashFile(source),StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("فشل تطابق SHA256 للنسخة.");
        using(var verification=Sql.Connect(source,true))Schema.Check(verification);
        string original=Company.DatabasePath;
        string temporary=original+".restore-tmp";
        string rollback=original+".rollback-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".db";
        File.Copy(source,temporary,true);
        bool swapped=false;
        try
        {
            Sql.Exec(_db,null,"PRAGMA wal_checkpoint(TRUNCATE)");
            _db.Close();
            // The file is on the same volume as the source database for an atomic rename.
            File.Move(original,rollback);
            try {File.Move(temporary,original);swapped=true;}
            catch {File.Move(rollback,original);throw;}
            _db.Open();Sql.Exec(_db,null,"PRAGMA foreign_keys=ON");Sql.Exec(_db,null,"PRAGMA journal_mode=WAL");
            Schema.Check(_db);
            using var tx=_db.BeginTransaction();
            // actor may no longer exist or may have changed role in restored data.
            AuditEvent(tx,Sql.Number(_db,tx,"SELECT id FROM Users ORDER BY id LIMIT 1"),"DatabaseRestored","Restore succeeded; previous file kept in rollback folder");
            tx.Commit();
        }
        catch
        {
            try{_db.Close();}catch{}
            if(swapped && File.Exists(rollback))
            {
                try {File.Move(original,original+".failed-restore",true);File.Move(rollback,original);}
                catch { /* do not discard rollback file; manual recovery documented */ }
            }
            try{_db.Open();}catch{}
            throw;
        }
        finally {try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
    }
}
