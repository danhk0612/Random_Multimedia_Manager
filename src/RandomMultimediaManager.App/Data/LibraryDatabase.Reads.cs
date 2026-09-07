using Microsoft.Data.Sqlite;
using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

public sealed partial class LibraryDatabase
{
    private List<T> Read<T>(string sql, Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var command = Command(null, sql, parameters);
            using var reader = command.ExecuteReader();
            var result = new List<T>();
            while (reader.Read()) result.Add(map(reader));
            return result;
        }
    }
    private static CategorySource ReadSource(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), r.GetString(3),
        r.GetBoolean(4), r.GetBoolean(5));
    public IReadOnlyList<Category> GetCategories() => Read("SELECT * FROM Category ORDER BY Name,Id;",
        r => new Category(Guid.Parse(r.GetString(0)), r.GetString(1),
            Enum.Parse<MediaType>(r.GetString(2)), r.GetBoolean(3)));
    public IReadOnlyList<CategorySource> GetSources(Guid categoryId) => Read(
        "SELECT * FROM CategorySource WHERE CategoryId=$id ORDER BY Id;", ReadSource, ("$id", Id(categoryId)));
    public IReadOnlyList<MediaItem> GetItems(Guid categoryId) => Read(
        "SELECT * FROM MediaItem WHERE CategoryId=$id ORDER BY Id;", r => new MediaItem(
            Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Enum.Parse<MediaType>(r.GetString(2)),
            r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetBoolean(7),
            r.GetBoolean(8), r.GetBoolean(9)), ("$id", Id(categoryId)));
    public IReadOnlyList<ViewHistory> GetHistory(Guid itemId) => Read(
        "SELECT * FROM ViewHistory WHERE MediaItemId=$id ORDER BY ViewedAtUtc,VisitId;",
        r => new ViewHistory(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)),
            r.GetInt64(2), Enum.Parse<VisitOrigin>(r.GetString(3))), ("$id", Id(itemId)));
    public StoredProgress? GetProgress(Guid itemId) => Read(
        "SELECT * FROM PlaybackProgress WHERE MediaItemId=$id;", r => new StoredProgress(
            new PlaybackProgress(Enum.Parse<MediaType>(r.GetString(1)),
                r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetDouble(3),
                r.IsDBNull(4) ? null : r.GetInt64(4)), r.GetInt64(5)), ("$id", Id(itemId))).SingleOrDefault();
    public AppSettings GetSettings() => Read("SELECT HistoryExclusionDays,ResumeMode FROM AppSettings WHERE Id=1;",
        r => new AppSettings(r.GetInt32(0), Enum.Parse<ResumeMode>(r.GetString(1)))).Single();
}
