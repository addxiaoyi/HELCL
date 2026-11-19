using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class TypographyAudit
{
    internal static readonly string[] Pages = { "launch", "instance", "version", "mod", "mods", "modpack", "datapack", "resourcepack", "shader", "world", "account", "java", "ai", "multiplayer", "servers", "playtime", "plugins", "feedback", "settings", "tasks" };
    internal static async Task Run(MainWindow window,string folder,bool live)
    {
        Directory.CreateDirectory(folder);Motion.Enabled=false;window.Title="HelL · 字体与图标检查";
        Console.WriteLine("[TYPE AUDIT] font="+Typography.Family+" dpi="+VisualTreeHelper.GetDpi(window).PixelsPerInchX);
        foreach(var mode in new[]{(Dark:false,Width:1180d,Height:740d),(Dark:true,Width:800d,Height:620d)})
        {
            App.ApplyTheme(mode.Dark);window.Width=mode.Width;window.Height=mode.Height;
            foreach(string page in Pages)
            {
                window.Navigate(page,instant:true);await Program.WaitForPage(window,page);await Task.Delay(300);window.UpdateLayout();
                if(page=="settings")CustomizationPreview.Walk(window).OfType<Rail>().Single().Select("look");
                window.UpdateLayout();
                Capture(window,Path.Combine(folder,page+"-"+(mode.Dark?"dark":"light")+".png"));
            }
            window.Navigate("settings",instant:true);await Program.WaitForPage(window,"settings");
            foreach(string section in new[]{"look","download","game","ai","launcher"})
            {
                CustomizationPreview.Walk(window).OfType<Rail>().Single().Select(section);
                foreach(var scroll in CustomizationPreview.Walk(window).OfType<SmoothScroll>().Where(scroll=>scroll.IsVisible))scroll.ScrollToTop();
                await MaterialControlsPreview.Layout(window);
                Capture(window,Path.Combine(folder,"settings-"+section+"-"+(mode.Dark?"dark":"light")+".png"));
                foreach(var scroll in CustomizationPreview.Walk(window).OfType<SmoothScroll>().Where(scroll=>scroll.IsVisible))scroll.ScrollToBottom();
                await MaterialControlsPreview.Layout(window);
                Capture(window,Path.Combine(folder,"settings-"+section+"-bottom-"+(mode.Dark?"dark":"light")+".png"));
            }
        }
        window.Navigate("settings",instant:true);await Program.WaitForPage(window,"settings");
        CustomizationPreview.Walk(window).OfType<Rail>().Single().Select("look");window.Width=1180;window.Height=740;
        foreach(var scroll in CustomizationPreview.Walk(window).OfType<SmoothScroll>().Where(scroll=>scroll.IsVisible))scroll.ScrollToTop();
        foreach(bool dark in new[]{false,true})
        {
            App.ApplyTheme(dark);await Task.Delay(120);
            await Controls(window,folder,dark);
        }
        Console.WriteLine("[TYPE AUDIT] Captured "+Pages.Length+" pages in light and dark plus controls. WPF captures omit DWM pixels.");
        if(live){App.ApplyTheme(false);await Task.Delay(100);_ = ShowControls();window.Activate();}
    }
    private static async Task Controls(MainWindow window,string folder,bool dark)
    {
        var layer=ShowControls();await MaterialControlsPreview.Layout(window);
        var controls=CustomizationPreview.Walk(layer.Card).OfType<FrameworkElement>().ToArray();
        foreach(var item in controls.OfType<TextBlock>().Where(text=>text.Text.Length>0&&text.Text.Length<25))
        {
            var ink=Ink(item);Console.WriteLine("[TEXT] "+item.Text+" size="+item.FontSize+" line="+item.LineHeight+" h="+item.ActualHeight+" ink="+ink+" family="+item.FontFamily.Source);
        }
        Capture(window,Path.Combine(folder,"controls-"+(dark?"dark":"light")+".png"));
        var combo=controls.OfType<ComboBox>().First();combo.IsDropDownOpen=true;await MaterialControlsPreview.Layout(window);
        if(combo.Template.FindName("PART_Popup",combo) is System.Windows.Controls.Primitives.Popup popup&&popup.Child is FrameworkElement child)
            Capture(child,Path.Combine(folder,"dropdown-"+(dark?"dark":"light")+".png"));
        combo.IsDropDownOpen=false;
        var menu=new ContextMenu{PlacementTarget=controls.OfType<Button>().First(),Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        menu.Items.Add(new MenuItem{Header="打开实例目录",Icon=Ui.Glyph("\ue8b7")});
        menu.Items.Add(new MenuItem{Header="刷新列表",Icon=Ui.Glyph("\ue72c"),InputGestureText="F5"});
        menu.Items.Add(new MenuItem{Header="固定到首页",IsCheckable=true,IsChecked=true});
        menu.IsOpen=true;await MaterialControlsPreview.Layout(window);
        Capture(menu,Path.Combine(folder,"menu-"+(dark?"dark":"light")+".png"));
        menu.IsOpen=false;layer.Dismiss();await MaterialControlsPreview.Layout(window);
    }
    internal static Dlg.Layer ShowControls()
    {
        var buttons=Ui.H(8,Ui.Btn("开始游戏",BtnKind.Primary,glyph:"\ue768"),Ui.Btn("下载",BtnKind.Ghost,glyph:"\ue896"),Ui.Btn("保存",BtnKind.Normal,glyph:"\ue74e"),Ui.IconBtn("\ue713","设置"));
        var combo=Ui.Combo(new[]{"Minecraft 1.21.1","Fabric · 生存世界","默认字体 ABC 0123"},"Minecraft 1.21.1");
        var body=Ui.V(14,Ui.Txt("清晰的中文 · Minecraft 1.21.1",24,true),Ui.H(14,Ui.Glyph("\ue713",14),Ui.Txt("图标与文字应在同一视觉中心",14)),
            Ui.H(14,Ui.Glyph("\ue896",12),Ui.Small("下载资源 · 100% · ABCxyz 0123456789")),buttons,
            Ui.Field("输入框",Ui.Input("输入名称或版本号…","Minecraft 1.21.1"),labelWidth:90),Ui.Field("下拉选择",combo,labelWidth:90),
            Ui.Field("占位提示",Ui.Input("输入名称或版本号…"),labelWidth:90),
            Ui.H(20,Ui.Check("保留原始文件",true),Ui.Switch(true)),Ui.H(8,Ui.Small("小号说明 12 px"),Ui.Txt("标准正文 14 px",14),Ui.Txt("章节标题 18 px",18,true)));
        return Dlg.Panel("字体与图标检查",body,720,footer:Ui.Btn("完成检查",BtnKind.Primary));
    }
    internal static Rect Ink(Visual visual)
    {
        Rect bounds=Rect.Empty;var drawing=VisualTreeHelper.GetDrawing(visual);if(drawing==null)return bounds;
        void Walk(Drawing item)
        {
            if(item is GlyphRunDrawing glyph){var ink=glyph.GlyphRun.ComputeInkBoundingBox();if(ink.IsEmpty)return;ink.Offset(glyph.GlyphRun.BaselineOrigin.X,glyph.GlyphRun.BaselineOrigin.Y);bounds.Union(ink);}
            else if(item is DrawingGroup group)foreach(var child in group.Children)Walk(child);
        }
        Walk(drawing);return bounds;
    }
    internal static void Capture(FrameworkElement element,string path)
    {
        // Popup margins belong to its separate presentation root, outside the child's size.
        var surface=element is Window?element:PresentationSource.FromVisual(element)?.RootVisual as FrameworkElement??element;
        Program.SaveSnapshot(surface,path,VisualTreeHelper.GetDpi(element).DpiScaleX);
    }
}
