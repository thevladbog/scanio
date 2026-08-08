using System.Diagnostics;
using Scanio.Domain.Capture;

namespace Scanio.Capture;

public sealed class ScanAssembler
{
    private readonly List<BufferedByte> _buffer = new();
    private readonly ScanFramingOptions _options;
    private readonly ScanFramingSnapshot _framing;
    private long _nextSequence = 1;

    public ScanAssembler(ScanFramingOptions? options = null)
    {
        _options = options ?? new ScanFramingOptions();
        _framing = _options.CreateSnapshot();
    }

    public IReadOnlyList<CompletedScan> Push(RawChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var completed = new List<CompletedScan>();
        foreach (var value in chunk.Bytes)
        {
            _buffer.Add(new BufferedByte(value, chunk));

            if (EndsWithTerminator())
            {
                completed.Add(Complete(
                    ScanCompletionReason.Terminator,
                    _buffer.Count - _options.Terminator.Length));
            }
            else if (UnfinishedPayloadLength() > _options.MaximumUnfinishedBytes)
            {
                completed.Add(Complete(ScanCompletionReason.BufferOverflow, _buffer.Count));
            }
        }

        return completed;
    }

    public CompletedScan? CompleteOnSilence(long monotonicTimestamp)
    {
        if (_buffer.Count == 0)
        {
            return null;
        }

        var lastReceivedTimestamp = _buffer[^1].Chunk.MonotonicTimestamp;
        if (monotonicTimestamp < lastReceivedTimestamp ||
            Stopwatch.GetElapsedTime(lastReceivedTimestamp, monotonicTimestamp) < _options.SilenceTimeout)
        {
            return null;
        }

        return Complete(ScanCompletionReason.SilenceTimeout, _buffer.Count);
    }

    public void DiscardPending() => _buffer.Clear();

    private bool EndsWithTerminator()
    {
        if (_options.Terminator.IsEmpty || _buffer.Count < _options.Terminator.Length)
        {
            return false;
        }

        var start = _buffer.Count - _options.Terminator.Length;
        for (var index = 0; index < _options.Terminator.Length; index++)
        {
            if (_buffer[start + index].Value != _options.Terminator[index])
            {
                return false;
            }
        }

        return true;
    }

    private int UnfinishedPayloadLength()
    {
        if (_options.Terminator.IsEmpty)
        {
            return _buffer.Count;
        }

        var maximumPrefixLength = Math.Min(_buffer.Count, _options.Terminator.Length - 1);
        for (var prefixLength = maximumPrefixLength; prefixLength > 0; prefixLength--)
        {
            var bufferStart = _buffer.Count - prefixLength;
            var matches = true;
            for (var index = 0; index < prefixLength; index++)
            {
                if (_buffer[bufferStart + index].Value != _options.Terminator[index])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return _buffer.Count - prefixLength;
            }
        }

        return _buffer.Count;
    }

    private CompletedScan Complete(ScanCompletionReason reason, int payloadLength)
    {
        var rawBytes = new byte[_buffer.Count];
        var payloadBytes = new byte[payloadLength];
        var chunks = new List<RawChunk>();

        for (var index = 0; index < _buffer.Count; index++)
        {
            var bufferedByte = _buffer[index];
            rawBytes[index] = bufferedByte.Value;
            if (index < payloadLength)
            {
                payloadBytes[index] = bufferedByte.Value;
            }

            if (chunks.Count == 0 || !ReferenceEquals(chunks[^1], bufferedByte.Chunk))
            {
                chunks.Add(bufferedByte.Chunk);
            }
        }

        var firstChunk = _buffer[0].Chunk;
        var lastChunk = _buffer[^1].Chunk;
        var scan = CompletedScan.Create(
            _nextSequence++,
            rawBytes,
            payloadBytes,
            chunks,
            firstChunk.ReceivedAt,
            lastChunk.ReceivedAt,
            firstChunk.MonotonicTimestamp,
            lastChunk.MonotonicTimestamp,
            reason,
            _framing,
            firstChunk.TransportIdentity);

        _buffer.Clear();
        return scan;
    }

    private readonly record struct BufferedByte(byte Value, RawChunk Chunk);
}
