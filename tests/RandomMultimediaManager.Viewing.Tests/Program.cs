using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.Viewing;
using RandomMultimediaManager.Core;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var surface = new Grid();
        var window = new Window { Content = surface, Width = 900, Height = 650 };
        window.Loaded += async (_, _) =>
        {
            int code = 0;
            try { await Verify(surface); }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            finally { window.Close(); app.Shutdown(code); }
        };
        return app.Run(window);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    private static async Task Verify(Grid surface)
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t11-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string comicPath = Path.Combine(root,"comic.cbz");
            using (var zip = ZipFile.Open(comicPath,ZipArchiveMode.Create))
            using (var bitmap = new SKBitmap(80,100))
            {
                bitmap.Erase(SKColors.Navy);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png,100);
                for (int i=0;i<3;i++) { using var stream=zip.CreateEntry($"{i}.png").Open(); png.SaveTo(stream); }
            }
            string videoPath=Path.Combine(root,"silent.mp4");
            File.Copy(Path.Combine(AppContext.BaseDirectory,"silent.mp4"),videoPath);
            using var db=LibraryDatabase.Open(Path.Combine(root,"library.db"));
            var comicCategory=new Category(Guid.NewGuid(),"Comic",MediaType.Comic);
            var videoCategory=new Category(Guid.NewGuid(),"Video",MediaType.Video);
            foreach(var category in new[]{comicCategory,videoCategory})
            {
                db.SaveCategory(category);
                db.AddSource(new(Guid.NewGuid(),category.Id,root,root.ToUpperInvariant()));
            }
            MediaItem Item(Category category,string path)
            {
                var file=new FileInfo(path);
                return new(Guid.NewGuid(),category.Id,category.MediaType,path,path.ToUpperInvariant(),file.Length,file.LastWriteTimeUtc.Ticks);
            }
            var comic=Item(comicCategory,comicPath); var video=Item(videoCategory,videoPath);
            db.ApplyObservedItems([comic,video],[]);
            db.SaveProgress(comic.Id,PlaybackProgress.Comic(2,.3),1);
            ViewingMedia? current=null;
            var content=new ContentControl(); surface.Children.Add(content);
            var videoSurface=new Grid(); surface.Children.Insert(0,videoSurface);
            var preparer=new ViewingMediaPreparer(videoSurface,m=>{current=m;content.Content=m.ComicContent;},m=>{if(ReferenceEquals(current,m)){current=null;content.Content=null;}});
            bool fail=false;
            var session=new SessionCoordinator(db,preparer,commitVisit:r=>fail?new(CommitStatus.Failed):db.CommitVisit(r));
            Check((await session.StartRandomAsync(Guid.NewGuid(),[comicCategory.Id])).Status==SessionStatus.Completed,"actual comic adapter starts random");
            var first=session.View.ActiveToken!;
            Check(current!.Capture(first)==PlaybackProgress.Comic(2,.3),"comic resume page and offset");
            Check(!session.ObserveProgress(first with{VisitId=Guid.NewGuid()},PlaybackProgress.Comic(0)),"late visit observation rejected");
            await session.SetSuppressedAsync(Guid.NewGuid(),first,true);
            Check((await session.OpenManualAsync(Guid.NewGuid(),video.Id)).Status==SessionStatus.Completed,"comic to actual video");
            Check(db.GetHistory(comic.Id).Count==0 && db.GetProgress(comic.Id)!.Position.ComicPageIndex==2,"suppressed visit saves progress only");
            var videoToken=session.View.ActiveToken!;
            Check(current!.Video!.Operation.SessionId==videoToken.SessionId && current.VideoVisit.VisitId==videoToken.VisitId,"video operation and visit mapping");
            current.Video.SetPaused(current.VideoVisit,true);
            var prior=current.Video.Snapshot(current.VideoVisit);
            var absent=video with{Id=Guid.NewGuid(),Path=Path.Combine(root,"absent.mp4"),PathKey=Path.Combine(root,"absent.mp4").ToUpperInvariant()};
            db.ApplyObservedItems([absent],[]);
            Check((await session.OpenManualAsync(Guid.NewGuid(),absent.Id)).Status==SessionStatus.Failed && session.View.ActiveToken==videoToken,"failed video preparation preserves active visit");
            Check(current.Video.Snapshot(current.VideoVisit).State==prior.State,"failed preparation preserves pause");
            fail=true;
            Check((await session.PreviousAsync(Guid.NewGuid())).Status==SessionStatus.SaveFailed,"actual adapters preserve frozen save failure");
            var frozen=session.View.FrozenCommit;
            Check(frozen is not null && session.View.ActiveToken==videoToken,"frozen payload and video remain owned");
            Check((await session.ResumeAfterSaveFailureAsync(Guid.NewGuid())).Status==SessionStatus.Completed,"confirmed rollback resumes current");
            Check(current.Video.Snapshot(current.VideoVisit).State==prior.State,"rollback restores paused state");
            fail=false;
            Check((await session.PreviousAsync(Guid.NewGuid())).Status==SessionStatus.Completed,"video to back comic");
            Check(session.View.ActiveToken!.VisitId!=first.VisitId && session.View.Pending!.SuppressHistory==false,"back creates unsuppressed visit");
            db.SetFavorite(comic.Id,true); db.SetRandomExcluded(comic.Id,true);
            Check((await session.NextAsync(Guid.NewGuid())).Status==SessionStatus.Completed,"forward reopens video");
            Check(db.GetHistory(comic.Id).Count==1,"back visit records independently from first suppression");
            Check((await session.PreviousAsync(Guid.NewGuid())).Status==SessionStatus.Completed,"permanent exclusion does not remove back slot");
            await session.SetSuppressedAsync(Guid.NewGuid(),session.View.ActiveToken!,true);
            Check((await session.LeaveAsync(Guid.NewGuid())).Status==SessionStatus.Completed,"normal leave commits and releases");
            Check(current is null && session.View.SessionId is null && videoSurface.Children.Count==0,"no current or prepared media remains");
            Check(db.GetHistory(comic.Id).Count==1 && db.GetItems(comicCategory.Id).Single().IsFavorite,"past history and favorite retained");
            using(var locked=File.Open(comicPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            using(var locked=File.Open(videoPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            Check(true,"comic and video handles released");
            using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
            var cancelResult=await preparer.PrepareAsync(comic,null,new(Guid.NewGuid(),Guid.NewGuid(),comic.Id),cancelled.Token);
            Check(cancelResult.Status==PreparationStatus.Cancelled,"actual adapter cancellation");
        }
        finally { Directory.Delete(root,true); }
    }
}
