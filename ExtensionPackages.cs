using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

public sealed class ExtensionPackage {
    public string Id, Name, Version, Folder, Sha256;
    public int ManifestVersion;
    public Dictionary<string,object> Manifest;
    static IEnumerable<object> Items(object value){var sequence=value as System.Collections.IEnumerable;return sequence==null||value is string?Enumerable.Empty<object>():sequence.Cast<object>();}
    public string Permissions {
        get {var items=new List<string>();foreach(var key in new[]{"permissions","host_permissions","optional_permissions","optional_host_permissions"}) {
            object value;if(Manifest.TryGetValue(key,out value))items.Add(key+": "+String.Join(", ",Items(value).Select(x=>Convert.ToString(x))));
        }
        object content;if(Manifest.TryGetValue("content_scripts",out content)) {
            var matches=new List<string>();foreach(var script in Items(content)){var entry=script as Dictionary<string,object>;object value;if(entry!=null&&entry.TryGetValue("matches",out value))matches.AddRange(Items(value).Select(x=>Convert.ToString(x)));}
            if(matches.Count>0)items.Add("content_scripts.matches: "+String.Join(", ",matches.Distinct()));
        }return items.Count==0?"Разрешения не заявлены.":String.Join(Environment.NewLine,items);}
    }
    public string Page(string kind) {
        object value;
        if(kind=="options") {
            if(Manifest.TryGetValue("options_ui",out value)&&value is Dictionary<string,object>){object page;if(((Dictionary<string,object>)value).TryGetValue("page",out page))return SafePage(Convert.ToString(page));}
            if(Manifest.TryGetValue("options_page",out value))return SafePage(Convert.ToString(value));
        }else foreach(var key in new[]{"action","browser_action","page_action"}) {
            if(Manifest.TryGetValue(key,out value)&&value is Dictionary<string,object>){object page;if(((Dictionary<string,object>)value).TryGetValue("default_popup",out page))return SafePage(Convert.ToString(page));}
        }
        return null;
    }
    static string SafePage(string page) {
        if(String.IsNullOrWhiteSpace(page)||page.Contains(":")||page.Contains("\\")||page.Split('/').Any(x=>x==".."))return null;
        return page.TrimStart('/');
    }
}

