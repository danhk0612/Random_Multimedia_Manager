using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RandomMultimediaManager.App.Data;
using RandomMultimediaManager.App.Deletion;
using RandomMultimediaManager.App.Sessions;
using RandomMultimediaManager.App.Viewing;
using RandomMultimediaManager.Core;
using SkiaSharp;

internal static class T12NativeVerification
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS T12 Windows: " + message); }
    public static async Task Run(Grid surface)
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-t12-native-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            DeletionRecord Record(string path, DeletionMode mode) => new(1,Guid.NewGuid(),path.ToUpperInvariant(),path,
                [new(Guid.NewGuid(),1,1)],mode,1,DeletionPhase.Prepared);
            foreach (var mode in new[] { DeletionMode.Recycle, DeletionMode.Permanent })
            {
                string file=Path.Combine(root,mode+".txt"); File.WriteAllText(file,"synthetic T12 copy");
                var result=await WindowsFileDeletion.DeleteAsync(Record(file,mode));
                Check(result.Outcome==DeletionOutcome.Succeeded && !File.Exists(file), mode+" OS result: "+result);
                var missing=await WindowsFileDeletion.DeleteAsync(Record(file,mode));
                Check(missing.Outcome==DeletionOutcome.Failed && missing.Missing, "already missing is not success");
                string locked=Path.Combine(root,mode+"-locked.txt"); File.WriteAllText(locked,"temporary test");
                using(var handle=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    var failed=await WindowsFileDeletion.DeleteAsync(Record(locked,mode));
                    Check(failed.Outcome!=DeletionOutcome.Succeeded && File.Exists(locked), mode+" locked file preserved");
                }
            }
            var sink=new WindowsFileDeletion.RecycleSink();
            Check(sink.PreDeleteItem(0,IntPtr.Zero)<0 && sink.Vetoed,"non-recycle fallback veto");
            string protectedDir=Path.Combine(root,"protected"); Directory.CreateDirectory(protectedDir);
            string protectedFile=Path.Combine(protectedDir,"denied.txt"); File.WriteAllText(protectedFile,"temporary ACL test");
            var identity=WindowsIdentity.GetCurrent().User!;
            var directoryInfo=new DirectoryInfo(protectedDir); var originalDir=directoryInfo.GetAccessControl();
            var fileInfo=new FileInfo(protectedFile); var originalFile=fileInfo.GetAccessControl();
            try
            {
                var ds=directoryInfo.GetAccessControl(); ds.AddAccessRule(new FileSystemAccessRule(identity,FileSystemRights.DeleteSubdirectoriesAndFiles,AccessControlType.Deny)); directoryInfo.SetAccessControl(ds);
                var fs=fileInfo.GetAccessControl(); fs.AddAccessRule(new FileSystemAccessRule(identity,FileSystemRights.Delete,AccessControlType.Deny)); fileInfo.SetAccessControl(fs);
                foreach(var mode in new[]{DeletionMode.Recycle,DeletionMode.Permanent})
                {
                    var denied=await WindowsFileDeletion.DeleteAsync(Record(protectedFile,mode));
                    Check(denied.Outcome!=DeletionOutcome.Succeeded && File.Exists(protectedFile),mode+" permission denied preserves file");
                }
            }
            finally { fileInfo.SetAccessControl(originalFile); directoryInfo.SetAccessControl(originalDir); }

            using var db=LibraryDatabase.Open(Path.Combine(root,"library.db"));
            var cat=new Category(Guid.NewGuid(),"T12",MediaType.Comic); db.SaveCategory(cat);
            string comic=Path.Combine(root,"synthetic.cbz");
            using(var zip=ZipFile.Open(comic,ZipArchiveMode.Create))
            using(var bitmap=new SKBitmap(30,40))
            { bitmap.Erase(SKColors.Blue); using var img=SKImage.FromBitmap(bitmap); using var png=img.Encode(SKEncodedImageFormat.Png,100); using var stream=zip.CreateEntry("1.png").Open(); png.SaveTo(stream); }
            var item=new MediaItem(Guid.NewGuid(),cat.Id,MediaType.Comic,comic,comic.ToUpperInvariant(),new FileInfo(comic).Length,1);
            db.ApplyObservedItems([item],[]);
            var service=new DeletionService(db,new DeletionJournal(Path.Combine(root,"delete-journal")),WindowsFileDeletion.DeleteAsync);
            var session=new SessionCoordinator(db,new ViewingMediaPreparer(surface,_=>{},_=>{}));
            Check((await session.OpenManualAsync(Guid.NewGuid(),item.Id)).Status==SessionStatus.Completed,"actual comic ready before delete");
            var deleted=await session.DeleteCurrentAsync(Guid.NewGuid(),service,DeletionMode.Permanent);
            Check(deleted.Status==SessionStatus.Completed && session.View.Pending is null && !File.Exists(comic),"comic resources released before OS delete");
            var videoCat=new Category(Guid.NewGuid(),"T12 Video",MediaType.Video); db.SaveCategory(videoCat);
            string video=Path.Combine(root,"copy.mp4"); File.Copy(Path.Combine(AppContext.BaseDirectory,"silent.mp4"),video);
            var videoItem=new MediaItem(Guid.NewGuid(),videoCat.Id,MediaType.Video,video,video.ToUpperInvariant(),new FileInfo(video).Length,1);
            db.ApplyObservedItems([videoItem],[]);
            Check((await session.OpenManualAsync(Guid.NewGuid(),videoItem.Id)).Status==SessionStatus.Completed,"actual video ready before delete");
            deleted=await session.DeleteCurrentAsync(Guid.NewGuid(),service,DeletionMode.Recycle);
            Check(deleted.Status==SessionStatus.Completed && session.View.Pending is null && !File.Exists(video),"video resources released before recycle");

            var dialog=new DeleteConfirmationWindow("synthetic-file-only.cbz");
            dialog.Loaded+=(_,_)=>{
                var panel=(StackPanel)dialog.Content;
                Check(panel.Children.OfType<RadioButton>().First().IsChecked==true,"confirmation defaults to recycle");
                dialog.Dispatcher.BeginInvoke(()=>dialog.Close(),DispatcherPriority.Background);
            };
            dialog.ShowDialog();
            Check(dialog.Selection is null,"closing confirmation cancels without mutation");
        }
        finally { Directory.Delete(root,true); }
    }
}
