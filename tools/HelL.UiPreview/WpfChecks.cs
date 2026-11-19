using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HelL;
using HelL.Services;
using HelL.Pages;
using HelL.Models;

internal static class WpfChecks
{
    private static readonly Grid Host = new Grid();
    private static readonly Button Origin = Ui.Btn("Origin");
    private static int _passed;

    internal static int Run(Application app)
    {
        Motion.Enabled = false;
        var stage = new Grid();
        stage.Children.Add(Origin);
        stage.Children.Add(Host);
        var window = new Window
        {
            Width = 900, Height = 580,
            Content = new AdornerDecorator { Child = stage },
            Title = "HelL · WPF interaction checks"
        };
        Dlg.Host = Host;
        app.MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            try
            {
                window.Activate();
                MaterialChecks();
                await DialogChecks();
                await FormChecks();
                await ControlChecks(stage);
                await CatalogChecks(stage);
                await InstanceHeaderChecks(stage);
                await VersionRowChecks(stage);
                await LaunchChecks(stage);
                await LaunchWorkspaceChecks(stage);
                await DashboardChecks(stage);
                await FileChecks(window);
                await PageChecks(stage);
                await MotionChecks(stage);
                Console.WriteLine($"[WPF PASS] {_passed} checks");
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                Program.WriteError(ex);
                app.Shutdown(1);
            }
        };
        return app.Run(window);
    }

    private static void MaterialChecks()
    {
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            Check(Ui.Res("B.WindowBase") is SolidColorBrush fallback && fallback.Color.A == 255 && fallback.Opacity == 1,
                $"{(dark ? "Dark" : "Light")} shell has an opaque fallback before native material is available");
            double lowest = double.MaxValue;
            foreach (Color backdrop in new[] { Colors.Black, Colors.White })
            {
                foreach (string surface in new[] { "B.PageWash", "B.Glass", "B.GlassStrong", "B.Popup", "B.ControlFill" })
                {
                    var visual = new DrawingVisual();
                    using (DrawingContext drawing = visual.RenderOpen())
                    {
                        var bounds = new Rect(0, 0, 100, 100);
                        drawing.DrawRectangle(new SolidColorBrush(backdrop), null, bounds);
                        if (surface == "B.ControlFill") drawing.DrawRectangle(Ui.Res("B.PageWash"), null, bounds);
                        drawing.DrawRectangle(Ui.Res(surface), null, bounds);
                        if (surface == "B.Glass" || surface == "B.GlassStrong") drawing.DrawRectangle(Ui.Res("B.GlassSheen"), null, bounds);
                    }
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    foreach (int sample in new[] { 1, 25, 50, 75, 98 })
                    {
                        byte[] pixel = new byte[4];
                        bitmap.CopyPixels(new Int32Rect(sample, sample, 1, 1), pixel, 4, 0);
                        Color background = Color.FromRgb(pixel[2], pixel[1], pixel[0]);
                        foreach (string ink in new[] { "B.Ink", "B.InkSoft", "B.InkMuted", "B.InkFaint" })
                        {
                            double a = Luminance(((SolidColorBrush)Ui.Res(ink)).Color);
                            double b = Luminance(background);
                            double ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
                            lowest = Math.Min(lowest, ratio);
                            if (ratio < 4.5) throw new InvalidOperationException($"[WPF FAIL] Glass contrast {ratio:F2}: dark={dark}, {surface}, {ink}, backdrop={backdrop}, sample={sample}");
                        }
                    }
                }
            }
            Check(lowest >= 4.5, $"{(dark ? "Dark" : "Light")} glass text stays readable over black and white backdrops (min {lowest:F2}:1)");
        }
        Check(!System.Text.Json.JsonSerializer.Deserialize<UiPrefs>("{\"SideFrost\":false}")!.SideFrost,
            "A saved choice to disable sidebar frost survives the new default");
        App.ApplyTheme(false);

        static double Luminance(Color color)
        {
            double Channel(byte channel)
            {
                double value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
    }

    private static async Task DialogChecks()
    {
        Origin.Focus();
        Task<bool> cancelled = Dlg.Confirm("Cancel", "Retain the original page");
        await Layout();
        Key(Top(), System.Windows.Input.Key.Escape);
        Check(await cancelled == false && Host.Children.Count == 0, "Escape completes and removes a dialog");
        Check(Origin.IsKeyboardFocused, "Dismiss restores the previous focus");

        Task<string?> prompt = Dlg.Prompt("Prompt", "Name", "Keep me");
        await Layout();
        var mask = (Border)Top().Children[0];
        mask.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        });
        Check(await prompt == null, "Backdrop cancellation completes prompt");

        Task<bool> lower = Dlg.Confirm("Lower", "First layer");
        await Layout();
        Grid lowerRoot = Top();
        Task<bool> upper = Dlg.Confirm("Upper", "Second layer");
        await Layout();
        Key(lowerRoot, System.Windows.Input.Key.Escape);
        Check(!lower.IsCompleted && !upper.IsCompleted, "Covered dialogs ignore Escape");
        Key(Top(), System.Windows.Input.Key.Escape);
        Check(await upper == false && !lower.IsCompleted, "Escape dismisses only the top layer");
        Key(Top(), System.Windows.Input.Key.Escape);
        await lower;

        Task<bool> danger = Dlg.Confirm("Delete", "Irreversible action", danger: true);
        await Layout();
        Check(FindButton("取消").IsKeyboardFocused, "Dangerous confirmation focuses Cancel");
        Key(FindButton("取消"), System.Windows.Input.Key.Enter);
        Check(!danger.IsCompleted || danger.Result == false, "Enter on Cancel never confirms a dangerous action");
        if (!danger.IsCompleted) Click(FindButton("取消"));
        await danger;

        var multiline = Ui.Input("", "Line one");
        multiline.AcceptsReturn = true;
        Task<bool> edit = Dlg.Ask("Multiline", multiline);
        await Layout();
        multiline.Focus();
        Key(multiline, System.Windows.Input.Key.Enter);
        Check(!edit.IsCompleted, "Multiline Enter does not submit");
        Button confirm = FindButton("确定");
        confirm.IsEnabled = false;
        multiline.AcceptsReturn = false;
        Key(multiline, System.Windows.Input.Key.Enter);
        Check(!edit.IsCompleted, "Disabled confirmation does not submit");
        Key(Top(), System.Windows.Input.Key.Escape);
        await edit;

        var selector = Ui.Combo(new[] { "One", "Two" }, width: 220, editable: true);
        Task<bool> selection = Dlg.Ask("Select", selector);
        await Layout();
        selector.Focus();
        selector.IsDropDownOpen = true;
        await Layout();
        Key(selector, System.Windows.Input.Key.Enter);
        Check(!selection.IsCompleted, "Enter in an open selector does not submit its dialog");
        selector.IsDropDownOpen = true;
        selector.Focus();
        await Layout();
        Key(selector, System.Windows.Input.Key.Escape);
        Check(!selection.IsCompleted, "Escape in an open selector keeps its dialog and input intact");
        selector.IsDropDownOpen = false;
        Key(Top(), System.Windows.Input.Key.Escape);
        await selection;

        var tallBody = Ui.V(10, Enumerable.Range(0, 40).Select(index => (UIElement)Ui.Txt("Setting " + index)).ToArray());
        Task<bool> tall = Dlg.Ask("Long settings form", tallBody, width: 680);
        await Layout();
        Button accept = FindButton("确定");
        Point footer = accept.TranslatePoint(new Point(), Host);
        Check(footer.Y >= 0 && footer.Y + accept.ActualHeight <= Host.ActualHeight,
            "Long dialogs keep their footer within the window");
        Check(Descendants(Top()).OfType<ScrollViewer>().Any(scroll => scroll.ScrollableHeight > 0),
            "Long dialog bodies scroll independently");
        Key(Top(), System.Windows.Input.Key.Escape);
        await tall;
    }

    private static async Task FormChecks()
    {
        var name = Ui.Input("Name");
        int saves = 0;
        var saveGate = new TaskCompletionSource<bool>();
        Task<bool> form = Dlg.EditAsync("Edit", Ui.Field("Name", name), async () =>
        {
            Dlg.Require(!string.IsNullOrWhiteSpace(name.Text), name, "Enter a name");
            saves++;
            if (saves == 1) throw new InvalidOperationException("Test save failure");
            await saveGate.Task;
        });
        await Layout();
        Button submit = FindButton("保存");
        Click(submit);
        await Layout();
        Check(!form.IsCompleted && name.IsEnabled && name.IsKeyboardFocused, "Invalid form stays open and focuses its field");
        Check(FieldState.GetError(name) == "Enter a name", "Validation error is associated with its field");
        var outline = (Border)name.Template.FindName("Fx", name);
        Check(outline.Opacity == 1 && outline.BorderBrush == Ui.Res("B.Danger"), "Invalid field displays its error outline");
        name.Text = "Retained name";
        Check(string.IsNullOrEmpty(FieldState.GetError(name)), "Editing clears the field error");
        Click(submit);
        await Layout();
        Check(!form.IsCompleted && name.Text == "Retained name" && submit.IsEnabled, "Save failure preserves input and enables retry");
        Click(submit);
        Click(submit);
        Key(Top(), System.Windows.Input.Key.Escape);
        Check(saves == 2 && !submit.IsEnabled && !form.IsCompleted, "Pending save blocks double submission and dismissal");
        saveGate.SetResult(true);
        Check(await form, "Successful retry completes the form");
        await Layout();
        Check(Host.Children.Count == 0, "Successful save removes the dialog");
    }

    private static async Task ControlChecks(Grid stage)
    {
        var combo = Ui.Combo(null, null, 220, "Choose a model", editable: true);
        stage.Children.Insert(0, combo);
        await Layout();
        var placeholder = (TextBlock)combo.Template.FindName("Ph", combo);
        Check(placeholder.Visibility == Visibility.Visible, "Empty editable selector shows its placeholder");
        combo.Text = "custom-model";
        await Layout();
        Check(placeholder.Visibility == Visibility.Collapsed, "Custom model text hides the selector placeholder");
        stage.Children.Remove(combo);

        var first = new Border { Height = 60 };
        var second = new Border { Height = 80 };
        var columns = new AdaptiveColumns(first, second, 320, 850);
        columns.Measure(new Size(1000, 600));
        columns.Arrange(new Rect(0, 0, 1000, 600));
        Check(Grid.GetColumn(second) == 2, "Wide content uses two columns");
        columns.Measure(new Size(600, 600));
        columns.Arrange(new Rect(0, 0, 600, 600));
        Check(Grid.GetRow(second) == 2 && second.ActualWidth <= 600, "Narrow content stacks within the viewport");
        columns.Measure(new Size(1000, 600));
        columns.Arrange(new Rect(0, 0, 1000, 600));
        Check(Grid.GetRow(second) == 0 && Grid.GetColumn(second) == 2, "Widening restores two columns");

        var menu = new ContextMenu { PlacementTarget = Origin };
        foreach (int index in Enumerable.Range(0, 60)) menu.Items.Add(new MenuItem { Header = "Version " + index });
        menu.IsOpen = true;
        await Layout();
        var menuScroll = Descendants(menu).OfType<ScrollViewer>().Single();
        Console.WriteLine($"[MENU] height={menu.ActualHeight}, viewport={stage.ActualHeight}, scrollable={menuScroll.ScrollableHeight}");
        Check(menuScroll.ScrollableHeight > 0 && menu.ActualHeight <= stage.ActualHeight, "Long menus stay within a bounded scrollable popup");
        menuScroll.ScrollToEnd();
        await Layout();
        var last = (MenuItem)menu.Items[59];
        Point corner = last.TranslatePoint(new Point(), menuScroll);
        Check(corner.Y >= 0 && corner.Y + last.ActualHeight <= menuScroll.ActualHeight + 1,
            "The final command remains reachable in a long menu");
        menu.IsOpen = false;
    }

    private static async Task CatalogChecks(Grid stage)
    {
        var catalog = new CatalogPage(CatalogKind.Shader);
        MethodInfo card = typeof(CatalogPage).GetMethod("ResultCard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entries = Enumerable.Range(0, 600).Select(index => new CatalogItem
        {
            Name = "Complementary Shaders - Reimagined " + index,
            Author = "An author with a particularly long display name",
            Description = "Preserving the elements of Minecraft with exceptional quality, detail, and performance. " + new string('W', 160),
            Source = "modrinth",
            Downloads = 71900000,
            Tags = new List<string> { "colored-lighting", "iris", "optifine", "vanilla-like", "high-performance" }
        }).ToList();
        var list = Ui.VirtualList<CatalogItem>(entry => (UIElement)card.Invoke(catalog, new object[] { entry })!);
        list.ItemsSource = entries;
        list.Width = 630;
        stage.Children.Insert(0, list);
        foreach (double width in new[] { 630.0, 850.0, 480.0 })
        {
            list.Width = width;
            await Layout();
            var viewport = Descendants(list).OfType<ScrollContentPresenter>().First();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var install = Named<Button>(row, "安装");
            Point corner = install.TranslatePoint(new Point(), viewport);
            Check(corner.X >= 0 && corner.X + install.ActualWidth <= viewport.ActualWidth + 0.5,
                $"Catalog install action stays inside {width}px viewport (right={corner.X + install.ActualWidth:F1}, viewport={viewport.ActualWidth:F1})");
            Check(Descendants(list).OfType<ListBoxItem>().Count() < 50, "Catalog preserves row virtualization at " + width);
        }
        stage.Children.Remove(list);

        catalog.Width = 636;
        stage.Children.Insert(0, catalog);
        await Layout();
        var filters = Descendants(catalog).OfType<Expander>().Single();
        Check(!filters.IsExpanded, "Narrow catalog starts with collapsed filters");
        filters.IsExpanded = true;
        await Layout();
        Button more = Named<Button>(catalog, "更多操作");
        Click(more);
        await Layout();
        ContextMenu menu = more.ContextMenu;
        Check(menu != null && menu.IsOpen && menu.PlacementTarget == more, "Catalog actions open from a visible menu anchor");
        var link = menu!.Items.OfType<MenuItem>().Single(item => item.Header.ToString() == "链接安装");
        menu.IsOpen = false;
        link.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Layout();
        Check(Host.Children.Count == 1 && !more.IsEnabled, "Link installation opens one prompt and blocks repeated menu actions");
        link.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Layout();
        Check(Host.Children.Count == 1, "Repeated link installation does not stack prompts");
        Key(Top(), System.Windows.Input.Key.Escape);
        await Layout();
        Check(Host.Children.Count == 0 && more.IsEnabled, "Cancelling link installation restores menu access");
        stage.Children.Remove(catalog);
    }

    private static async Task InstanceHeaderChecks(Grid stage)
    {
        var page = new InstancePage();
        stage.Children.Insert(0, page);
        foreach (double width in new[] { 920.0, 636.0, 480.0 })
        {
            page.Width = width;
            await Layout();
            Button create = Descendants(page).OfType<Button>().Single(button => button.IsVisible && AutomationProperties.GetName(button) == "新建实例");
            var search = (TextBox)typeof(InstancePage).GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Point createCorner = create.TranslatePoint(new Point(), page);
            Point searchCorner = search.TranslatePoint(new Point(), page);
            Check(createCorner.Y + create.ActualHeight <= searchCorner.Y,
                $"Instance creation stays above the filter at {width}px");
            Check(search.ActualWidth >= 160, $"Instance filter leaves room for names at {width}px");
            foreach (FrameworkElement control in new FrameworkElement[] { create, search, Named<Button>(page, "更多操作") })
            {
                Point corner = control.TranslatePoint(new Point(), page);
                Check(corner.X >= 0 && corner.X + control.ActualWidth <= page.ActualWidth + 1,
                    $"Instance toolbar control fits {width}px: {AutomationProperties.GetName(control)}");
            }
        }
        Button newInstance = Descendants(page).OfType<Button>().Single(button => button.IsVisible && AutomationProperties.GetName(button) == "新建实例");
        newInstance.Focus();
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(newInstance), 0, System.Windows.Input.Key.Down)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        newInstance.RaiseEvent(down);
        Check(!down.Handled, "Instance list shortcuts do not intercept focused toolbar buttons");
        Button more = Named<Button>(page, "更多操作");
        Click(more);
        await Layout();
        Check(more.ContextMenu.IsOpen && more.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header.ToString())
            .SequenceEqual(new[] { "打开游戏目录", "导入官方启动器", "刷新", "重命名当前分组…" }), "Instance menu preserves directory, import, refresh, and group actions");
        more.ContextMenu.IsOpen = false;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(InstancePage).GetField("_rows", hidden)!.SetValue(page, new List<InstanceInfo> { new InstanceInfo { Name = "UI fixture" } });
        typeof(InstancePage).GetMethod("Render", hidden, null, Type.EmptyTypes, null)!.Invoke(page, null);
        await Layout();
        var list = (Panel)typeof(InstancePage).GetField("_list", hidden)!.GetValue(page)!;
        list.Focus();
        Key(list, System.Windows.Input.Key.Down);
        Check((string?)typeof(InstancePage).GetField("_selected", hidden)!.GetValue(page) == "UI fixture",
            "Focused instance lists retain arrow-key selection");
        stage.Children.Remove(page);
    }

    private static async Task VersionRowChecks(Grid stage)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = new VersionPage();
        Type installedType = typeof(VersionPage).GetNestedType("Installed", BindingFlags.NonPublic)!;
        object installed = Activator.CreateInstance(installedType, nonPublic: true)!;
        installedType.GetField("Id")!.SetValue(installed, "1.21.1-Fabric-Survival-with-a-long-version-name");
        installedType.GetField("Loader")!.SetValue(installed, "Fabric");
        installedType.GetField("Mc")!.SetValue(installed, "1.21.1");
        installedType.GetField("Mods")!.SetValue(installed, 125);
        var row = (FrameworkElement)typeof(VersionPage).GetMethod("VersionCard", hidden)!.Invoke(page, new[] { installed })!;
        var frame = new Border { Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        stage.Children.Insert(0, frame);
        foreach (double width in new[] { 920.0, 636.0, 480.0 })
        {
            frame.Width = width;
            await Layout();
            foreach (var button in Descendants(row).OfType<Button>())
            {
                Point point = button.TranslatePoint(new Point(), row);
                Check(point.X >= 0 && point.X + button.ActualWidth <= row.ActualWidth + 1,
                    $"Installed-version action fits {width}px: {AutomationProperties.GetName(button)}");
            }
        }
        stage.Children.Remove(frame);
    }

    private static async Task LaunchChecks(Grid stage)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var launch = new LaunchPage();
        T Field<T>(string name) => (T)typeof(LaunchPage).GetField(name, hidden)!.GetValue(launch)!;
        var banner = (FrameworkElement)typeof(LaunchPage).GetMethod("BuildBanner", hidden)!.Invoke(launch, null)!;
        stage.Children.Insert(0, banner);
        typeof(LaunchPage).GetMethod("SyncBanner", hidden)!.Invoke(launch, null);
        await Layout();
        var stop = Field<Button>("_stopBtn");
        var start = Field<Button>("_launchBtn");
        Check(!stop.IsVisible && !stop.IsEnabled, "Idle launch banner hides its stop action");
        Check(Field<Border>("_bBadge").Visibility == Visibility.Collapsed && Field<Border>("_healthBadge").Visibility == Visibility.Collapsed,
            "An unselected version has no duplicate badge or health grade");
        typeof(LaunchPage).GetField("_taskId", hidden)!.SetValue(launch, "launch-fixture");
        start.IsEnabled = false;
        stop.IsEnabled = true;
        launch.OnEvent(new BridgeEvent { Event = "progress", TaskId = "launch-fixture", Current = 25, Total = 100 });
        await Layout();
        Check(stop.IsVisible && !start.IsEnabled, "Preparing a launch keeps its stop action available");
        launch.OnEvent(new BridgeEvent { Event = "game_exited", TaskId = "another-game" });
        Check(!start.IsEnabled, "Another game's exit cannot enable the active launch");
        launch.OnEvent(new BridgeEvent { Event = "game_started" });
        await Layout();
        Check(stop.IsVisible && !start.IsEnabled && Descendants(start).OfType<TextBlock>().Any(text => text.Text == "游戏运行中"),
            "Legacy game-started events retain Stop and clearly label the running status");
        launch.OnEvent(new BridgeEvent { Event = "finished", TaskId = "launch-fixture", Success = false, Message = "已取消" });
        await Layout();
        Check(!stop.IsVisible && !start.IsEnabled, "Finishing without a selected version hides Stop and keeps launch unavailable");
        typeof(LaunchPage).GetField("_syncing", hidden)!.SetValue(launch, true);
        Field<ComboBox>("_ver").Items.Add("1.20.1");
        Field<ComboBox>("_ver").SelectedIndex = 0;
        typeof(LaunchPage).GetField("_taskId", hidden)!.SetValue(launch, "launch-next");
        stop.IsEnabled = true;
        launch.OnEvent(new BridgeEvent { Event = "finished", TaskId = "launch-next", Success = true });
        await Layout();
        Check(start.IsEnabled && !stop.IsVisible, "A completed launch with a selected version can be started again");
        stage.Children.Remove(banner);
    }

    private static async Task LaunchWorkspaceChecks(Grid stage)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var launch = new LaunchPage();
        var apply = typeof(LaunchPage).GetMethod("ApplyLayoutState", hidden)!;
        using var state = System.Text.Json.JsonDocument.Parse("{}");
        apply.Invoke(launch, new object[] { state.RootElement });
        stage.Children.Insert(0, launch);
        await Layout();
        Check(Descendants(launch).OfType<DashHost.CardView>().Count() == 0 && ((DashHost)typeof(LaunchPage).GetField("_dash", hidden)!.GetValue(launch)!).Layout.Items.Count == 6,
            "The default desktop layout preserves the six editable modules without fixed canvas frames");
        T Field<T>(string name) => (T)typeof(LaunchPage).GetField(name, hidden)!.GetValue(launch)!;
        Button Action(string name) => Descendants(launch).OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);
        Check(Action("安装 Minecraft").IsVisible && !Field<Button>("_launchBtn").IsVisible,
            "An empty library offers installation instead of a disabled launch button");
        var edit = Action("编辑布局");
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout();
        Check(Descendants(launch).OfType<DashHost.CardView>().Count() == 6, "Layout editing retains all six existing widgets");
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout();
        Check(Descendants(launch).OfType<DashHost.CardView>().Count() == 0 && Field<DashHost>("_dash").Layout.Items.Count == 6 && !Field<ComboBox>("_ver").IsVisible,
            "Leaving layout editing preserves all modules and hides unusable empty-library selectors");

        typeof(LaunchPage).GetField("_syncing", hidden)!.SetValue(launch, true);
        Field<ComboBox>("_inst").Items.Add("My Minecraft instance with a long localized name");
        Field<ComboBox>("_inst").SelectedIndex = 0;
        Field<ComboBox>("_ver").Items.Add("1.21.1-Fabric-0.16.10-Survival");
        Field<ComboBox>("_ver").SelectedIndex = 0;
        Field<ComboBox>("_acc").Items.Add("Alex");
        Field<ComboBox>("_acc").SelectedIndex = 0;
        Field<ComboBox>("_java").Items.Add("Java 21 (64-bit)");
        Field<ComboBox>("_java").SelectedIndex = 0;
        Field<Border>("_healthBadge").Visibility = Visibility.Collapsed;
        Field<Border>("_bBadge").Visibility = Visibility.Collapsed;
        Field<SPanel>("_newsHost").Children.Add(Ui.Muted("暂无新闻"));
        Field<TextBlock>("_bTitle").Text = "1.21.1 · Fabric Survival";
        Field<TextBlock>("_bSub").Text = "Minecraft Java Edition";
        typeof(LaunchPage).GetMethod("SyncLaunchAction", hidden)!.Invoke(launch, null);
        await Layout();
        Check(!Field<Button>("_installBtn").IsVisible && Field<Button>("_launchBtn").IsVisible,
            "Selecting a version restores the launch action");
        Action("高级启动设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var folds = Descendants(launch).OfType<Expander>().ToList();
        foreach (var fold in folds)
        {
            fold.ApplyTemplate();
            var header = (ToggleButton)fold.Template.FindName("HeaderSite", fold);
            Check(header.Focusable && AutomationProperties.GetName(header) == fold.Header.ToString(),
                "Disclosure has a keyboard focus target and visible name: " + fold.Header);
            var peer = new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(header);
            ((System.Windows.Automation.Provider.IToggleProvider)peer).Toggle();
            Check(fold.IsExpanded, "Disclosure opens through its accessible toggle: " + fold.Header);
        }
        await Layout();
        TextBlock emptyLog = Descendants(launch).OfType<TextBlock>().Single(text => text.Text == "启动游戏后，运行日志会显示在这里。");
        Check(emptyLog.Visibility == Visibility.Visible, "An expanded empty game log explains when output appears");
        Field<TextBox>("_log").Text = "[Preview fixture] Starting Minecraft";
        await Layout();
        Check(emptyLog.Visibility == Visibility.Collapsed, "Game output replaces the empty log hint");
        Field<TextBox>("_log").Clear();
        foreach (double width in new[] { 920.0, 636.0, 480.0 })
        {
            launch.Width = width;
            await Layout();
            foreach (string name in new[] { "_inst", "_ver", "_acc", "_java", "_server" })
            {
                FrameworkElement control = Field<FrameworkElement>(name);
                Point corner = control.TranslatePoint(new Point(), launch);
                Check(control.IsVisible && corner.X >= 0 && corner.X + control.ActualWidth <= launch.ActualWidth + 1,
                    $"Expanded launch field {name} fits {width}px");
            }
        }
        string? screenshotFolder = Environment.GetEnvironmentVariable("HELL_UI_CHECK_SCREENSHOTS");
        if (!string.IsNullOrEmpty(screenshotFolder))
        {
            launch.SetResourceReference(Control.BackgroundProperty, "B.Canvas");
            var scroll = Descendants(launch).OfType<ScrollViewer>().First();
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark);
                foreach (double width in new[] { 920.0, 636.0 })
                {
                    launch.Width = width;
                    launch.Height = 680;
                    stage.Width = width;
                    stage.Height = 680;
                    await Layout();
                    scroll.ScrollToTop();
                    await Layout();
                    Program.SaveSnapshot(launch, Path.Combine(screenshotFolder, $"expanded-{(dark ? "dark" : "light")}-{width}.png"));
                    scroll.ScrollToBottom();
                    await Layout();
                    Program.SaveSnapshot(launch, Path.Combine(screenshotFolder, $"log-{(dark ? "dark" : "light")}-{width}.png"));
                }
            }
            App.ApplyTheme(false);
            stage.Width = double.NaN;
            stage.Height = double.NaN;
            launch.Height = double.NaN;
        }
        launch.Width = double.NaN;
        var dashboard = Field<DashHost>("_dash");
        var custom = DashLayout.Default();
        custom.Items[0].X = 0.04;
        string coordinates = custom.ToJson();
        dashboard.Load(custom);
        typeof(LaunchPage).GetMethod("RefreshWorkspace", hidden)!.Invoke(launch, null);
        await Layout();
        Check(Descendants(launch).OfType<DashHost.CardView>().Any() && custom.ToJson() == coordinates,
            "A customized arrangement keeps its widgets and saved coordinates");
        apply.Invoke(launch, new object[] { state.RootElement });
        await Layout();
        Check(Descendants(launch).OfType<DashHost.CardView>().Count() == 0 && dashboard.Layout.Items.Count == 6 && Field<ComboBox>("_ver").SelectedIndex == 0,
            "Restoring the desktop layout preserves all editable modules and the selected version");
        stage.Children.Remove(launch);
    }

    private static async Task DashboardChecks(Grid stage)
    {
        var dashboard = new DashHost
        {
            ContentFactory = card => new Border { Child = Ui.Txt(card.Type) },
            ViewportHeight = 500
        };
        var layout = DashLayout.Default();
        string before = string.Join(";", layout.Items.Select(card => $"{card.X},{card.Y},{card.W},{card.H}"));
        dashboard.Load(layout);
        dashboard.Measure(new Size(640, double.PositiveInfinity));
        dashboard.Arrange(new Rect(new Point(), dashboard.DesiredSize));
        var cards = dashboard.Children.OfType<DashHost.CardView>().OrderBy(card => card.TranslatePoint(new Point(), dashboard).Y).ToList();
        Check(dashboard.DesiredSize.Height > 500 && cards.All(card => card.ActualWidth <= 640), "Narrow dashboard scrolls vertically without horizontal clipping");
        for (int i = 1; i < cards.Count; i++)
            Check(cards[i].TranslatePoint(new Point(), dashboard).Y >= cards[i - 1].TranslatePoint(new Point(), dashboard).Y + cards[i - 1].ActualHeight,
                "Compact dashboard cards do not overlap " + i);
        dashboard.EditMode = true;
        dashboard.Measure(new Size(640, double.PositiveInfinity));
        dashboard.Arrange(new Rect(0, 0, 640, 500));
        Check(before == string.Join(";", layout.Items.Select(card => $"{card.X},{card.Y},{card.W},{card.H}")), "Responsive dashboard preserves saved coordinates");
        await Layout();
    }

    private static async Task FileChecks(Window owner)
    {
        string directory = Path.Combine(Environment.GetEnvironmentVariable("TIPCL_HOME")!, "picker-fixtures");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "first.jar"), "original");
        File.WriteAllText(Path.Combine(directory, "second.JAR"), "second");
        File.WriteAllText(Path.Combine(directory, "hidden.txt"), "filtered");

        async Task<FilePickerWindow> Exercise(FilePickerMode mode, Func<FilePickerWindow, Task> scenario, string filename = "")
        {
            Exception? failure = null;
            var picker = new FilePickerWindow(mode, "File picker checks", "Mods|*.jar|All files|*.*", filename, directory) { Owner = owner };
            picker.Loaded += async (_, _) =>
            {
                try
                {
                    await Ready(picker);
                    await scenario(picker);
                }
                catch (Exception ex) { failure = ex; picker.Close(); }
            };
            picker.ShowDialog();
            if (failure != null) throw failure;
            await Layout();
            return picker;
        }
        var open = await Exercise(FilePickerMode.Open, async picker =>
        {
            ListBox files = Named<ListBox>(picker, "文件列表");
            Check(files.Items.Count == 3, "File picker lists folders and only matching files");
            files.SelectedIndex = 1;
            Program.SaveSnapshot(picker, Path.Combine(Environment.CurrentDirectory, "TestResults/ui-refine-20261105/file-picker.png"));
            Click(Named<Button>(picker, "打开"));
            await Layout();
        });
        Check(open.SelectedPaths?.Single() == Path.Combine(directory, "first.jar"), "Open returns the selected existing file");

        var multi = await Exercise(FilePickerMode.OpenMany, async picker =>
        {
            ListBox files = Named<ListBox>(picker, "文件列表");
            files.SelectedItems.Add(files.Items[1]);
            files.SelectedItems.Add(files.Items[2]);
            Click(Named<Button>(picker, "打开"));
            await Layout();
        });
        Check(multi.SelectedPaths?.Length == 2, "Multiple-file picker returns both selected files");

        var saved = await Exercise(FilePickerMode.Save, async picker =>
        {
            Click(Named<Button>(picker, "保存"));
            await Layout();
        }, "new-backup");
        Check(saved.SelectedPaths?.Single() == Path.Combine(directory, "new-backup.jar"), "Save picker supplies the missing extension");
        Check(!File.Exists(saved.SelectedPaths![0]), "Choosing a save destination does not write a file");

        var overwrite = await Exercise(FilePickerMode.Save, async picker =>
        {
            Click(Named<Button>(picker, "保存"));
            await Layout();
            Button replace = Named<Button>(picker, "替换文件");
            Check(!replace.IsKeyboardFocused, "Overwrite confirmation does not focus the destructive action");
            Key(replace, System.Windows.Input.Key.Escape);
            await Layout();
            Check(picker.IsVisible && picker.SelectedPaths == null, "Cancelling overwrite leaves the save picker open");
            Check(Named<TextBox>(picker, "文件名").Text == "first.jar", "Cancelling overwrite preserves the filename");
            Click(Named<Button>(picker, "保存"));
            await Layout();
            Click(Named<Button>(picker, "替换文件"));
        }, "first.jar");
        Check(overwrite.SelectedPaths?.Single() == Path.Combine(directory, "first.jar") && File.ReadAllText(Path.Combine(directory, "first.jar")) == "original", "Overwrite confirmation returns the path without modifying file contents");

        var folder = await Exercise(FilePickerMode.Folder, async picker =>
        {
            Check(Named<ListBox>(picker, "文件列表").Items.Count == 1, "Folder mode excludes regular files");
            Click(Named<Button>(picker, "选择此文件夹"));
            await Layout();
        });
        Check(folder.SelectedPaths?.Single() == directory, "Folder picker can select its current directory");

        var cancelled = await Exercise(FilePickerMode.Open, async picker =>
        {
            TextBox path = Named<TextBox>(picker, "文件夹路径");
            path.Text = Path.Combine(directory, "missing");
            BubbleKey(path, System.Windows.Input.Key.Enter);
            await Ready(picker);
            Check(!Named<Button>(picker, "打开").IsEnabled && Named<ListBox>(picker, "文件列表").Items.Count == 0, "Inaccessible directory cannot select stale entries");
            path.Text = directory;
            BubbleKey(path, System.Windows.Input.Key.Enter);
            path.Text = Path.Combine(directory, "nested");
            BubbleKey(path, System.Windows.Input.Key.Enter);
            await Ready(picker);
            Check(path.Text == Path.Combine(directory, "nested") && Named<ListBox>(picker, "文件列表").Items.Count == 0, "Newer navigation wins over an older directory read");
            Key(path, System.Windows.Input.Key.Escape);
        });
        Check(cancelled.SelectedPaths == null, "Escape cancels file selection");

        await Exercise(FilePickerMode.Folder, async picker =>
        {
            Click(Named<Button>(picker, "新建文件夹"));
            await Layout();
            TextBox name = Named<TextBox>(picker, "名称");
            name.Text = "../escape";
            Click(Named<Button>(picker, "创建"));
            await Layout();
            Check(name.IsVisible && name.IsKeyboardFocused && !Directory.Exists(Path.Combine(directory, "..", "escape")),
                "Invalid folder name stays open without creating a directory");
            Key(name, System.Windows.Input.Key.Escape);
            await Layout();
            Check(picker.IsVisible && !name.IsVisible, "Escape cancels only the new-folder form");
            Click(Named<Button>(picker, "新建文件夹"));
            await Layout();
            name = Named<TextBox>(picker, "名称");
            name.Text = "created-in-picker";
            Button create = Named<Button>(picker, "创建");
            Click(create);
            Key(name, System.Windows.Input.Key.Escape);
            Check(create.IsVisible && !create.IsEnabled, "Pending folder creation cannot dismiss its form");
            await Until(() => Named<TextBox>(picker, "文件夹路径").Text == Path.Combine(directory, "created-in-picker"));
            await Ready(picker);
            Check(Directory.Exists(Path.Combine(directory, "created-in-picker")), "New folder is created and opened");
            Key(picker, System.Windows.Input.Key.Escape);
        });

        string large = Path.Combine(directory, "large");
        Directory.CreateDirectory(large);
        for (int index = 0; index < 600; index++) File.WriteAllText(Path.Combine(large, $"mod-{index:000}.jar"), "fixture");
        await Exercise(FilePickerMode.Open, async picker =>
        {
            TextBox path = Named<TextBox>(picker, "文件夹路径");
            path.Text = large;
            BubbleKey(path, System.Windows.Input.Key.Enter);
            await Ready(picker);
            ListBox files = Named<ListBox>(picker, "文件列表");
            Check(files.Items.Count == 600 && Descendants(files).OfType<ListBoxItem>().Count() < 100,
                "Large directories virtualize rows outside the viewport");
            Named<TextBox>(picker, "筛选当前文件夹").Text = "mod-599";
            await Layout();
            Check(files.Items.Count == 1, "Filtering a large directory keeps the matching file");
            files.SelectedIndex = 0;
            Click(Named<Button>(picker, "打开"));
        });
    }

    private static async Task PageChecks(Grid stage)
    {
        var tasks = new TasksPage();
        stage.Children.Insert(0, tasks);
        tasks.OnShown();
        await Layout();
        Check(!Named<Button>(tasks, "全部取消").IsEnabled, "An empty task list disables Cancel all");
        TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = "ui-complete", Title = "Completed fixture" });
        TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = "ui-running", Title = "Running fixture" });
        TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = "ui-complete", Success = true });
        tasks.OnShown();
        await Layout();
        Button clear = Named<Button>(tasks, "清除");
        Check(clear.IsEnabled, "A completed task exposes an enabled clear action");
        Check(Named<Button>(tasks, "全部取消").IsEnabled, "Running tasks enable Cancel all");
        Click(clear);
        await Layout();
        Check(TaskStore.Get("ui-complete") == null && TaskStore.Get("ui-running") != null,
            "Clearing a completed task preserves a running task");
        TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = "ui-running", Success = true });
        TaskStore.ClearFinished();
        await Layout();
        Check(!Named<Button>(tasks, "全部取消").IsEnabled, "Finishing all tasks disables Cancel all again");
        stage.Children.Remove(tasks);

        var ai = new AiPage { Width = 640 };
        stage.Children.Insert(0, ai);
        await Layout();
        var title = (TextBlock)typeof(AiPage).GetField("_titleText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ai)!;
        title.Text = new string('W', 160);
        Button more = Descendants(ai).OfType<Button>().Single(button => (string?)button.ToolTip == "更多");
        foreach (double width in new[] { 920.0, 640.0, 480.0 })
        {
            ai.Width = width;
            await Layout();
            Point corner = more.TranslatePoint(new Point(), ai);
            Check(corner.X >= 0 && corner.X + more.ActualWidth <= ai.ActualWidth + 1,
                $"Long conversation titles keep the menu visible at {width}px");
        }
        ai.Width = 640;
        await Layout();
        Button history = Descendants(ai).OfType<Button>().Single(button => (string?)button.ToolTip == "显示 / 隐藏对话列表");
        Click(history);
        await Layout();
        Check(Host.Children.Count == 1 && Descendants(Top()).OfType<TextBox>().Any(), "Narrow assistant opens conversation history in a dialog");
        Key(Top(), System.Windows.Input.Key.Escape);
        await Layout();
        Check(Host.Children.Count == 0, "Conversation history closes with Escape");
        Click(history);
        await Layout();
        ai.Width = 880;
        await Layout();
        Check(Host.Children.Count == 0 && Descendants(ai).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "新对话" && button.IsVisible),
            "Widening the assistant restores history to its sidebar");
        stage.Children.Remove(ai);
    }

    private static async Task MotionChecks(Grid stage)
    {
        foreach (string style in new[] { "Chk", "Switch", "Radio" })
        {
            ToggleButton toggle = style == "Radio" ? (ToggleButton)new RadioButton() : new CheckBox();
            toggle.Style = Ui.S(style);
            stage.Children.Insert(0, toggle);
            await Layout();
            toggle.IsChecked = true;
            await Layout();
            string partName = style == "Chk" ? "Tick" : style == "Switch" ? "TrOn" : "Dot";
            var indicator = (UIElement)toggle.Template.FindName(partName, toggle);
            Check(indicator.Opacity == 1 && !DependencyPropertyHelper.GetValueSource(indicator, UIElement.OpacityProperty).IsAnimated,
                style + " updates immediately when motion is disabled");
            if (style == "Switch") Check(((TranslateTransform)((UIElement)toggle.Template.FindName("Kn", toggle)).RenderTransform).X == 18,
                "Enabled switch places its thumb at the selected end");
            toggle.IsChecked = false;
            await Layout();
            Check(indicator.Opacity == 0, style + " clears its selected state immediately");
            if (style == "Switch") Check(((TranslateTransform)((UIElement)toggle.Template.FindName("Kn", toggle)).RenderTransform).X == 0,
                "Disabled switch restores its thumb position");
            stage.Children.Remove(toggle);
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 150; attempt++)
        {
            await Task.Delay(20);
            await Layout();
            if (condition()) return;
        }
        throw new TimeoutException("The expected UI state did not arrive.");
    }

    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Descendants(root).OfType<T>()
        .Single(element => AutomationProperties.GetName(element) == name || (AutomationProperties.GetLabeledBy(element) as TextBlock)?.Text == name);
    private static async Task Ready(FilePickerWindow picker)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(20);
            await Layout();
            if (Descendants(picker).OfType<ProgressBar>().All(progress => !progress.IsVisible)) return;
        }
        throw new TimeoutException("File picker did not finish reading its directory.");
    }
    private static void BubbleKey(UIElement element, Key key) => element.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

    private static Grid Top() => (Grid)Host.Children[Host.Children.Count - 1];
    private static Button FindButton(string name) => Descendants(Top()).OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Key(UIElement element, Key key) => element.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static async Task Layout() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("[WPF FAIL] " + label);
        _passed++;
        Console.WriteLine("[PASS] " + label);
    }
}
