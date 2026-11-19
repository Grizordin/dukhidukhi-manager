using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public partial class ExtensionManager {
    static void PackageTest(string[] arguments) {
        var result=new Dictionary<string,object>();try {var package=ExtensionPackages.Unpack(File.ReadAllBytes(arguments[1]),arguments[3]=="-"?null:arguments[3],arguments[2]);result["ok"]=true;result["id"]=package.Id;result["name"]=package.Name;result["version"]=package.Version;result["folder"]=package.Folder;result["sha256"]=package.Sha256;}
        catch(Exception e){result["ok"]=false;result["error"]=e.Message;Environment.ExitCode=1;}
        File.WriteAllText(arguments[4],new JavaScriptSerializer().Serialize(result));
    }
    async Task RunExtensionProbe() {
        var result=new Dictionary<string,object>();try {
            if(args.Length<5)throw new IOException("Probe requires profile, extension folder and local test URL plus output path.");
            result["runtime"]=environment.BrowserVersionString;result["profile"]=profile;var extension=await web.CoreWebView2.Profile.AddBrowserExtensionAsync(Path.GetFullPath(args[2]));result["id"]=extension.Id;result["enabled"]=extension.IsEnabled;
            await RefreshExtensionList();web.CoreWebView2.Navigate(args[3]);await Task.Delay(3500);result["injected"]=await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.extensionManagerProbe||null");
            result["darkreader_style"]=await web.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('style.darkreader')");
            await extension.EnableAsync(false);result["disabled"]=!extension.IsEnabled;web.CoreWebView2.Reload();await Task.Delay(1000);result["after_disable"]=await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.extensionManagerProbe||null");
            await extension.EnableAsync(true);web.CoreWebView2.Reload();await Task.Delay(1000);result["after_enable"]=await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.extensionManagerProbe||null");
            var package=ExtensionPackages.ReadFolder(args[2]);result["popup_page"]=package.Page("popup");result["options_page"]=package.Page("options");
            foreach(var kind in new[]{"popup","options"}){var page=package.Page(kind);if(page!=null){web.CoreWebView2.Navigate("chrome-extension://"+extension.Id+"/"+page);await Task.Delay(700);result[kind+"_title"]=web.CoreWebView2.DocumentTitle;}}
            await RefreshExtensionList();
            result["layout"]=new {browser=web.Bounds.ToString(),status=status.Bounds.ToString()};
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(Path.GetDirectoryName(args[4]),"manager-layout.png"));}
            await extension.RemoveAsync();result["removed"]=!(await web.CoreWebView2.Profile.GetBrowserExtensionsAsync()).Any(x=>x.Id==extension.Id);
            result["api_probe"]=await web.CoreWebView2.ExecuteScriptAsync("typeof chrome.userScripts");
        }catch(Exception e){result["error"]=e.ToString();}
        File.WriteAllText(args[4],new JavaScriptSerializer().Serialize(result));Close();
    }
}
