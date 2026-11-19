using System;
using System.Linq;
using System.Threading;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class DeviceCodeExpiryTests
{
    [Theory]
    [InlineData(true, 180L)]
    [InlineData(false, 0L)]
    public void DeviceCodeEventsPreserveRealExpiryAndLegacyCallers(bool includeExpiry, long expected)
    {
        using var fixture = new BackendFixture();
        using var events = fixture.Bus.Subscribe();
        try
        {
            string task = fixture.Api.StartTask("device code fixture", context =>
            {
                if (includeExpiry) context.LoginCode("controlled-code", "https://microsoft.com/link", 180L);
                else context.LoginCode("controlled-code", "https://microsoft.com/link");
                return "complete";
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            PyDict found = null;
            while (found == null)
            {
                PyDict entry = events.Take(timeout.Token);
                if (Py.S(entry["event"]) == "login_code") found = (PyDict)entry["data"];
            }
            Assert.Equal("controlled-code", found["code"]);
            Assert.Equal("https://microsoft.com/link", found["uri"]);
            Assert.Equal(expected, Py.Int(found["expires_in"]));
            Assert.True(Py.Truthy(fixture.Api.wait_task(task, 3L)["ok"]));
        }
        finally { fixture.Bus.Unsubscribe(events); }
    }
}
