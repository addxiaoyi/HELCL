using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class AccountRefreshTests : IDisposable
{
	private readonly TestHome home = new TestHome();
	private readonly Func<PyDict, PyDict> microsoft = AccountManager.MicrosoftRefreshHook;
	private readonly Func<PyDict, PyDict> authlib = AccountManager.AuthlibRefreshHook;
	private readonly Func<PyDict, PyDict> nide8 = AccountManager.Nide8RefreshHook;

	public void Dispose()
	{
		AccountManager.MicrosoftRefreshHook = microsoft;
		AccountManager.AuthlibRefreshHook = authlib;
		AccountManager.Nide8RefreshHook = nide8;
		home.Dispose();
	}

	private static PyDict Account(string type = "microsoft") => PyDict.Of("type", type, "name", "Player", "access_token", "old-test-token",
		"refresh_token", "test-refresh", "expires_at", 0L);

	private static PyDict Renew(PyDict account)
	{
		account["access_token"] = "new-test-token";
		account["expires_at"] = Py.Time() + 3600;
		return account;
	}

	[Theory]
	[InlineData("microsoft")]
	[InlineData("authlib")]
	[InlineData("nide8")]
	public void FailedRefresh_DoesNotMutateMemoryOrPersistedAccount(string type)
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account(type));
		byte[] before = File.ReadAllBytes(Auth.ACCOUNTS_FILE);
		PyDict Failure(PyDict copy)
		{
			copy["access_token"] = "partial-test-token";
			throw new AuthError("Temporary service failure");
		}
		AccountManager.MicrosoftRefreshHook = Failure;
		AccountManager.AuthlibRefreshHook = Failure;
		AccountManager.Nide8RefreshHook = Failure;
		Assert.Throws<AuthError>(() => manager.EnsureValid(account));
		Assert.Equal("old-test-token", account["access_token"]);
		Assert.Equal(before, File.ReadAllBytes(Auth.ACCOUNTS_FILE));
	}

	[Fact]
	public void Refresh_DoesNotChangeAnotherSelectedAccount()
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account());
		manager.AddAccount(manager.OfflineAccount("Selected"));
		AccountManager.MicrosoftRefreshHook = Renew;
		manager.EnsureValid(account);
		Assert.Equal("Selected", manager.active);
		Assert.Equal("Selected", new AccountManager().active);
		Assert.Equal("new-test-token", manager.GetAccount("Player")["access_token"]);
	}

	[Fact]
	public void Refresh_RenamesSelectedAccountWithoutDuplicates()
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account());
		AccountManager.MicrosoftRefreshHook = copy => { copy["name"] = "Renamed"; return Renew(copy); };
		manager.EnsureValid(account);
		Assert.Equal("Renamed", manager.active);
		Assert.Null(manager.GetAccount("Player"));
		Assert.Single(manager.accounts);
		Assert.NotNull(new AccountManager().GetAccount("Renamed"));
		Assert.Same(account, manager.EnsureValid(account));
		Assert.Equal("Renamed", account["name"]);
	}

	[Fact]
	public void Refresh_NameConflictPreservesBothAccounts()
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account());
		manager.AddAccount(manager.OfflineAccount("Existing"));
		byte[] before = File.ReadAllBytes(Auth.ACCOUNTS_FILE);
		AccountManager.MicrosoftRefreshHook = copy => { copy["name"] = "Existing"; return Renew(copy); };
		Assert.Throws<AuthError>(() => manager.EnsureValid(account));
		Assert.Equal(2, manager.accounts.Count);
		Assert.Equal("old-test-token", account["access_token"]);
		Assert.Equal(before, File.ReadAllBytes(Auth.ACCOUNTS_FILE));
	}

	[Fact]
	public void Refresh_DeletedAccountIsNotResurrected()
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account());
		AccountManager.MicrosoftRefreshHook = copy => { manager.RemoveAccount("Player"); return Renew(copy); };
		Assert.Throws<AuthError>(() => manager.EnsureValid(account));
		Assert.Empty(manager.accounts);
		Assert.Empty(new AccountManager().accounts);
	}

	[Fact]
	public void Refresh_PersistenceFailurePreservesInMemoryToken()
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account());
		AccountManager.MicrosoftRefreshHook = Renew;
		using var held = new FileStream(Auth.ACCOUNTS_FILE, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		Assert.ThrowsAny<Exception>(() => manager.EnsureValid(account));
		Assert.Equal("old-test-token", manager.GetAccount("Player")["access_token"]);
		Assert.Equal("Player", manager.active);
	}

	[Theory]
	[InlineData(30, 1)]
	[InlineData(3600, 0)]
	public void Refresh_UsesExpirySafetyWindow(int remaining, int expectedCalls)
	{
		var manager = new AccountManager();
		PyDict account = Account();
		account["expires_at"] = Py.Time() + remaining;
		manager.AddAccount(account);
		int calls = 0;
		AccountManager.MicrosoftRefreshHook = copy => { calls++; return Renew(copy); };
		manager.EnsureValid(account);
		Assert.Equal(expectedCalls, calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentRefreshes_ReuseOneSuccessfulRefresh(bool renamed)
	{
		var manager = new AccountManager();
		PyDict account = manager.AddAccount(Account());
		int calls = 0;
		AccountManager.MicrosoftRefreshHook = copy =>
		{
			Interlocked.Increment(ref calls);
			if (renamed)
				copy["name"] = "Renamed";
			return Renew(copy);
		};
		Task<PyDict> first = Task.Run(() => manager.EnsureValid(account));
		Task<PyDict> second = Task.Run(() => manager.EnsureValid(account));
		PyDict[] refreshed = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(1, calls);
		Assert.Same(refreshed[0], refreshed[1]);
		Assert.Equal(renamed ? "Renamed" : "Player", manager.active);
	}
}
