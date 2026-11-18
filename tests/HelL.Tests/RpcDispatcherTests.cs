#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class RpcDispatcherTests
{
    [Fact]
    public void OnlyPublicLowercaseNonHiddenMethodsAreExposed()
    {
        var rpc = new RpcDispatcher(new Commands());
        Assert.Equal(new[] { "collect", "fail", "page", "tag" }, rpc.MethodNames.OrderBy(name => name));
        Assert.Equal(-32601L, Assert.Throws<RpcError>(() => rpc.Call("_private", new PyDict())).Code);
        Assert.Equal(-32601L, Assert.Throws<RpcError>(() => rpc.Call("missing", new PyDict())).Code);
        Assert.Throws<InvalidOperationException>(() => new RpcDispatcher(new Overloads()));
    }

    [Fact]
    public void KeywordAndPositionalBindingPreserveDefaultsAndNumericConversion()
    {
        var rpc = new RpcDispatcher(new Commands());
        var named = Assert.IsType<PyDict>(rpc.Call("page", PyDict.Of("limit", 3L, "instance", "world")));
        Assert.Equal(3L, named["limit"]);
        Assert.Equal("stable", named["tag"]);
        var positional = Assert.IsType<PyDict>(rpc.Call("page", new List<object> { "world" }));
        Assert.Equal(24L, positional["limit"]);
        Assert.Equal("stable", positional["tag"]);
        Assert.Equal("stable", rpc.Call("tag", new PyDict()));
        Assert.Equal("explicit", rpc.Call("tag", PyDict.Of("value", "explicit")));
    }

    [Fact]
    public void UnknownKeywordsWarnWithoutChangingLegacyCallBehavior()
    {
        var previous = RpcDispatcher.Warn;
        var warnings = new List<string>();
        try
        {
            RpcDispatcher.Warn = warnings.Add;
            var reply = Assert.IsType<PyDict>(new RpcDispatcher(new Commands()).Call("page", PyDict.Of("instance", "world", "obsolete", true)));
            Assert.Equal("world", reply["instance"]);
            Assert.Contains("obsolete", Assert.Single(warnings));
        }
        finally { RpcDispatcher.Warn = previous; }
    }

    [Fact]
    public void VariadicParametersKeepExtraPositionalsAndKeywords()
    {
        var rpc = new RpcDispatcher(new Commands());
        var positional = Assert.IsType<PyDict>(rpc.Call("collect", new List<object> { "world", 1L, "two" }));
        Assert.Equal(new object[] { 1L, "two" }, Assert.IsType<List<object>>(positional["args"]));
        Assert.Empty(Assert.IsType<PyDict>(positional["kwargs"]));
        var named = Assert.IsType<PyDict>(rpc.Call("collect", PyDict.Of("name", "world", "group", "friends")));
        Assert.Empty(Assert.IsType<List<object>>(named["args"]));
        Assert.Equal("friends", Assert.IsType<PyDict>(named["kwargs"])["group"]);
    }

    [Fact]
    public void InvalidArgumentsFailBeforeCallingTheTargetAndErrorsAreUnwrapped()
    {
        var target = new Commands();
        var rpc = new RpcDispatcher(target);
        Assert.Throws<TypeError>(() => rpc.Call("page", new PyDict()));
        Assert.Throws<TypeError>(() => rpc.Call("page", PyDict.Of("instance", "world", "limit", "bad")));
        Assert.Throws<TypeError>(() => rpc.Call("page", new List<object> { "world", 1L, "tag", "extra" }));
        Assert.Throws<TypeError>(() => rpc.Call("page", "invalid params"));
        Assert.Equal(0, target.PageCalls);
        Assert.Equal("instance missing", Assert.Throws<ArgumentException>(() => rpc.Call("fail", new PyDict())).Message);
    }

    private sealed class Commands
    {
        public int PageCalls { get; private set; }
        public object page(string instance, int limit = 24, [Def("stable")] object? tag = null)
        {
            PageCalls++;
            return PyDict.Of("instance", instance, "limit", limit, "tag", tag!);
        }
        public object tag([Def("stable")] string value = "csharp-default") => value;
        public object collect(string name, [VarArgs] List<object>? args = null, [VarKw] PyDict? kwargs = null)
            => PyDict.Of("name", name, "args", args!, "kwargs", kwargs!);
        public object fail() => throw new ArgumentException("instance missing");
        public object? Uppercase() => null;
        public object? _private() => null;
        [RpcHidden] public object? hidden() => null;
        public object? generic<T>() => null;
    }

    private sealed class Overloads
    {
        public object? duplicate() => null;
        public object duplicate(string name) => name;
    }
}
