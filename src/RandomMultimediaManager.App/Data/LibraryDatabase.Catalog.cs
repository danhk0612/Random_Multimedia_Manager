using RandomMultimediaManager.Core;

namespace RandomMultimediaManager.App.Data;

public sealed partial class LibraryDatabase
{
    public void SaveCategory(Category category)
    {
        string name = category.Name.Trim();
        if (name.Length is < 1 or > 100 || !Enum.IsDefined(category.MediaType))
            throw new ArgumentException("분류 이름 또는 타입이 올바르지 않습니다.");
        Write(transaction => Execute(transaction, """
            INSERT INTO Category VALUES($id,$name,$type,$enabled)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,
            MediaType=excluded.MediaType, IsEnabled=excluded.IsEnabled;
            """, ("$id", Id(category.Id)), ("$name", name),
            ("$type", category.MediaType.ToString()), ("$enabled", category.IsEnabled)));
        // The composite FK rejects a type change even if registered items are Missing.
    }

    public CategorySource AddSource(CategorySource source)
    {
        ValidatePathPair(source.RootPath, source.RootPathKey);
        return Write(transaction =>
        {
            Execute(transaction, """
                INSERT INTO CategorySource VALUES($id,$category,$path,$key,$recursive,$enabled)
                ON CONFLICT(CategoryId,RootPathKey) DO NOTHING;
                """, ("$id", Id(source.Id)), ("$category", Id(source.CategoryId)),
                ("$path", source.RootPath), ("$key", source.RootPathKey),
                ("$recursive", source.IncludeSubdirectories), ("$enabled", source.IsEnabled));
            using var command = Command(transaction,
                "SELECT * FROM CategorySource WHERE CategoryId=$category AND RootPathKey=$key;",
                ("$category", Id(source.CategoryId)), ("$key", source.RootPathKey));
            using var reader = command.ExecuteReader();
            reader.Read();
            return ReadSource(reader);
        });
    }
    public void UpdateSource(CategorySource source)
    {
        ValidatePathPair(source.RootPath, source.RootPathKey);
        Write(transaction =>
        {
            RequireOne(Execute(transaction, """
                UPDATE CategorySource SET RootPath=$path, RootPathKey=$key,
                IncludeSubdirectories=$recursive, IsEnabled=$enabled WHERE Id=$id AND CategoryId=$category;
                """, ("$id", Id(source.Id)), ("$category", Id(source.CategoryId)),
                ("$path", source.RootPath), ("$key", source.RootPathKey),
                ("$recursive", source.IncludeSubdirectories), ("$enabled", source.IsEnabled)));
            return 0;
        });
    }
    public void RemoveSource(Guid sourceId) => Write(transaction =>
        Execute(transaction, "DELETE FROM CategorySource WHERE Id=$id;", ("$id", Id(sourceId))));

    // T05 supplies only successfully observed records and explicitly confirmed missing IDs.
    // This method performs no scan, path discovery, or missing inference.
    public void ApplyObservedItems(IReadOnlyCollection<MediaItem> presentItems,
        IReadOnlyCollection<Guid> confirmedMissingIds)
    {
        foreach (var item in presentItems) ValidatePathPair(item.Path, item.PathKey);
        Write(transaction =>
        {
            foreach (var item in presentItems)
            {
                Execute(transaction, """
                    DELETE FROM PlaybackProgress WHERE MediaItemId IN
                    (SELECT Id FROM MediaItem WHERE CategoryId=$category AND PathKey=$key
                     AND (FileSize<>$size OR LastWriteTimeUtc<>$time));
                    INSERT INTO MediaItem(Id,CategoryId,MediaType,Path,PathKey,FileSize,LastWriteTimeUtc)
                    VALUES($id,$category,$type,$path,$key,$size,$time)
                    ON CONFLICT(CategoryId,PathKey) DO UPDATE SET Path=excluded.Path,
                    FileSize=excluded.FileSize, LastWriteTimeUtc=excluded.LastWriteTimeUtc, IsMissing=0;
                    """, ("$id", Id(item.Id)), ("$category", Id(item.CategoryId)),
                    ("$type", item.MediaType.ToString()), ("$path", item.Path), ("$key", item.PathKey),
                    ("$size", item.FileSize), ("$time", item.LastWriteTimeUtc));
            }
            foreach (var id in confirmedMissingIds)
                RequireOne(Execute(transaction, "UPDATE MediaItem SET IsMissing=1 WHERE Id=$id;", ("$id", Id(id))));
            return 0;
        });
    }
    public void SetFavorite(Guid itemId, bool desiredValue) => Write(transaction =>
    {
        RequireOne(Execute(transaction, "UPDATE MediaItem SET IsFavorite=$value WHERE Id=$id;",
            ("$id", Id(itemId)), ("$value", desiredValue))); return 0;
    });
    public void SetRandomExcluded(Guid itemId, bool desiredValue) => Write(transaction =>
    {
        RequireOne(Execute(transaction, "UPDATE MediaItem SET IsRandomExcluded=$value WHERE Id=$id;",
            ("$id", Id(itemId)), ("$value", desiredValue))); return 0;
    });
    public void SaveSettings(AppSettings settings)
    {
        settings.Validate();
        Write(transaction =>
        {
            RequireOne(Execute(transaction,
                "UPDATE AppSettings SET HistoryExclusionDays=$days, ResumeMode=$mode WHERE Id=1;",
                ("$days", settings.HistoryExclusionDays), ("$mode", settings.ResumeMode.ToString())));
            return 0;
        });
    }

    // The complete Windows path/reparse validation belongs to T05. Storage requires canonical pairs.
    private static void ValidatePathPair(string path, string key)
    {
        if (string.IsNullOrWhiteSpace(path) || key != path.ToUpperInvariant())
            throw new ArgumentException("정규화된 경로와 PathKey가 필요합니다.");
    }
}
