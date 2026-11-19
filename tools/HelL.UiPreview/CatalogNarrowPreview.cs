using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class CatalogNarrowPreview
{
    private static int _checks;
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);_checks++;Console.WriteLine("[PASS] "+message);}
    private static void Invoke(object page,string method,params object[] args)=>page.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,args);
    private static void Fits(FrameworkElement control,FrameworkElement parent)
    {
        var origin=control.TranslatePoint(new Point(),parent);Check(origin.X>=-1&&origin.X+control.ActualWidth<=parent.ActualWidth+1,"control fits without horizontal clipping: "+control.GetType().Name);
    }
    internal static async Task Run(MainWindow window,string folder)
    {
        Directory.CreateDirectory(folder);Motion.Enabled=false;window.Width=800;window.Height=620;
        foreach(var kind in CatalogKind.All)
        {
            window.Navigate(kind.Key,instant:true);await Program.WaitForPage(window,kind.Key);
            var page=CustomizationPreview.Walk(window).OfType<CatalogPage>().Single();var results=MaterialControlsPreview.Field<ListBox>(page,"_results");
            await MaterialControlsPreview.Until(()=>results.Items.Count>0,"real catalog results arrive: "+kind.Key,25);
            Invoke(page,"CancelSearch");var first=results.Items.Cast<CatalogItem>().First();
            Check(first.IconUrl.Length>0,"upstream icon URL is attached to "+kind.Key);
            await MaterialControlsPreview.Until(()=>CustomizationPreview.Walk(results).OfType<ProjectCover>().Any(cover=>cover.Ready),"genuine project icon decoded: "+kind.Key,25);
            var filters=CustomizationPreview.Walk(page).OfType<Expander>().Single();var listHost=MaterialControlsPreview.Field<Grid>(page,"_listHost");
            foreach(bool dark in new[]{false,true})
            {
                App.ApplyTheme(dark);filters.IsExpanded=true;await MaterialControlsPreview.Layout(window);
                Check(listHost.ActualHeight>=140,"expanded filters retain visible results: "+kind.Key);
                var scroll=(SmoothScroll)filters.Content;Check(scroll.ActualHeight<=240,"filters scroll within a bounded region: "+kind.Key);
                foreach(var combo in CustomizationPreview.Walk(filters).OfType<ComboBox>())Fits(combo,scroll);
                scroll.ScrollToBottom();await MaterialControlsPreview.Layout(window);
                var more=CustomizationPreview.Named(page,"更多操作");var position=more.TranslatePoint(new Point(),page);
                Check(more.IsVisible&&position.Y+more.ActualHeight<=page.ActualHeight+1,"catalog actions stay reachable in the top toolbar: "+kind.Key);
                Program.SaveSnapshot(window,Path.Combine(folder,kind.Key+"-filters-"+dark+".png"));
                filters.IsExpanded=false;await MaterialControlsPreview.Layout(window);
                foreach(var button in CustomizationPreview.Walk(results).OfType<ButtonBase>().Where(button=>button.IsVisible&&button.ActualWidth>0))Fits(button,results);
                Program.SaveSnapshot(window,Path.Combine(folder,kind.Key+"-results-"+dark+".png"));
            }
            filters.IsExpanded=true;window.Width=1180;await MaterialControlsPreview.Layout(window);Check(filters.IsExpanded,"wide catalog keeps filters open");
            window.Width=800;await MaterialControlsPreview.Layout(window);Check(filters.IsExpanded,"returning to narrow width preserves filter preference");
            filters.IsExpanded=false;
            var longTitle=JsonSerializer.Deserialize<CatalogItem>(JsonSerializer.Serialize(first))!;
            longTitle.Name="用于验证长标题换行的真实项目名称 · "+first.Name+" · "+first.Name;
            longTitle.ChineseName=longTitle.Name;longTitle.Localization="中文简介";
            Invoke(page,"ShowRows",new object[]{new[]{longTitle}});await MaterialControlsPreview.Layout(window);
            Check(CustomizationPreview.Walk(results).OfType<TextBlock>().Any(text=>text.Text==longTitle.Name&&text.TextWrapping==TextWrapping.Wrap),"long resource title wraps: "+kind.Key);
            Invoke(page,"ShowFailure","网络暂不可用","这是界面错误状态验收，不代表真实请求失败。");await MaterialControlsPreview.Layout(window);Fits(CustomizationPreview.Named(page,"重试"),page);
            Invoke(page,"ShowRows",new object[]{new[]{first}});await MaterialControlsPreview.Layout(window);Check(results.Items.Count==1,"catalog recovers its results after error state: "+kind.Key);
        }
        Console.WriteLine("[CATALOG NARROW] "+_checks+" checks passed");
    }
}
