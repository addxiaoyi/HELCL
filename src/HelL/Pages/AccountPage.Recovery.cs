using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class AccountPage
{
    private List<AccountRow> _accountRows = new List<AccountRow>();
    private readonly TextBox _accountSearch = Ui.Input("搜索账号名称或 UUID");
    private readonly ComboBox _accountType = Ui.Combo(new[] { "全部类型", "微软", "离线", "皮肤站", "统一通行证" }, "全部类型", 145);
    private readonly TextBlock _loginExpiry = Ui.Small("");
    private readonly DispatcherTimer _loginClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _loginDeadline;
    private bool _clockBound;
    private Button? _copyLoginCode, _openLoginBrowser, _renewLoginCode;

    private UIElement BuildAccountFilters()
    {
        _accountSearch.Width = double.NaN;
        _accountSearch.TextChanged += (_, _) => RenderAccountRows();
        _accountType.SelectionChanged += (_, _) => RenderAccountRows();
        return new PageHeader(_accountSearch, _accountType);
    }

    private void RenderAccountRows()
    {
        if (_accountRows.Count == 0) return;
        string query = _accountSearch.Text?.Trim() ?? "";
        string type = _accountType.SelectedIndex > 0 ? _accountType.Str() : "";
        var filtered = _accountRows.Where(account => (query.Length == 0
            || account.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || (account.Uuid ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            && (type.Length == 0 || TypeLabel(account) == type)).ToList();
        _list.Children.Clear();
        foreach (AccountRow account in filtered) _list.Children.Add(Row(account));
        _count.Text = filtered.Count + " / " + _accountRows.Count;
        if (filtered.Count == 0) _list.Children.Add(Ui.Muted("没有匹配账号，请修改名称或类型筛选。").Wrap());
    }

    private void SetLoginExpiry(int expiresIn)
    {
        _loginDeadline = expiresIn > 0 && expiresIn <= 86400 ? DateTimeOffset.UtcNow.AddSeconds(expiresIn) : (DateTimeOffset?)null;
        if (!_clockBound) { _loginClock.Tick += (_, _) => UpdateLoginExpiry(); _clockBound = true; }
        _loginClock.Start();
        UpdateLoginExpiry();
    }

    private void UpdateLoginExpiry()
    {
        if (_loginLayer == null) { _loginClock.Stop(); return; }
        if (!_loginDeadline.HasValue)
        {
            _loginExpiry.Text = "服务未提供到期时间；若验证码失效，请重新获取。";
            if (_renewLoginCode != null) _renewLoginCode.Visibility = Visibility.Visible;
            return;
        }
        TimeSpan remaining = _loginDeadline.Value - DateTimeOffset.UtcNow;
        bool expired = remaining <= TimeSpan.Zero;
        _loginExpiry.Text = expired ? "验证码已过期，请重新获取。" : "验证码剩余有效期 " + remaining.ToString(@"mm\:ss");
        if (_copyLoginCode != null) _copyLoginCode.IsEnabled = !expired;
        if (_openLoginBrowser != null) _openLoginBrowser.IsEnabled = !expired && _loginUri.Length > 0;
        if (_renewLoginCode != null) _renewLoginCode.Visibility = expired ? Visibility.Visible : Visibility.Collapsed;
        if (expired) _loginClock.Stop();
    }

    private async Task RestartMicrosoftLogin()
    {
        string? previous = _loginTask;
        if (previous != null) await Api.CallAsync<object>("cancel_task", new { task_id = previous });
        _loginTask = null;
        _loginClock.Stop();
        _loginLayer?.Close();
        _loginLayer = null;
        await MicrosoftLoginAsync();
    }
}
