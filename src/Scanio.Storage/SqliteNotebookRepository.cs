using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Scanio.Application.Notebook;
using Scanio.Domain.Analysis;
using Scanio.Domain.Capture;
using Scanio.Domain.Transport;

namespace Scanio.Storage;

public sealed class SqliteNotebookRepository : INotebookRepository
{
    private const int SchemaVersion = 1;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteNotebookRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = Schema;
        command.ExecuteNonQuery();

        using var version = connection.CreateCommand();
        version.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var actualVersion = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (actualVersion != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported notebook database schema version {actualVersion}; expected {SchemaVersion}.");
        }
    }

    public NotebookSession CreateSession(string name, DateTimeOffset startedAt)
    {
        var session = NotebookSession.Create(Guid.NewGuid(), name, startedAt);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sessions(id, name, started_at) VALUES ($id, $name, $started);";
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", session.Name);
        command.Parameters.AddWithValue("$started", FormatDate(startedAt));
        command.ExecuteNonQuery();
        return session;
    }

    public void Append(NotebookRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var scanId = InsertScan(connection, transaction, record);
        InsertChunks(connection, transaction, scanId, record.Scan.ContributingChunks);
        InsertAnalyses(connection, transaction, scanId, record.Analyses);
        transaction.Commit();
    }

    public void CompleteSession(Guid sessionId, DateTimeOffset endedAt)
    {
        ExecuteSessionUpdate(
            "UPDATE sessions SET ended_at = $value WHERE id = $id;",
            sessionId,
            FormatDate(endedAt));
    }

    public IReadOnlyList<NotebookSession> GetSessions()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.id, s.name, s.started_at, s.ended_at, COUNT(r.id)
            FROM sessions s
            LEFT JOIN scans r ON r.session_id = s.id
            GROUP BY s.id, s.name, s.started_at, s.ended_at
            ORDER BY s.started_at DESC, s.rowid DESC;
            """;
        using var reader = command.ExecuteReader();
        var sessions = new List<NotebookSession>();
        while (reader.Read())
        {
            var session = NotebookSession.Create(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                ParseDate(reader.GetString(2)));
            sessions.Add(session.WithSummary(
                reader.GetString(1),
                reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)),
                reader.GetInt32(4)));
        }

        return sessions;
    }

    public IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId)
    {
        using var connection = OpenConnection();
        var rows = ReadScanRows(connection, sessionId);
        return rows.Select(row => MaterializeRecord(connection, row)).ToArray();
    }

    public void RenameSession(Guid sessionId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ExecuteSessionUpdate(
            "UPDATE sessions SET name = $value WHERE id = $id;",
            sessionId,
            name.Trim());
    }

    public void DeleteSession(Guid sessionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void ExecuteSessionUpdate(string sql, Guid sessionId, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static long InsertScan(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NotebookRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO scans(
                session_id, sequence, recorded_at, raw_bytes, payload_bytes,
                started_at, ended_at, start_mono, end_mono, completion_reason,
                framing_terminator, framing_silence_ticks, framing_max_bytes,
                transport_kind, transport_stable_id, transport_display_name, transport_hardware_id,
                text_encoding, decoded_text, escaped_display, decoding_warning, duplicate_count)
            VALUES(
                $session, $sequence, $recorded, $raw, $payload,
                $started, $ended, $start_mono, $end_mono, $reason,
                $terminator, $silence, $maximum,
                $kind, $stable, $display, $hardware,
                $encoding, $text, $escaped, $decode_warning, $duplicates);
            SELECT last_insert_rowid();
            """;
        Add(command, "$session", record.SessionId.ToString("D"));
        Add(command, "$sequence", record.Sequence);
        Add(command, "$recorded", FormatDate(record.RecordedAt));
        Add(command, "$raw", record.Scan.RawBytes.ToArray());
        Add(command, "$payload", record.Scan.PayloadBytes.ToArray());
        Add(command, "$started", FormatDate(record.Scan.StartedAt));
        Add(command, "$ended", FormatDate(record.Scan.EndedAt));
        Add(command, "$start_mono", record.Scan.StartMonotonicTimestamp);
        Add(command, "$end_mono", record.Scan.EndMonotonicTimestamp);
        Add(command, "$reason", (int)record.Scan.CompletionReason);
        Add(command, "$terminator", record.Scan.Framing.Terminator.ToArray());
        Add(command, "$silence", record.Scan.Framing.SilenceTimeout.Ticks);
        Add(command, "$maximum", record.Scan.Framing.MaximumUnfinishedBytes);
        Add(command, "$kind", (int)record.Scan.Transport.Kind);
        Add(command, "$stable", record.Scan.Transport.StableId);
        Add(command, "$display", record.Scan.Transport.DisplayName);
        Add(command, "$hardware", record.Scan.Transport.HardwareId);
        Add(command, "$encoding", (int)record.Decoded.Encoding);
        Add(command, "$text", record.Decoded.Text);
        Add(command, "$escaped", record.Decoded.EscapedDisplay);
        Add(command, "$decode_warning", record.Decoded.DecodingWarning);
        Add(command, "$duplicates", record.DuplicateCount);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void InsertChunks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long scanId,
        ImmutableArray<RawChunk> chunks)
    {
        for (var ordinal = 0; ordinal < chunks.Length; ordinal++)
        {
            var chunk = chunks[ordinal];
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO chunks(
                    scan_id, ordinal, sequence, bytes, received_at, monotonic_timestamp,
                    transport_kind, transport_stable_id, transport_display_name, transport_hardware_id)
                VALUES($scan, $ordinal, $sequence, $bytes, $received, $mono, $kind, $stable, $display, $hardware);
                """;
            Add(command, "$scan", scanId);
            Add(command, "$ordinal", ordinal);
            Add(command, "$sequence", chunk.SequenceNumber);
            Add(command, "$bytes", chunk.Bytes.ToArray());
            Add(command, "$received", FormatDate(chunk.ReceivedAt));
            Add(command, "$mono", chunk.MonotonicTimestamp);
            Add(command, "$kind", (int)chunk.TransportIdentity.Kind);
            Add(command, "$stable", chunk.TransportIdentity.StableId);
            Add(command, "$display", chunk.TransportIdentity.DisplayName);
            Add(command, "$hardware", chunk.TransportIdentity.HardwareId);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertAnalyses(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long scanId,
        ImmutableArray<AnalysisResult> analyses)
    {
        for (var ordinal = 0; ordinal < analyses.Length; ordinal++)
        {
            var analysis = analyses[ordinal];
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO analyses(scan_id, ordinal, analyzer_name, format, is_match, confidence, evidence, summary)
                VALUES($scan, $ordinal, $analyzer, $format, $match, $confidence, $evidence, $summary);
                SELECT last_insert_rowid();
                """;
            Add(command, "$scan", scanId);
            Add(command, "$ordinal", ordinal);
            Add(command, "$analyzer", analysis.AnalyzerName);
            Add(command, "$format", analysis.Format);
            Add(command, "$match", analysis.IsMatch ? 1 : 0);
            Add(command, "$confidence", (int)analysis.Confidence);
            Add(command, "$evidence", analysis.Evidence);
            Add(command, "$summary", analysis.Summary);
            var analysisId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            InsertAnalysisValues(connection, transaction, analysisId, analysis);
        }
    }

    private static void InsertAnalysisValues(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long analysisId,
        AnalysisResult analysis)
    {
        for (var ordinal = 0; ordinal < analysis.Fields.Length; ordinal++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO analysis_fields(analysis_id, ordinal, code, name, value) VALUES($id, $ordinal, $code, $name, $value);";
            Add(command, "$id", analysisId);
            Add(command, "$ordinal", ordinal);
            Add(command, "$code", analysis.Fields[ordinal].Code);
            Add(command, "$name", analysis.Fields[ordinal].Name);
            Add(command, "$value", analysis.Fields[ordinal].Value);
            command.ExecuteNonQuery();
        }

        InsertMessages(connection, transaction, analysisId, "analysis_errors", analysis.ValidationErrors);
        InsertMessages(connection, transaction, analysisId, "analysis_warnings", analysis.ValidationWarnings);
    }

    private static void InsertMessages(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long analysisId,
        string table,
        ImmutableArray<string> messages)
    {
        for (var ordinal = 0; ordinal < messages.Length; ordinal++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table}(analysis_id, ordinal, message) VALUES($id, $ordinal, $message);";
            Add(command, "$id", analysisId);
            Add(command, "$ordinal", ordinal);
            Add(command, "$message", messages[ordinal]);
            command.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<ScanRow> ReadScanRows(SqliteConnection connection, Guid sessionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, session_id, sequence, recorded_at, raw_bytes, payload_bytes,
                   started_at, ended_at, start_mono, end_mono, completion_reason,
                   framing_terminator, framing_silence_ticks, framing_max_bytes,
                   transport_kind, transport_stable_id, transport_display_name, transport_hardware_id,
                   text_encoding, decoded_text, escaped_display, decoding_warning, duplicate_count
            FROM scans WHERE session_id = $session ORDER BY id;
            """;
        Add(command, "$session", sessionId.ToString("D"));
        using var reader = command.ExecuteReader();
        var rows = new List<ScanRow>();
        while (reader.Read())
        {
            rows.Add(new ScanRow(
                reader.GetInt64(0),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt64(2),
                ParseDate(reader.GetString(3)),
                reader.GetFieldValue<byte[]>(4),
                reader.GetFieldValue<byte[]>(5),
                ParseDate(reader.GetString(6)),
                ParseDate(reader.GetString(7)),
                reader.GetInt64(8),
                reader.GetInt64(9),
                (ScanCompletionReason)reader.GetInt32(10),
                reader.GetFieldValue<byte[]>(11),
                reader.GetInt64(12),
                reader.GetInt32(13),
                (TransportKind)reader.GetInt32(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                (PayloadTextEncoding)reader.GetInt32(18),
                reader.GetString(19),
                reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.GetInt32(22)));
        }

        return rows;
    }

    private static NotebookRecord MaterializeRecord(SqliteConnection connection, ScanRow row)
    {
        var transport = new TransportIdentity(
            row.TransportKind,
            row.TransportStableId,
            row.TransportDisplayName,
            row.TransportHardwareId);
        var scan = CompletedScan.Create(
            row.Sequence,
            row.RawBytes,
            row.PayloadBytes,
            ReadChunks(connection, row.Id),
            row.StartedAt,
            row.EndedAt,
            row.StartMono,
            row.EndMono,
            row.CompletionReason,
            ScanFramingSnapshot.Create(
                row.Terminator,
                TimeSpan.FromTicks(row.SilenceTicks),
                row.MaximumBytes),
            transport);
        var decoded = DecodedPayload.Create(
            row.PayloadBytes,
            row.Encoding,
            row.Text,
            row.EscapedDisplay,
            row.DecodingWarning);
        return NotebookRecord.Create(
            row.Sequence,
            row.SessionId,
            scan,
            decoded,
            ReadAnalyses(connection, row.Id),
            row.DuplicateCount,
            row.RecordedAt);
    }

    private static IReadOnlyList<RawChunk> ReadChunks(SqliteConnection connection, long scanId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT sequence, bytes, received_at, monotonic_timestamp,
                   transport_kind, transport_stable_id, transport_display_name, transport_hardware_id
            FROM chunks WHERE scan_id = $scan ORDER BY ordinal;
            """;
        Add(command, "$scan", scanId);
        using var reader = command.ExecuteReader();
        var chunks = new List<RawChunk>();
        while (reader.Read())
        {
            var transport = new TransportIdentity(
                (TransportKind)reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7));
            chunks.Add(RawChunk.Create(
                reader.GetInt64(0),
                reader.GetFieldValue<byte[]>(1),
                ParseDate(reader.GetString(2)),
                reader.GetInt64(3),
                transport));
        }

        return chunks;
    }

    private static IReadOnlyList<AnalysisResult> ReadAnalyses(SqliteConnection connection, long scanId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, analyzer_name, format, is_match, confidence, evidence, summary
            FROM analyses WHERE scan_id = $scan ORDER BY ordinal;
            """;
        Add(command, "$scan", scanId);
        using var reader = command.ExecuteReader();
        var rows = new List<AnalysisRow>();
        while (reader.Read())
        {
            rows.Add(new AnalysisRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                (AnalysisConfidence)reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return rows.Select(row =>
                AnalysisResult.Create(
                    row.AnalyzerName,
                    row.Format,
                    row.IsMatch,
                    row.Confidence,
                    row.Evidence,
                    row.Summary,
                    ReadFields(connection, row.Id),
                    ReadMessages(connection, row.Id, "analysis_errors"),
                    ReadMessages(connection, row.Id, "analysis_warnings")))
            .ToArray();
    }

    private static IReadOnlyList<AnalysisField> ReadFields(SqliteConnection connection, long analysisId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT code, name, value FROM analysis_fields WHERE analysis_id = $id ORDER BY ordinal;";
        Add(command, "$id", analysisId);
        using var reader = command.ExecuteReader();
        var fields = new List<AnalysisField>();
        while (reader.Read())
        {
            fields.Add(new AnalysisField(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return fields;
    }

    private static IReadOnlyList<string> ReadMessages(
        SqliteConnection connection,
        long analysisId,
        string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT message FROM {table} WHERE analysis_id = $id ORDER BY ordinal;";
        Add(command, "$id", analysisId);
        using var reader = command.ExecuteReader();
        var messages = new List<string>();
        while (reader.Read())
        {
            messages.Add(reader.GetString(0));
        }

        return messages;
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private const string Schema =
        """
        PRAGMA journal_mode = WAL;
        PRAGMA foreign_keys = ON;
        CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL);
        INSERT INTO schema_info(version) SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM schema_info);
        CREATE TABLE IF NOT EXISTS sessions(
            id TEXT PRIMARY KEY, name TEXT NOT NULL, started_at TEXT NOT NULL, ended_at TEXT NULL);
        CREATE TABLE IF NOT EXISTS scans(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
            sequence INTEGER NOT NULL, recorded_at TEXT NOT NULL,
            raw_bytes BLOB NOT NULL, payload_bytes BLOB NOT NULL,
            started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
            start_mono INTEGER NOT NULL, end_mono INTEGER NOT NULL, completion_reason INTEGER NOT NULL,
            framing_terminator BLOB NOT NULL, framing_silence_ticks INTEGER NOT NULL, framing_max_bytes INTEGER NOT NULL,
            transport_kind INTEGER NOT NULL, transport_stable_id TEXT NOT NULL,
            transport_display_name TEXT NOT NULL, transport_hardware_id TEXT NULL,
            text_encoding INTEGER NOT NULL, decoded_text TEXT NOT NULL, escaped_display TEXT NOT NULL,
            decoding_warning TEXT NULL, duplicate_count INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_scans_session_id ON scans(session_id, id);
        CREATE TABLE IF NOT EXISTS chunks(
            scan_id INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL, sequence INTEGER NOT NULL, bytes BLOB NOT NULL,
            received_at TEXT NOT NULL, monotonic_timestamp INTEGER NOT NULL,
            transport_kind INTEGER NOT NULL, transport_stable_id TEXT NOT NULL,
            transport_display_name TEXT NOT NULL, transport_hardware_id TEXT NULL,
            PRIMARY KEY(scan_id, ordinal));
        CREATE TABLE IF NOT EXISTS analyses(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_id INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL, analyzer_name TEXT NOT NULL, format TEXT NOT NULL,
            is_match INTEGER NOT NULL, confidence INTEGER NOT NULL, evidence TEXT NOT NULL, summary TEXT NOT NULL,
            UNIQUE(scan_id, ordinal));
        CREATE TABLE IF NOT EXISTS analysis_fields(
            analysis_id INTEGER NOT NULL REFERENCES analyses(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL, code TEXT NOT NULL, name TEXT NOT NULL, value TEXT NOT NULL,
            PRIMARY KEY(analysis_id, ordinal));
        CREATE TABLE IF NOT EXISTS analysis_errors(
            analysis_id INTEGER NOT NULL REFERENCES analyses(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL, message TEXT NOT NULL, PRIMARY KEY(analysis_id, ordinal));
        CREATE TABLE IF NOT EXISTS analysis_warnings(
            analysis_id INTEGER NOT NULL REFERENCES analyses(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL, message TEXT NOT NULL, PRIMARY KEY(analysis_id, ordinal));
        """;

    private sealed record ScanRow(
        long Id,
        Guid SessionId,
        long Sequence,
        DateTimeOffset RecordedAt,
        byte[] RawBytes,
        byte[] PayloadBytes,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        long StartMono,
        long EndMono,
        ScanCompletionReason CompletionReason,
        byte[] Terminator,
        long SilenceTicks,
        int MaximumBytes,
        TransportKind TransportKind,
        string TransportStableId,
        string TransportDisplayName,
        string? TransportHardwareId,
        PayloadTextEncoding Encoding,
        string Text,
        string EscapedDisplay,
        string? DecodingWarning,
        int DuplicateCount);

    private sealed record AnalysisRow(
        long Id,
        string AnalyzerName,
        string Format,
        bool IsMatch,
        AnalysisConfidence Confidence,
        string Evidence,
        string Summary);
}
