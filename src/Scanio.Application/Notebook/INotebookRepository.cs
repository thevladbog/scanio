namespace Scanio.Application.Notebook;

public interface INotebookRepository
{
    void Initialize();

    NotebookSession CreateSession(string name, DateTimeOffset startedAt);

    void Append(NotebookRecord record);

    void CompleteSession(Guid sessionId, DateTimeOffset endedAt);

    IReadOnlyList<NotebookSession> GetSessions();

    IReadOnlyList<NotebookRecord> GetRecords(Guid sessionId);

    void RenameSession(Guid sessionId, string name);

    void DeleteSession(Guid sessionId);
}
