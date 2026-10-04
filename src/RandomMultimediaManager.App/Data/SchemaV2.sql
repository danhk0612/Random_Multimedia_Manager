CREATE TABLE StorageBinding (
 RootKey TEXT PRIMARY KEY NOT NULL COLLATE BINARY,
 Kind TEXT NOT NULL CHECK(Kind IN ('Local','RemoteMapped','RemoteUNC','VirtualOrUnknown')),
 ExpectedTarget TEXT,
 EvidenceKind TEXT NOT NULL CHECK(EvidenceKind IN ('None','LocalDeviceAndVolume','WindowsMapping','UncName','UserConfirmation')),
 Revision INTEGER NOT NULL CHECK(Revision>=0),
 RequiresConfirmation INTEGER NOT NULL CHECK(RequiresConfirmation IN (0,1)),
 Provider TEXT,
 Device TEXT,
 Volume TEXT
);
CREATE TABLE SourceRefreshPolicy (
 SourceId TEXT PRIMARY KEY NOT NULL REFERENCES CategorySource(Id) ON DELETE CASCADE,
 ScanOnStartup INTEGER NOT NULL CHECK(ScanOnStartup IN (0,1)),
 RefreshMode TEXT NOT NULL CHECK(RefreshMode IN ('Manual','Events','Scheduled')),
 IntervalHours INTEGER,
 LastCompletedAtUtc INTEGER,
 CHECK((RefreshMode='Manual' AND IntervalHours IS NULL) OR
       (RefreshMode IN ('Events','Scheduled') AND IntervalHours IS NOT NULL AND IntervalHours BETWEEN 1 AND 168))
);