public static class ExtensionPackages {
    public const int MaxCrx=64*1024*1024;
    static JavaScriptSerializer Json(){return new JavaScriptSerializer {MaxJsonLength=8*1024*1024};}
    static void CopyExact(Stream source,Stream output,long expected) {
        long total=0;var buffer=new byte[65536];int count;while((count=source.Read(buffer,0,buffer.Length))>0){total+=count;if(total>expected||total>64L*1024*1024)throw new InvalidDataException("Фактический размер ZIP-файла превышает заявленный.");output.Write(buffer,0,count);}if(total!=expected)throw new InvalidDataException("Фактический размер ZIP-файла не совпал с заявленным.");
    }
    public static string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    sealed class Field {public int Number;public byte[] Bytes;}
    static ulong Varint(byte[] bytes,ref int pos) {
        ulong result=0;for(int shift=0;shift<64;shift+=7){if(pos>=bytes.Length)throw new InvalidDataException("Оборванный CRX header.");byte b=bytes[pos++];if(shift==63&&(b&254)!=0)throw new InvalidDataException("CRX varint overflow.");result|=((ulong)(b&127))<<shift;if((b&128)==0)return result;}throw new InvalidDataException("CRX varint overflow.");
    }
    static List<Field> Fields(byte[] bytes) {
        int pos=0;var result=new List<Field>();while(pos<bytes.Length){ulong tag=Varint(bytes,ref pos);int wire=(int)(tag&7);if(tag>>3==0||tag>>3>Int32.MaxValue)throw new InvalidDataException("Неверный CRX tag.");
            if(wire==2){ulong size=Varint(bytes,ref pos);if(size>(ulong)(bytes.Length-pos))throw new InvalidDataException("Неверная длина CRX header.");var data=new byte[(int)size];Buffer.BlockCopy(bytes,pos,data,0,data.Length);pos+=data.Length;result.Add(new Field {Number=(int)(tag>>3),Bytes=data});}
            else if(wire==0)Varint(bytes,ref pos);
            else if(wire==1||wire==5){int size=wire==1?8:4;if(pos+size>bytes.Length)throw new InvalidDataException("Оборванный CRX header.");pos+=size;}
            else throw new InvalidDataException("Неподдерживаемый CRX wire type.");
        }return result;
    }
    static byte[] One(List<Field> fields,int number) {var matches=fields.Where(x=>x.Number==number).ToArray();if(matches.Length!=1)throw new InvalidDataException("Неоднозначный или отсутствующий CRX field.");return matches[0].Bytes;}
    sealed class Der {
        byte[] bytes;int pos;public Der(byte[] data){bytes=data;}
        public byte[] Read(byte tag) {if(pos>=bytes.Length||bytes[pos++]!=tag)throw new InvalidDataException("Неверный формат публичного RSA-ключа.");if(pos>=bytes.Length)throw new InvalidDataException("Оборванный DER.");int size=bytes[pos++];if(size>=128){int count=size&127;if(count==0||count>4||pos+count>bytes.Length)throw new InvalidDataException("Неверная длина DER.");size=0;for(int i=0;i<count;i++)size=checked(size*256+bytes[pos++]);}if(size<0||size>bytes.Length-pos)throw new InvalidDataException("Оборванный DER.");var value=new byte[size];Buffer.BlockCopy(bytes,pos,value,0,size);pos+=size;return value;}
        public void End(){if(pos!=bytes.Length)throw new InvalidDataException("Лишние данные в RSA-ключе.");}
    }
    static RSAParameters RsaKey(byte[] spki) {
        var outer=new Der(spki);var sequence=new Der(outer.Read(0x30));outer.End();var algorithm=new Der(sequence.Read(0x30));var oid=algorithm.Read(0x06);if(BitConverter.ToString(oid)!="2A-86-48-86-F7-0D-01-01-01")throw new InvalidDataException("Ожидался RSA-ключ CRX.");algorithm.Read(0x05);algorithm.End();var bits=sequence.Read(0x03);sequence.End();if(bits.Length<2||bits[0]!=0)throw new InvalidDataException("Неверный RSA bit string.");var reader=new Der(bits.Skip(1).ToArray());var values=new Der(reader.Read(0x30));reader.End();var modulus=values.Read(0x02);var exponent=values.Read(0x02);values.End();if(modulus.Length>0&&modulus[0]==0)modulus=modulus.Skip(1).ToArray();if(exponent.Length>0&&exponent[0]==0)exponent=exponent.Skip(1).ToArray();if(modulus.Length<128||modulus.Length>1024||exponent.Length==0||exponent.Length>8)throw new InvalidDataException("Недопустимый размер RSA-ключа.");return new RSAParameters {Modulus=modulus,Exponent=exponent};
    }
    static string IdFor(byte[] bytes) {return String.Concat(bytes.Select(b=>""+(char)('a'+(b>>4))+(char)('a'+(b&15))));}
    public static ExtensionPackage Unpack(byte[] crx,string expectedId,string storage) {
        if(crx.Length<16||crx.Length>MaxCrx||Encoding.ASCII.GetString(crx,0,4)!="Cr24"||BitConverter.ToUInt32(crx,4)!=3)throw new InvalidDataException("Поддерживаются подписанные пакеты CRX3.");
        uint size=BitConverter.ToUInt32(crx,8);if(size>1024*1024||size>(uint)(crx.Length-12))throw new InvalidDataException("Неверная длина CRX3 header.");
        var header=Fields(crx.Skip(12).Take((int)size).ToArray());var signed=One(header,10000);var identity=One(Fields(signed),1);if(identity.Length!=16)throw new InvalidDataException("Неверный CRX ID.");string id=IdFor(identity);if(expectedId!=null&&expectedId.Trim()!=id)throw new InvalidDataException("ID скачанного пакета не совпадает с выбранным расширением.");
        byte[] payload=crx.Skip(12+(int)size).ToArray();byte[] digest;
        using(var hash=SHA256.Create())using(var message=new MemoryStream()) {
            var context=Encoding.ASCII.GetBytes("CRX3 SignedData\0");message.Write(context,0,context.Length);var length=BitConverter.GetBytes(signed.Length);message.Write(length,0,length.Length);message.Write(signed,0,signed.Length);message.Write(payload,0,payload.Length);message.Position=0;digest=hash.ComputeHash(message);
        }
        byte[] identityKey=null;
        foreach(var proof in header.Where(x=>x.Number==2)) {
            var fields=Fields(proof.Bytes);var key=One(fields,1);var signature=One(fields,2);using(var hash=SHA256.Create())if(!hash.ComputeHash(key).Take(16).SequenceEqual(identity))continue;
            using(var rsa=new RSACng()){rsa.ImportParameters(RsaKey(key));if(!rsa.VerifyHash(digest,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("RSA-подпись CRX3 не прошла проверку.");}identityKey=key;
        }
        if(identityKey==null)throw new InvalidDataException("Не найдена поддерживаемая RSA-подпись владельца CRX ID. ECDSA-only пакеты пока не поддерживаются.");
        string hashValue=Hash(crx);storage=Path.GetFullPath(storage);Directory.CreateDirectory(storage);string folder=Path.Combine(storage,id+"-"+hashValue.Substring(0,16));
        // Validate ZIP paths and sizes before writing any extracted file.
        using(var memory=new MemoryStream(payload))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)) {
            if(zip.Entries.Count>20000)throw new InvalidDataException("Слишком много файлов расширения.");long total=0;var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries){string relative=Relative(entry.FullName);if(relative.Equals("local-package-receipt.json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Зарезервированное имя квитанции пакета.");if(!paths.Add(relative))throw new InvalidDataException("Повторяющееся имя файла в ZIP.");if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Символические ссылки в пакете не поддерживаются.");total=checked(total+entry.Length);if(total>256L*1024*1024||entry.Length>64L*1024*1024)throw new InvalidDataException("Распакованный пакет слишком большой.");}
            var manifestEntry=zip.GetEntry("manifest.json");if(manifestEntry==null||manifestEntry.Length>2*1024*1024)throw new InvalidDataException("Манифест расширения не найден или слишком большой.");
            Dictionary<string,object> manifest;using(var stream=manifestEntry.Open())using(var memoryManifest=new MemoryStream()){CopyExact(stream,memoryManifest,manifestEntry.Length);manifest=Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(memoryManifest.ToArray()).TrimStart('\uFEFF'));}
            manifest["key"]=Convert.ToBase64String(identityKey);
            var package=Info(manifest,folder);package.Id=id;package.Sha256=hashValue;
            if(Directory.Exists(folder)) {
                string receipt=Path.Combine(folder,"local-package-receipt.json");if(!File.Exists(receipt)||Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt))["sha256"].ToString()!=hashValue)throw new IOException("Каталог пакета уже существует без ожидаемой квитанции.");
                foreach(var entry in zip.Entries) {
                    if(entry.FullName.EndsWith("/")||entry.FullName.StartsWith("_metadata/",StringComparison.Ordinal))continue;
                    string file=Path.Combine(folder,Relative(entry.FullName).Replace('/',Path.DirectorySeparatorChar));
                    if(!File.Exists(file)||new FileInfo(file).Length>MaxCrx)throw new IOException("Распакованный пакет изменён: отсутствует файл или превышен размер.");
                    if(entry.FullName=="manifest.json") {if(File.ReadAllText(file)!=Json().Serialize(manifest))throw new IOException("Манифест пакета изменён.");}
                    else using(var stream=entry.Open())using(var memoryCopy=new MemoryStream()){CopyExact(stream,memoryCopy,entry.Length);if(Hash(memoryCopy.ToArray())!=Hash(File.ReadAllBytes(file)))throw new IOException("Распакованный пакет изменён. Используйте новую папку пакетов.");}
                }
                return ReadFolder(folder,id,hashValue);
            }
            string stage=Path.Combine(storage,"stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try {
                foreach(var entry in zip.Entries){string relative=Relative(entry.FullName);if(relative.StartsWith("_metadata/",StringComparison.Ordinal))continue;string path=Path.GetFullPath(Path.Combine(stage,relative.Replace('/',Path.DirectorySeparatorChar)));if(!path.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Выход из каталога пакета.");if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write))CopyExact(source,output,entry.Length);}
                File.WriteAllText(Path.Combine(stage,"manifest.json"),Json().Serialize(manifest),new UTF8Encoding(false));
                // Receipt is outside extension-reserved _metadata and is not executable.
                File.WriteAllText(Path.Combine(stage,"local-package-receipt.json"),Json().Serialize(new {sha256=hashValue,id=id}),Encoding.UTF8);
                Directory.Move(stage,folder);
            }finally{if(Directory.Exists(stage)&&Path.GetFullPath(stage).StartsWith(storage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(stage,true);}
            return ReadFolder(folder,id,hashValue);
        }
    }
    static string Relative(string value) {
        if(String.IsNullOrEmpty(value)||value.Contains("\\")||value.Contains(":")||value.StartsWith("/")||value.Split('/').Any(x=>x==".."||x=="."||x.TrimEnd(' ','.')!=x)||value.Contains("\0"))throw new InvalidDataException("Недопустимый путь в пакете.");return value;
    }
    static ExtensionPackage Info(Dictionary<string,object> manifest,string folder) {
        object v;if(!manifest.TryGetValue("name",out v))throw new InvalidDataException("В манифесте нет имени.");var result=new ExtensionPackage {Name=Convert.ToString(v),Folder=folder,Manifest=manifest};
        if(!manifest.TryGetValue("version",out v))throw new InvalidDataException("В манифесте нет версии.");result.Version=Convert.ToString(v);if(!Regex.IsMatch(result.Version,@"^\d+(\.\d+){0,3}$"))throw new InvalidDataException("Неподдерживаемая версия расширения.");
        if(!manifest.TryGetValue("manifest_version",out v)||(Convert.ToInt32(v)!=2&&Convert.ToInt32(v)!=3))throw new InvalidDataException("Поддерживаются Manifest V2 и V3.");result.ManifestVersion=Convert.ToInt32(v);return result;
    }
    public static ExtensionPackage ReadFolder(string folder,string id=null,string hash=null) {
        folder=Path.GetFullPath(folder);string manifest=Path.Combine(folder,"manifest.json");if(!File.Exists(manifest)||new FileInfo(manifest).Length>2*1024*1024)throw new InvalidDataException("В папке нет допустимого manifest.json.");var package=Info(Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(manifest)),folder);package.Id=id;package.Sha256=hash;
        if(package.Name.StartsWith("__MSG_",StringComparison.Ordinal)&&package.Name.EndsWith("__",StringComparison.Ordinal)) {
            object locale;if(package.Manifest.TryGetValue("default_locale",out locale)&&Regex.IsMatch(Convert.ToString(locale),"^[A-Za-z0-9_-]+$")) {
                var messages=Path.Combine(folder,"_locales",Convert.ToString(locale),"messages.json");if(File.Exists(messages)){var localized=Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(messages));object row;string key=package.Name.Substring(6,package.Name.Length-8);if(localized.TryGetValue(key,out row)&&row is Dictionary<string,object>){object message;if(((Dictionary<string,object>)row).TryGetValue("message",out message))package.Name=Convert.ToString(message);}}
            }
        }return package;
    }
}
