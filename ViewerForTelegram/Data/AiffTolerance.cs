using NAudio.Wave;

namespace ViewerForTelegram.Data;

/// <summary>
/// Opens .aiff files the way players do: NAudio's <see cref="AiffFileReader"/> walks
/// the chunks to the very end of the file and fails ("Incorrect length for long")
/// on a few stray bytes after the last chunk - padding some tools leave behind.
/// The audio itself is fine, so when the plain open fails the file is read again
/// through a view that ends where the last complete chunk ends.
/// </summary>
public static class AiffTolerance
{
    /// <summary>
    /// Opens <paramref name="path"/>. When the reader had to be given a shortened view of the file,
    /// <paramref name="ownedStream"/> is the file stream behind it - the caller disposes it with the reader.
    /// </summary>
    public static AiffFileReader Open(string path, out Stream? ownedStream)
    {
        ownedStream = null;
        try
        {
            return new AiffFileReader(path);
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
        {
            FileStream file = File.OpenRead(path);
            try
            {
                long? end = LastCompleteChunkEnd(file);
                if (end is null || end >= file.Length)
                {
                    throw;   // not the stray-bytes case - the original error stands
                }

                var reader = new AiffFileReader(new LimitedStream(file, end.Value));
                ownedStream = file;
                return reader;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// The offset where the last complete chunk of an AIFF file ends (chunks are padded to an
    /// even length), or null when the stream is not an AIFF file at all.
    /// </summary>
    public static long? LastCompleteChunkEnd(Stream stream)
    {
        var header = new byte[12];
        stream.Position = 0;
        if (stream.Read(header, 0, 12) < 12
            || !IsId(header, 0, "FORM")
            || !(IsId(header, 8, "AIFF") || IsId(header, 8, "AIFC")))
        {
            return null;
        }

        long end = 12;
        var chunk = new byte[8];
        while (end + 8 <= stream.Length)
        {
            stream.Position = end;
            if (stream.Read(chunk, 0, 8) < 8)
            {
                break;
            }

            long length = (long)(uint)((chunk[4] << 24) | (chunk[5] << 16) | (chunk[6] << 8) | chunk[7]);
            long next = end + 8 + length + (length & 1);
            if (next > stream.Length)
            {
                break;   // a cut-off chunk: what is before it is all there is
            }

            end = next;
        }
        return end;
    }

    private static bool IsId(byte[] buffer, int offset, string id) =>
        buffer[offset] == id[0] && buffer[offset + 1] == id[1]
        && buffer[offset + 2] == id[2] && buffer[offset + 3] == id[3];

    /// <summary>A read-only view of the first <c>length</c> bytes of another stream.</summary>
    private sealed class LimitedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _length;

        public LimitedStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
            _inner.Position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = Math.Clamp(value, 0, _length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = _length - _inner.Position;
            return left <= 0 ? 0 : _inner.Read(buffer, offset, (int)Math.Min(count, left));
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => _length + offset,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
