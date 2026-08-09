using Scanio.Domain.Transport;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Services;
using Scanio.Transports.Serial;

namespace Scanio.Presentation.ViewModels;

public sealed record ConnectionSnapshotViewModel(
    string Endpoint,
    string FriendlyName,
    string? HardwareId,
    string StateLabel,
    string ParametersLabel)
{
    public static ConnectionSnapshotViewModel? From(
        ConnectionPresentationSnapshot? snapshot,
        IUiLocalizer localizer)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new ConnectionSnapshotViewModel(
            snapshot.Endpoint,
            snapshot.Identity.DisplayName,
            snapshot.Identity.HardwareId,
            ConnectionLabels.State(snapshot.State, localizer),
            snapshot.Options is { } options
                ? string.Join(
                    " · ",
                    options.BaudRate,
                    options.DataBits,
                    ConnectionLabels.Parity(options.Parity, localizer),
                    ConnectionLabels.StopBits(options.StopBits, localizer))
                : localizer["Keyboard.ReconstructedInput"]);
    }
}

internal static class ConnectionLabels
{
    public static string State(ConnectionState state, IUiLocalizer localizer) =>
        localizer[$"Connection.State.{state}"];

    public static string Parity(SerialParity parity, IUiLocalizer localizer) =>
        localizer[$"Serial.Parity.{parity}"];

    public static string StopBits(SerialStopBits stopBits, IUiLocalizer localizer) =>
        localizer[$"Serial.StopBits.{stopBits}"];

    public static string Handshake(SerialHandshake handshake, IUiLocalizer localizer) =>
        localizer[$"Serial.Handshake.{handshake}"];
}
