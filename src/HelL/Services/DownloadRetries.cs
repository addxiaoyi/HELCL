using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HelL.Services;

public static class DownloadRetries
{
    public sealed record Request(string Method, string ArgumentsJson, DateTime Created);
    private static readonly HashSet<string> Allowed = new HashSet<string> { "install_game", "repair_version", "install_java", "download_java", "install_mod", "install_resourcepack", "install_shader", "install_datapack", "minecraft_install" };
    private static readonly ConcurrentDictionary<string, Request> Pending = new ConcurrentDictionary<string, Request>();

    public static void Register(string id, string method, object? arguments)
    {
        if (string.IsNullOrEmpty(id) || !Allowed.Contains(method)) return;
        Pending[id] = new Request(method, JsonSerializer.Serialize(arguments, BridgeClient.JsonOpt), DateTime.UtcNow);
        foreach (var old in Pending.OrderByDescending(entry => entry.Value.Created).Skip(60)) Pending.TryRemove(old.Key, out _);
    }
    public static Request? Find(string id) => Pending.TryGetValue(id, out var request) ? request : null;
    public static void Remove(string id) => Pending.TryRemove(id, out _);
}
