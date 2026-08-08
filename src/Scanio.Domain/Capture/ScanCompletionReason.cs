namespace Scanio.Domain.Capture;

public enum ScanCompletionReason
{
    Terminator,
    SilenceTimeout,
    BufferOverflow
}
