using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.Viewing;
using RandomMultimediaManager.App.Views;
using RandomMultimediaManager.Core;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new VerificationApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var surface = new Grid();
        var window = new Window { Content = surface, Width = 900, Height = 650 };
        window.Loaded += async (_, _) =>
        {
            int code = 0;
            try { await Verify(surface); await T12NativeVerification.Run(surface); await T15NativeVerification.Run(); await T16VideoVerification.Run(surface); await T17LibraryBrowserVerification.Run(); await T13ViewingShortcutsVerification.Run(); await T18A2NativeVerification.Run();
            await T18A4NativeVerification.Run(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            finally { window.Close(); app.Shutdown(code); }
        };
        return app.Run(window);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject node)
            { yield return node; foreach (var descendant in Descendants(node)) yield return descendant; }
    }
    private static async Task Wait(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(45);
        while(!condition()) { if(DateTime.UtcNow>deadline) throw new TimeoutException("UI condition"); await Task.Delay(50); }
    }
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
            await Wait(() => current.Video.Snapshot(current.VideoVisit).State == LibVLCSharp.Shared.VLCState.Paused);
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
            db.SetRandomExcluded(comic.Id,false);
            db.SaveSettings(new(0));
            db.ApplyObservedItems([], [absent.Id]);
            var ui = new ViewingWindow(db);
            ui.Show();
            await Wait(() => ((ListBox)ui.FindName("Categories")).Items.Count == 2
                && ((FrameworkElement)ui.FindName("SessionControls")).IsEnabled);
            var categories = (ListBox)ui.FindName("Categories");
            categories.SelectAll();
            void Click(string label) => Descendants(ui).OfType<Button>().Single(b => Equals(b.Content,label))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("랜덤 시작");
            await Wait(() => ui.Current is not null && ((FrameworkElement)ui.FindName("SessionControls")).IsEnabled);
            var uiFirst=ui.Coordinator.View.ActiveToken!;
            Check(ui.Coordinator.View.Selected.Count==2,"actual UI multi-category selection");
            await Task.Delay(5500);
            await Wait(() => ((FrameworkElement)ui.FindName("SessionControls")).IsEnabled);
            Check(db.GetProgress(uiFirst.ItemId) is not null,"UI five-second progress checkpoint");
            Click("다음");
            await Wait(() => ui.Coordinator.View.ActiveToken != uiFirst && ((FrameworkElement)ui.FindName("SessionControls")).IsEnabled);
            Check(ui.Current!.Item.Id!=uiFirst.ItemId,"UI mixed media next");
            Click("이전");
            await Wait(() => ui.Coordinator.View.ActiveToken?.ItemId==uiFirst.ItemId && ((FrameworkElement)ui.FindName("SessionControls")).IsEnabled);
            Check(ui.Coordinator.View.ActiveToken!.VisitId!=uiFirst.VisitId,"UI back creates new visit");
            bool closed=false; ui.Closed+=(_,_)=>closed=true;
            ui.Close();
            await Wait(()=>closed);
            Check(ui.Coordinator.View.SessionId is null,"actual UI normal close finishes LeaveAsync");
            db.AddSource(new(Guid.NewGuid(), comicCategory.Id, root, root.ToUpperInvariant(), true, false));
            db.SetRandomExcluded(comic.Id, true);
            db.SaveCategory(comicCategory with { IsEnabled = false });
            var manual = new ViewingWindow(db, null, comic.Id);
            manual.Show();
            await Wait(() => manual.Current?.Item.Id == comic.Id
                && ((FrameworkElement)manual.FindName("SessionControls")).IsEnabled);
            var manualVisit = manual.Coordinator.View.Pending!;
            Check(manualVisit.Origin == VisitOrigin.Manual
                && db.GetHistory(comic.Id).All(history => history.VisitId != manualVisit.VisitId),
                "T17 manual open permits recently viewed, random-excluded items in inactive categories");
            bool manualClosed = false; manual.Closed += (_, _) => manualClosed = true;
            manual.Close();
            await Wait(() => manualClosed);
            Check(db.GetHistory(comic.Id).Any(history => history.VisitId == manualVisit.VisitId
                && history.Origin == VisitOrigin.Manual),
                "T17 manual viewing uses the existing normal leave and history commit");
            db.SaveCategory(comicCategory with { IsEnabled = true });
            db.SetRandomExcluded(comic.Id, false);
            string secondVideoPath = Path.Combine(root, "전방 항목.mp4");
            File.Copy(videoPath, secondVideoPath);
            var secondVideoFile = new FileInfo(secondVideoPath);
            var secondVideo = new MediaItem(Guid.NewGuid(), videoCategory.Id, MediaType.Video,
                secondVideoFile.FullName, secondVideoFile.FullName.ToUpperInvariant(), secondVideoFile.Length,
                secondVideoFile.LastWriteTimeUtc.Ticks);
            db.ApplyObservedItems([secondVideo], []);
            var browserSession = new ViewingWindow(db);
            browserSession.Show();
            await Wait(() => ((ListBox)browserSession.FindName("Categories")).Items.Count == 2
                && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            var videoCategoryPicker = (ListBox)browserSession.FindName("Categories");
            videoCategoryPicker.SelectedItem = videoCategoryPicker.Items.Cast<Category>()
                .Single(value => value.Id == videoCategory.Id);
            ClickOn(browserSession, "랜덤 시작");
            await Wait(() => browserSession.Current is not null
                && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            await ClickOnAndWait(browserSession, "다음",
                () => browserSession.Coordinator.View.Slots.Count == 2
                    && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            Guid forwardItemId = browserSession.Coordinator.View.Slots[1].ItemId;
            await ClickOnAndWait(browserSession, "이전",
                () => browserSession.Coordinator.View.Cursor == 0
                    && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            Guid priorItemId = browserSession.Coordinator.View.ActiveToken!.ItemId;
            await SelectFromLibraryAsync(browserSession, comicCategory, comic);
            await Wait(() => browserSession.Current?.Item.Id == comic.Id
                && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            Check(browserSession.Coordinator.View.Cursor == 1
                && browserSession.Coordinator.View.Slots.Count == 3
                && browserSession.Coordinator.View.Slots[0].ItemId == priorItemId
                && browserSession.Coordinator.View.Slots[1].ItemId == comic.Id
                && browserSession.Coordinator.View.Slots[2].ItemId == forwardItemId,
                "T17 manual library open inserts after the cursor and preserves Forward history");
            await ClickOnAndWait(browserSession, "다음",
                () => browserSession.Current?.Item.Id == forwardItemId
                    && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            var beforeNoOp = browserSession.Current;
            var tokenBeforeNoOp = browserSession.Coordinator.View.ActiveToken;
            await SelectFromLibraryAsync(browserSession, videoCategory,
                secondVideo.Id == forwardItemId ? secondVideo : video);
            await Wait(() => ReferenceEquals(browserSession.Current, beforeNoOp)
                && browserSession.Coordinator.View.ActiveToken == tokenBeforeNoOp
                && ((FrameworkElement)browserSession.FindName("SessionControls")).IsEnabled);
            Check(ReferenceEquals(browserSession.Current, beforeNoOp)
                && browserSession.Coordinator.View.ActiveToken == tokenBeforeNoOp
                && ((TextBlock)browserSession.FindName("Status")).Text == "NoOp",
                "T17 selecting the current ItemId is a no-op in the active viewing session");
            var beforeBrowserExit = browserSession.Current;
            var tokenBeforeBrowserExit = browserSession.Coordinator.View.ActiveToken;
            await RequestExitWithLibraryOpenAsync(browserSession);
            Check(ReferenceEquals(browserSession.Current, beforeBrowserExit)
                && browserSession.Coordinator.View.ActiveToken == tokenBeforeBrowserExit,
                "T17 exit request closes the library modal without changing the active visit");
            bool browserSessionClosed = false;
            browserSession.Closed += (_, _) => browserSessionClosed = true;
            browserSession.Close();
            await Wait(() => browserSessionClosed);
            // No current visit means LeaveAsync completes synchronously; Close must still run
            // after the original Closing event has unwound (one X, including NoCandidates).
            foreach (bool noCandidates in new[] { false, true })
            {
                var empty = new ViewingWindow(db);
                empty.Show();
                await Wait(() => ((ListBox)empty.FindName("Categories")).Items.Count == 2
                    && ((FrameworkElement)empty.FindName("SessionControls")).IsEnabled);
                if (noCandidates)
                {
                    db.SetRandomExcluded(comic.Id,true);
                    var choices=(ListBox)empty.FindName("Categories");
                    choices.SelectedItem=choices.Items.Cast<Category>().Single(c=>c.Id==comicCategory.Id);
                    Descendants(empty).OfType<Button>().Single(b=>Equals(b.Content,"랜덤 시작"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Wait(()=>((TextBlock)empty.FindName("Status")).Text.Contains("후보가 없습니다"));
                    await Wait(()=>((FrameworkElement)empty.FindName("SessionControls")).IsEnabled);
                }
                bool emptyClosed=false; empty.Closed+=(_,_)=>emptyClosed=true;
                empty.Close();
                await Wait(()=>emptyClosed);
                Check(true,noCandidates ? "one close after no candidates" : "one close before viewing");
            }

        }
        finally { Directory.Delete(root,true); }
    }
    private static void ClickOn(ViewingWindow window, string label) => Descendants(window).OfType<Button>()
        .Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task ClickOnAndWait(ViewingWindow window, string label, Func<bool> condition)
    {
        ClickOn(window, label);
        await Wait(condition);
    }

    private static async Task SelectFromLibraryAsync(ViewingWindow host, Category category, MediaItem item)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try
            {
                var dialog = Application.Current.Windows.OfType<Window>()
                    .Single(window => window.Title == "라이브러리에서 파일 선택");
                var browser = (LibraryBrowserView)dialog.Content;
                var viewModel = (RandomMultimediaManager.App.ViewModels.LibraryBrowserViewModel)browser.DataContext;
                await Wait(() => viewModel.Categories.Count > 0 && viewModel.VisibleItems.Count > 0);
                var picker = (ComboBox)browser.FindName("CategoryPicker");
                picker.SelectedItem = viewModel.Categories.Single(value => value.Id == category.Id);
                await Wait(() => viewModel.VisibleItems.Any(row => row.Item.Id == item.Id));
                var list = (ListBox)browser.FindName("FileList");
                list.SelectedItem = viewModel.VisibleItems.Single(row => row.Item.Id == item.Id);
                await Wait(() => viewModel.SelectedItem?.Item.Id == item.Id);
                Descendants(browser).OfType<Button>().Single(button => Equals(button.Content, "감상 열기"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        };
        timer.Start();
        ClickOn(host, "라이브러리에서 열기");
        await completion.Task;
        await Wait(() => !Application.Current.Windows.OfType<Window>()
                .Any(window => window.Title == "라이브러리에서 파일 선택")
            && ((FrameworkElement)host.FindName("SessionControls")).IsEnabled);
    }

    private static async Task RequestExitWithLibraryOpenAsync(ViewingWindow host)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                _ = Application.Current.Windows.OfType<Window>()
                    .Single(window => window.Title == "라이브러리에서 파일 선택");
                host.SetExitRequested(true);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        };
        timer.Start();
        ClickOn(host, "라이브러리에서 열기");
        await completion.Task;
        await Wait(() => !Application.Current.Windows.OfType<Window>()
                .Any(window => window.Title == "라이브러리에서 파일 선택")
            && !((FrameworkElement)host.FindName("SessionControls")).IsEnabled);
        host.SetExitRequested(false);
        await Wait(() => ((FrameworkElement)host.FindName("SessionControls")).IsEnabled);
    }
}

// Exercise App's real SessionEnding routing without starting the user's production database.
internal sealed class VerificationApp : RandomMultimediaManager.App.App
{
    protected override void OnStartup(StartupEventArgs e) { }
    public void RaiseSessionEnding(SessionEndingCancelEventArgs e) => base.OnSessionEnding(e);
}
