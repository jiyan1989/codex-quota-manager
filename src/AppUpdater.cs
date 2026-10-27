using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

sealed class UpdateRelease {
    public string Version, Url, Sha256, Page;
}

static class AppUpdater {
    public const string Repository="jiyan1989/codex-quota-manager";
    public static string CurrentVersion {get{return typeof(AppUpdater).Assembly.GetName().Version.ToString(3);}}
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
    static readonly object JsonLock=new object();
    static object Get(Dictionary<string,object> o,string key){object v;return o.TryGetValue(key,out v)?v:null;}
    public static UpdateRelease ParseRelease(string json){
        Dictionary<string,object> data;lock(JsonLock)data=Json.Deserialize<Dictionary<string,object>>(json);
        if(Convert.ToBoolean(Get(data,"draft")??false) || Convert.ToBoolean(Get(data,"prerelease")??false))return null;
        string tag=Convert.ToString(Get(data,"tag_name"));Version version;
        if(tag==null || !tag.StartsWith("v",StringComparison.Ordinal) || !System.Version.TryParse(tag.Substring(1),out version) || version.Revision>0)throw new InvalidDataException("无法识别发布版本。");
        string expected="CodexQuotaManager-"+tag+"-windows-x64.zip";
        var assets=Get(data,"assets") as IEnumerable;
        if(assets==null)throw new InvalidDataException("发布包尚未上传。");
        foreach(object item in assets){
            var asset=item as Dictionary<string,object>;if(asset==null || Convert.ToString(Get(asset,"name"))!=expected)continue;
            string url=Convert.ToString(Get(asset,"browser_download_url"));Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="github.com" || uri.AbsolutePath!="/"+Repository+"/releases/download/"+tag+"/"+expected)throw new InvalidDataException("更新来源不是本项目的 GitHub 发布包。");
            string digest=Convert.ToString(Get(asset,"digest"));
            if(!System.Text.RegularExpressions.Regex.IsMatch(digest??"","^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("发布包缺少有效校验信息，请稍后重试。");
            return new UpdateRelease{Version=version.ToString(3),Url=url,Sha256=digest.Substring(7).ToLowerInvariant(),Page="https://github.com/"+Repository+"/releases/tag/"+tag};
        }
        throw new InvalidDataException("未找到 Windows 运行包。");
    }
    static void Download(string url,string destination,long limit){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="CodexQuotaManager/"+CurrentVersion;request.Accept="*/*";request.Timeout=20000;request.ReadWriteTimeout=20000;
        using(var response=(HttpWebResponse)request.GetResponse()){
            if(response.ResponseUri.Scheme!="https" || response.ContentLength>limit)throw new InvalidDataException("更新文件大小或连接不符合要求。");
            using(var source=response.GetResponseStream())using(var target=File.Create(destination)){
                byte[] buffer=new byte[65536];long total=0;int size;
                while((size=source.Read(buffer,0,buffer.Length))>0){total+=size;if(total>limit)throw new InvalidDataException("更新文件过大。");target.Write(buffer,0,size);}
            }
        }
    }
    public static UpdateRelease Check(string directory){
        Directory.CreateDirectory(directory);string file=Path.Combine(directory,"release-"+Guid.NewGuid().ToString("N")+".json");
        try{
            try{Download("https://raw.githubusercontent.com/"+Repository+"/main/update.json",file,4*1024*1024);}
            catch(WebException){Download("https://api.github.com/repos/"+Repository+"/releases/latest",file,4*1024*1024);}
            return ParseRelease(File.ReadAllText(file,Encoding.UTF8));
        }
        finally{if(File.Exists(file))File.Delete(file);}
    }
    public static string Hash(string path){using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
    public static string ExtractVerified(string zip,string hash,string expectedVersion,string directory){
        if(!String.Equals(Hash(zip),hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新包校验失败，已保留当前版本。");
        Directory.CreateDirectory(directory);string exe=Path.Combine(directory,"CodexQuotaMonitor.exe");
        using(var archive=ZipFile.OpenRead(zip)){
            var entries=archive.Entries.Where(x=>x.FullName=="CodexQuotaMonitor.exe").ToArray();
            if(entries.Length!=1 || entries[0].Length<1 || entries[0].Length>64*1024*1024)throw new InvalidDataException("更新包内的程序文件无效。");
            using(var input=entries[0].Open())using(var output=File.Create(exe))input.CopyTo(output);
        }
        var actual=System.Reflection.AssemblyName.GetAssemblyName(exe).Version;
        if(actual.ToString(3)!=expectedVersion)throw new InvalidDataException("程序版本与发布版本不一致。");
        return exe;
    }
    public static string Prepare(UpdateRelease release,string directory){
        Directory.CreateDirectory(directory);string zip=Path.Combine(directory,"package.zip");Download(release.Url,zip,128*1024*1024);
        return ExtractVerified(zip,release.Sha256,release.Version,directory);
    }
    public static Dictionary<string,object> ReadObject(string path){try{lock(JsonLock)return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));}catch{return new Dictionary<string,object>();}}
    public static void WriteObject(string path,object value){lock(JsonLock)File.WriteAllText(path,Json.Serialize(value),new UTF8Encoding(false));}
    public static bool AutoCheckDue(string cache){
        var data=ReadObject(cache);DateTime checkedAt;object value;
        return !data.TryGetValue("checkedAt",out value) || !DateTime.TryParse(Convert.ToString(value),out checkedAt) || DateTime.UtcNow-checkedAt.ToUniversalTime()>TimeSpan.FromDays(1);
    }
    public static UpdateRelease CachedRelease(string cache){
        try{
            object value;var data=ReadObject(cache);if(!data.TryGetValue("release",out value))return null;
            var release=value as Dictionary<string,object>;if(release==null)return null;
            string version=Convert.ToString(Get(release,"Version"));string json;
            lock(JsonLock)json=Json.Serialize(new {tag_name="v"+version,draft=false,prerelease=false,assets=new[]{new{name="CodexQuotaManager-v"+version+"-windows-x64.zip",browser_download_url=Convert.ToString(Get(release,"Url")),digest="sha256:"+Convert.ToString(Get(release,"Sha256"))}}});
            return ParseRelease(json);
        }catch{return null;}
    }
}
