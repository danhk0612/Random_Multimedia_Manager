CREATE TABLE Category (
 Id TEXT PRIMARY KEY NOT NULL,
 Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 100),
 MediaType TEXT NOT NULL CHECK(MediaType IN ('Comic','Video')),
 IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK(IsEnabled IN (0,1)),
 UNIQUE(Id, MediaType)
);
CREATE TABLE CategorySource (
 Id TEXT PRIMARY KEY NOT NULL,
 CategoryId TEXT NOT NULL REFERENCES Category(Id) ON DELETE CASCADE,
 RootPath TEXT NOT NULL,
 RootPathKey TEXT NOT NULL COLLATE BINARY,
 IncludeSubdirectories INTEGER NOT NULL DEFAULT 1 CHECK(IncludeSubdirectories IN (0,1)),
 IsEnabled INTEGER NOT NULL DEFAULT 1 CHECK(IsEnabled IN (0,1)),
 UNIQUE(CategoryId, RootPathKey)
);
CREATE TABLE MediaItem (
 Id TEXT PRIMARY KEY NOT NULL,
 CategoryId TEXT NOT NULL,
 MediaType TEXT NOT NULL CHECK(MediaType IN ('Comic','Video')),
 Path TEXT NOT NULL,
 PathKey TEXT NOT NULL COLLATE BINARY,
 FileSize INTEGER NOT NULL CHECK(FileSize >= 0),
 LastWriteTimeUtc INTEGER NOT NULL,
 IsFavorite INTEGER NOT NULL DEFAULT 0 CHECK(IsFavorite IN (0,1)),
 IsRandomExcluded INTEGER NOT NULL DEFAULT 0 CHECK(IsRandomExcluded IN (0,1)),
 IsMissing INTEGER NOT NULL DEFAULT 0 CHECK(IsMissing IN (0,1)),
 FOREIGN KEY(CategoryId, MediaType) REFERENCES Category(Id, MediaType),
 UNIQUE(CategoryId, PathKey),
 UNIQUE(Id, MediaType)
);
CREATE INDEX IX_Item_Path ON MediaItem(PathKey);
CREATE INDEX IX_Item_Candidates ON MediaItem(CategoryId, IsMissing, IsRandomExcluded);
CREATE TABLE ViewHistory (
 VisitId TEXT PRIMARY KEY NOT NULL,
 MediaItemId TEXT NOT NULL REFERENCES MediaItem(Id) ON DELETE CASCADE,
 ViewedAtUtc INTEGER NOT NULL,
 Origin TEXT NOT NULL CHECK(Origin IN ('Random','Manual','Back','Forward'))
);
CREATE INDEX IX_History_ItemTime ON ViewHistory(MediaItemId, ViewedAtUtc DESC);
CREATE TABLE PlaybackProgress (
 MediaItemId TEXT PRIMARY KEY NOT NULL,
 MediaType TEXT NOT NULL CHECK(MediaType IN ('Comic','Video')),
 ComicPageIndex INTEGER,
 ComicPageOffset REAL,
 VideoPositionMs INTEGER,
 UpdatedAtUtc INTEGER NOT NULL,
 FOREIGN KEY(MediaItemId, MediaType) REFERENCES MediaItem(Id, MediaType) ON DELETE CASCADE,
 CHECK(
  (MediaType='Comic' AND ComicPageIndex IS NOT NULL AND ComicPageIndex>=0
   AND ComicPageOffset IS NOT NULL AND ComicPageOffset BETWEEN 0 AND 1
   AND VideoPositionMs IS NULL)
  OR
  (MediaType='Video' AND VideoPositionMs IS NOT NULL AND VideoPositionMs>=0
   AND ComicPageIndex IS NULL AND ComicPageOffset IS NULL)
 )
);
CREATE TABLE AppSettings (
 Id INTEGER PRIMARY KEY CHECK(Id=1),
 HistoryExclusionDays INTEGER NOT NULL DEFAULT 7 CHECK(HistoryExclusionDays BETWEEN 0 AND 36500),
 ResumeMode TEXT NOT NULL DEFAULT 'Resume' CHECK(ResumeMode IN ('Resume','FromStart'))
);
INSERT INTO AppSettings(Id) VALUES(1);
CREATE TABLE AppliedDeletion (
 OperationId TEXT PRIMARY KEY NOT NULL
);
CREATE TABLE VisitCommit (
 VisitId TEXT PRIMARY KEY NOT NULL,
 MediaItemId TEXT NOT NULL REFERENCES MediaItem(Id) ON DELETE CASCADE,
 PayloadHash BLOB NOT NULL CHECK(length(PayloadHash)=32)
);
CREATE INDEX IX_VisitCommit_Item ON VisitCommit(MediaItemId);
