using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class WallpaperCachePolicyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hell-policy-" + Guid.NewGuid().ToString("N"));
    public WallpaperCachePolicyTests() => Directory.CreateDirectory(_folder);
    private string Entry(char key, long bytes, int unusedDays)
    {
        string path = Path.Combine(_folder, new string(key, 64) + ".mp4"); using (var file = File.Create(path)) file.SetLength(bytes);
        File.WriteAllText(path + ".size", bytes.ToString()); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-unusedDays)); return path;
    }
    [Theory]
    [InlineData(255,0)] [InlineData(4097,0)] [InlineData(512,-1)] [InlineData(512,366)]
    public void InvalidPolicyIsRejected(int capacity, int days) => Assert.Throws<ArgumentOutOfRangeException>(() => new WallpaperCachePolicy(capacity, days));
    [Fact]
    public async Task RetentionRemovesExpiredEntriesAndKeepsRecentOnes()
    {
        string old = Entry('a',2048,8), recent = Entry('b',4096,2);
        var cleanup = await new WallpaperVideoCache(_folder, new WallpaperCachePolicy(512,7)).EnforcePolicy();
        Assert.Equal(2048, cleanup.FreedBytes); Assert.False(File.Exists(old)); Assert.True(File.Exists(recent)); Assert.Empty(cleanup.Errors);
    }
    [Fact]
    public async Task NoAgeLimitRetainsOldCacheBelowCapacity()
    {
        string old = Entry('a',2048,365);
        Assert.Equal(0,(await new WallpaperVideoCache(_folder,new WallpaperCachePolicy(512,0)).EnforcePolicy()).FreedBytes); Assert.True(File.Exists(old));
    }
    [Fact]
    public async Task ReducedCapacityEvictsOldestEntryFirst()
    {
        string oldest = Entry('a',160L*1048576,20), recent = Entry('b',160L*1048576,2);
        var cleanup = await new WallpaperVideoCache(_folder,new WallpaperCachePolicy(256,0)).EnforcePolicy();
        Assert.False(File.Exists(oldest)); Assert.True(File.Exists(recent)); Assert.Equal(160L*1048576,cleanup.FreedBytes);
    }
    [Fact]
    public async Task ExpiredPlayingEntryRemainsUntilLeaseIsReleased()
    {
        string old = Entry('a',2048,8); var cache = new WallpaperVideoCache(_folder,new WallpaperCachePolicy(512,7));
        using(var lease=cache.HoldPlayback(old)) { var cleanup=await cache.EnforcePolicy(); Assert.Equal(1,cleanup.SkippedFiles); Assert.True(File.Exists(old)); }
        Assert.Equal(2048,(await cache.EnforcePolicy()).FreedBytes);
    }
    [Fact]
    public async Task PlayingCachedVideoRefreshesRetentionClock()
    {
        string source=Path.Combine(_folder,"source.mp4"); File.WriteAllText(source,"original");
        var cache=new WallpaperVideoCache(_folder,new WallpaperCachePolicy(512,7)); string path=cache.CachePath(source);
        File.WriteAllBytes(path,new byte[2048]); File.WriteAllText(path+".size","2048"); File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-8));
        Assert.Equal(path,cache.Cached(source)); Assert.Equal(0,(await cache.EnforcePolicy()).FreedBytes); Assert.True(File.Exists(path));
    }
    [Fact]
    public async Task EncodingHonorsConfiguredCapacityAndEvictionOrder()
    {
        string old=Entry('a',128L*1048576,20), source=Path.Combine(_folder,"source.mp4"); File.WriteAllText(source,"original");
        var cache=new WallpaperVideoCache(_folder,(_,output,progress,token)=>{File.WriteAllBytes(output,new byte[2048]);return Task.FromResult(output);},new WallpaperCachePolicy(256,0));
        string encoded=await cache.Prepare(source,new Progress<double>(),CancellationToken.None);
        Assert.False(File.Exists(old));Assert.True(File.Exists(encoded));Assert.Equal("original",File.ReadAllText(source));
    }
    public void Dispose()=>Directory.Delete(_folder,true);
}
