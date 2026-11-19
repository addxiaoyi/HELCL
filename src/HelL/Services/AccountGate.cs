using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Models;

namespace HelL.Services;

internal static class AccountGate
{
	public static bool IsSentinel(string? name)
	{
		string text = name?.Trim() ?? "";
		if (text.Length != 0 && !(text == "离线模式"))
		{
			return text == I18n.L("离线模式");
		}
		return true;
	}

	public static async Task<List<string>> ListAsync()
	{
		return (from r in (await AppServices.Client.TryCallAsync("get_account_rows", null, new List<AccountRow>())) ?? new List<AccountRow>()
			where !IsSentinel(r.Name)
			orderby r.Active descending
			select r.Name).Distinct().ToList();
	}

	public static async Task<string?> EnsureAsync(string? preferred = null)
	{
		List<string> list = await ListAsync();
		if (!IsSentinel(preferred) && list.Contains(preferred.Trim()))
		{
			return preferred.Trim();
		}
		if (list.Count > 0)
		{
			return list[0];
		}
		return await CreateOfflineAsync();
	}

	public static async Task<string?> CreateOfflineAsync()
	{
		string typed = AppServices.Window?.Prefs.LastUser?.Trim() ?? "";
		if (IsSentinel(typed) || typed == "Player")
		{
			typed = "";
		}
		int num = 0;
		while (true)
		{
			TextBox name = Ui.Input(I18n.L("例如 Steve"));
			name.Text = typed;
			ComboBox skin = Ui.Combo(new string[3]
			{
				I18n.L("默认"),
				"Steve",
				"Alex"
			});
			skin.SelectedIndex = num;
			SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("启动游戏前需要先创建一个离线用户（无正版验证）。")), name, Ui.Field(I18n.L("默认皮肤"), skin, null, 78.0));
			if (!(await Dlg.Ask(I18n.L("创建离线用户"), body, I18n.L("创建并继续"))))
			{
				return null;
			}
			typed = name.Text?.Trim() ?? "";
			num = Math.Max(0, skin.SelectedIndex);
			if (ValidName(typed))
			{
				break;
			}
			AppServices.Window?.Toast(I18n.L("用户名不可用"), I18n.L("1–16 个字符，不能含空格"), ToastKind.Warning);
		}
		BridgeClient client = AppServices.Client;
		string text = await client.CallAsync<string>("add_offline_account", new
		{
			username = typed,
			skin = num switch
			{
				1 => "steve", 
				2 => "alex", 
				_ => "default", 
			}
		});
		text = (string.IsNullOrWhiteSpace(text) ? typed : text);
		AppServices.Window?.Toast(I18n.L("已创建离线用户"), text, ToastKind.Success);
		return text;
	}

	public static bool ValidName(string name)
	{
		if (name.Length >= 1 && name.Length <= 16 && !name.Any(char.IsWhiteSpace))
		{
			return !IsSentinel(name);
		}
		return false;
	}
}
