using System;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Services;

internal sealed class SecretInput : Grid
{
    private readonly PasswordBox password = Ui.Pw();
    private readonly TextBox visible = Ui.Input();
    private readonly Button reveal;
    private bool syncing;
    public event RoutedEventHandler? TextChanged;
    public string Text
    {
        get => password.Password;
        set
        {
            if (password.Password == (value ?? "")) return;
            syncing = true;
            password.Password = value ?? "";
            visible.Text = value ?? "";
            syncing = false;
            TextChanged?.Invoke(this, new RoutedEventArgs());
        }
    }
    public bool Revealed => visible.Visibility == Visibility.Visible;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TagProperty && password != null && visible != null)
        { password.ToolTip = e.NewValue; visible.Tag = e.NewValue; }
    }
    public SecretInput(string hint)
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Tag = hint;
        password.ToolTip = hint;
        visible.Tag = hint;
        visible.Visibility = Visibility.Collapsed;
        reveal = Ui.Btn("显示", BtnKind.Chip, (_, _) => Toggle());
        reveal.Margin = new Thickness(6, 0, 0, 0);
        Children.Add(password); Children.Add(visible); Children.Add(reveal);
        SetColumn(reveal, 1);
        System.Windows.Automation.AutomationProperties.SetName(password, hint);
        System.Windows.Automation.AutomationProperties.SetName(visible, hint);
        System.Windows.Automation.AutomationProperties.SetName(reveal, "显示或隐藏密钥");
        password.PasswordChanged += (_, _) => Changed(password.Password);
        visible.TextChanged += (_, _) => Changed(visible.Text);
        Unloaded += (_, _) => { if (Revealed) Toggle(); };
    }
    private void Changed(string value)
    {
        if (syncing) return;
        syncing = true; password.Password = value; visible.Text = value; syncing = false;
        TextChanged?.Invoke(this, new RoutedEventArgs());
    }
    private void Toggle()
    {
        bool show = !Revealed;
        password.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        visible.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        reveal.Content = show ? "隐藏" : "显示";
        if (show) visible.Focus(); else password.Focus();
    }
}
