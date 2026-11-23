using System.Text;
using ViewerForTelegram.Data;

namespace ViewerForTelegram.Tests;

public class AiffToleranceTests
{
    private static byte[] BigEndian(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static byte[] Chunk(string id, byte[] body)
    {
        var l = new List<byte>();
        l.AddRange(Encoding.ASCII.GetBytes(id));
        l.AddRange(BigEndian(body.Length));
        l.AddRange(body);
        if (body.Length % 2 == 1)
        {
            l.Add(0);
        }
        return l.ToArray();
    }

    /// <summary>16-bit stereo, 44.1 kHz, <paramref name="frames"/> frames of silence.</summary>
    private static byte[] Aiff(int frames, byte[]? trailing = null, byte[]? extraChunkBeforeAudio = null)
    {
        var comm = new List<byte> { 0, 2 };
        comm.AddRange(BigEndian(frames));
        comm.AddRange(new byte[] { 0, 16 });
        comm.AddRange(new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0, 0, 0, 0, 0, 0 });   // 44100 as an 80-bit float

        var ssnd = new List<byte>();
        ssnd.AddRange(BigEndian(0));
        ssnd.AddRange(BigEndian(0));
        ssnd.AddRange(new byte[frames * 4]);

        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("AIFF"));
        body.AddRange(Chunk("COMM", comm.ToArray()));
        if (extraChunkBeforeAudio is not null)
        {
            body.AddRange(Chunk("ANNO", extraChunkBeforeAudio));
        }
        body.AddRange(Chunk("SSND", ssnd.ToArray()));

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("FORM"));
        file.AddRange(BigEndian(body.Count));
        file.AddRange(body);
        if (trailing is not null)
        {
            file.AddRange(trailing);   // after the declared end of the FORM
        }
        return file.ToArray();
    }

    private static string Write(TempPath file, byte[] bytes)
    {
        File.WriteAllBytes(file.Path, bytes);
        return file.Path;
    }

    [Fact]
    public void Open_ValidFile_UsesTheNormalReader()
    {
        using var file = TempPath.File();

        using var reader = AiffTolerance.Open(Write(file, Aiff(44100)), out Stream? owned);

        Assert.Null(owned);
        Assert.Equal(1.0, reader.TotalTime.TotalSeconds, 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void Open_StrayBytesAfterTheLastChunk_StillOpens(int strayBytes)
    {
        using var file = TempPath.File();
        string path = Write(file, Aiff(44100, trailing: new byte[strayBytes]));

        using var reader = AiffTolerance.Open(path, out Stream? owned);
        using (owned)
        {
            Assert.NotNull(owned);
            Assert.Equal(1.0, reader.TotalTime.TotalSeconds, 2);
        }
    }

    [Fact]
    public void Open_StrayBytesAfterAnOddChunk_StillOpens()
    {
        using var file = TempPath.File();
        string path = Write(file, Aiff(22050, trailing: new byte[5], extraChunkBeforeAudio: new byte[5]));

        using var reader = AiffTolerance.Open(path, out Stream? owned);
        using (owned)
        {
            Assert.Equal(0.5, reader.TotalTime.TotalSeconds, 2);
        }
    }

    [Fact]
    public void Open_RepairedFile_ReadsAllItsAudio()
    {
        using var file = TempPath.File();
        string path = Write(file, Aiff(1000, trailing: new byte[3]));

        using var reader = AiffTolerance.Open(path, out Stream? owned);
        using (owned)
        {
            var buffer = new byte[8192];
            int total = 0, read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            Assert.Equal(1000 * 4, total);
        }
    }

    [Fact]
    public void Open_NotAnAiffFile_KeepsTheOriginalError()
    {
        using var file = TempPath.File();
        string path = Write(file, Encoding.ASCII.GetBytes("this is not an aiff file at all"));

        Assert.ThrowsAny<Exception>(() => AiffTolerance.Open(path, out _));
    }

    [Fact]
    public void LastCompleteChunkEnd_IgnoresStrayBytes()
    {
        byte[] clean = Aiff(100);
        using var stream = new MemoryStream(Aiff(100, trailing: new byte[3]));

        Assert.Equal(clean.Length, AiffTolerance.LastCompleteChunkEnd(stream));
    }

    [Fact]
    public void LastCompleteChunkEnd_NotAiff_IsNull()
    {
        using var stream = new MemoryStream(new byte[100]);

        Assert.Null(AiffTolerance.LastCompleteChunkEnd(stream));
    }
}
