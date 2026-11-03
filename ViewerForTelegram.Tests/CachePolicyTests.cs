using ViewerForTelegram.Logic;

namespace ViewerForTelegram.Tests;

public class CachePolicyTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void FitsInCache_FileUnderLimit_IsAllowed() =>
        Assert.True(CachePolicy.FitsInCache(2 * Gb));

    [Fact]
    public void FitsInCache_FileExactlyAtLimit_IsAllowed() =>
        Assert.True(CachePolicy.FitsInCache(CachePolicy.LimitBytes));

    [Fact]
    public void FitsInCache_FileOverLimit_IsRejected() =>
        Assert.False(CachePolicy.FitsInCache(CachePolicy.LimitBytes + 1));

    [Fact]
    public void FitsInCache_FourGigabytes_IsRejected() =>
        Assert.False(CachePolicy.FitsInCache(4 * Gb));

    [Fact]
    public void FitsInCache_UnknownSize_IsLetThrough() =>
        Assert.True(CachePolicy.FitsInCache(0));

    [Fact]
    public void FitsInCache_UsesTheGivenLimit() =>
        Assert.False(CachePolicy.FitsInCache(101, limitBytes: 100));
}
