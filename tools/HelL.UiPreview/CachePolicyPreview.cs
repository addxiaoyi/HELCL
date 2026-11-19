using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class CachePolicyPreview
{
    private static DependencyObject Top => Dlg.Host!.Children[Dlg.Host.Children.Count-1];
    private static void Click(string name) => CustomizationPreview.Named(Top,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);Console.WriteLine("[PASS] "+message);}
    private static string Entry(string folder,char key,int days)
    {
        string file=Path.Combine(folder,new string(key,64)+".mp4");File.WriteAllBytes(file,new byte[2048]);File.WriteAllText(file+".size","2048");File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-days));return file;
    }
    internal static async Task Run(MainWindow window,string folder)
    {
        Directory.CreateDirectory(folder);Motion.Enabled=false;window.Width=800;window.Height=620;
        string directory=Path.Combine(Utils.ROOT,"cache","wallpaper-video");Directory.CreateDirectory(directory);
        string expired=Entry(directory,'a',15), recent=Entry(directory,'b',2), active=Entry(directory,'c',15);
        using(var held=new WallpaperVideoCache(directory).HoldPlayback(active))
        {
            await WallpaperDialog.Show();await MaterialControlsPreview.Layout(window);Click("缓存策略…");await MaterialControlsPreview.Layout(window);
            var combos=CustomizationPreview.Walk(Top).OfType<ComboBox>().ToArray();Check(combos.Length==2,"policy dialog exposes capacity and retention");
            combos[0].SelectedItem="1024 MiB";combos[1].SelectedItem="7 天未使用后清理";Click("保存策略");
            await MaterialControlsPreview.Until(()=>Dlg.Host!.Children.Count==1,"saving policy returns to wallpaper editor");
            Check(await AppServices.Client.CallAsync<int>("get_setting",new{key="ui_wallpaper_cache_mib"})==1024,"capacity persisted through settings API");
            Check(await AppServices.Client.CallAsync<int>("get_setting",new{key="ui_wallpaper_cache_days"})==7,"retention persisted through settings API");
            Check(!File.Exists(expired)&&File.Exists(recent)&&File.Exists(active),"saving policy prunes expired idle entries and retains active video");
            await Task.Delay(1200);var panel=CustomizationPreview.Walk(Top).OfType<WallpaperCachePanel>().Single();panel.BringIntoView();await MaterialControlsPreview.Layout(window);
            Check(MaterialControlsPreview.Field<TextBlock>(panel,"_usage").Text.Contains("1024 MiB"),"cache usage reflects the configured capacity");
            Program.SaveSnapshot(window,Path.Combine(folder,"policy-saved.png"));
            Click("缓存策略…");await MaterialControlsPreview.Layout(window);combos=CustomizationPreview.Walk(Top).OfType<ComboBox>().ToArray();
            Check(combos[0].SelectedItem.ToString()=="1024 MiB"&&combos[1].SelectedItem.ToString()=="7 天未使用后清理","reopening restores both choices");
            foreach(bool dark in new[]{false,true}){App.ApplyTheme(dark);await MaterialControlsPreview.Layout(window);Program.SaveSnapshot(window,Path.Combine(folder,"policy-"+dark+".png"));}
            combos[0].SelectedItem="4096 MiB";Click("取消");Check(Wallpaper.CachePolicy.CapacityMiB==1024,"cancel retains saved policy");Click("关闭");
        }
        await Wallpaper.ReloadAsync();Check(!File.Exists(active)&&File.Exists(recent),"reload enforces retention after playback lease ends");
        await AppServices.Client.CallAsync("save_settings",new{data=new Dictionary<string,object>{{"ui_wallpaper_cache_mib",99999},{"ui_wallpaper_cache_days",-8}}});
        await Wallpaper.ReloadAsync();Check(Wallpaper.CachePolicy.CapacityMiB==4096&&Wallpaper.CachePolicy.KeepDays==0,"backend clamps invalid policy values");
        await AppServices.Client.CallAsync("save_settings",new{data=new Dictionary<string,object>{{"ui_wallpaper_cache_mib",512},{"ui_wallpaper_cache_days",0}}});await Wallpaper.ReloadAsync();
        Console.WriteLine("[CACHE POLICY] runtime checks passed");
    }
}
