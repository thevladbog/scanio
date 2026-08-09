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
            TransportPresentationLabels.Endpoint(snapshot.Identity, snapshot.Endpoint, localizer),
            TransportPresentationLabels.DisplayName(snapshot.Identity, localizer),
            snapshot.Identity.HardwareId,
            ConnectionLabels.State(snapshot.State, localizer),
            snapshot.Identity.Kind == TransportKind.KeyboardCapture
                ? localizer["Keyboard.ReconstructedInput"]
                : snapshot.Options is { } options
                ? string.Join(
                    " · ",
                    options.BaudRate,
                    options.DataBits,
                    ConnectionLabels.Parity(options.Parity, localizer),
                    ConnectionLabels.StopBits(options.StopBits, localizer))
                : string.Empty);
    }
}

internal static class TransportPresentationLabels
{
    public static string DisplayName(TransportIdentity identity, IUiLocalizer? localizer) =>
        identity.Kind == TransportKind.KeyboardCapture && localizer is not null
            ? localizer["Transport.Keyboard.DisplayName"]
            : identity.DisplayName;

    public static string Endpoint(
        TransportIdentity identity,
        string fallback,
        IUiLocalizer? localizer) =>
        identity.Kind == TransportKind.KeyboardCapture && localizer is not null
            ? localizer["Transport.Keyboard.Endpoint"]
            : fallback;
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
