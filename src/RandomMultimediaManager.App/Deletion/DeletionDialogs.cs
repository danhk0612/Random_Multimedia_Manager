using System.Windows;
using System.Windows.Controls;
using RandomMultimediaManager.App.Sessions;

namespace RandomMultimediaManager.App.Deletion;

public sealed class DeleteConfirmationWindow : Window
{
    public DeletionMode? Selection { get; private set; }
    public DeleteConfirmationWindow(string path)
    {
        Title = "현재 파일 삭제"; Width = 540; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"{path}\n\n실제 파일을 삭제합니다. 같은 경로가 등록된 모든 분류의 감상 기록과 진행 위치도 제거됩니다. 즐겨찾기와 영구 랜덤 제외는 유지됩니다.", TextWrapping = TextWrapping.Wrap });
        var recycle = new RadioButton { Content = "휴지통으로 이동 (기본)", IsChecked = true, Margin = new Thickness(0,16,0,8) };
        var permanent = new RadioButton { Content = "영구 삭제 — 휴지통에서 복원할 수 없음" };
        panel.Children.Add(recycle); panel.Children.Add(permanent);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        var confirm = new Button { Content = "선택한 방법으로 삭제", Padding = new Thickness(12,6,12,6) };
        var cancel = new Button { Content = "취소", IsCancel = true, IsDefault = true, Padding = new Thickness(12,6,12,6), Margin = new Thickness(8,0,0,0) };
        confirm.Click += (_, _) => { Selection = permanent.IsChecked == true ? DeletionMode.Permanent : DeletionMode.Recycle; DialogResult = true; };
        buttons.Children.Add(confirm); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = panel;
    }
}

public static class DeletionDialogs
{
    public static async Task RecoverAsync(DeletionService service, Window? owner = null, SessionCoordinator? coordinator = null)
    {
        foreach (var entry in service.Pending.ToArray())
        {
            if (entry.Record is { Phase: DeletionPhase.Succeeded or DeletionPhase.Failed or DeletionPhase.Cancelled } record)
            {
                var retry = await service.CompleteAsync(record);
                if (!retry.Resolved) Show(owner, "삭제 DB/저널 정리를 완료하지 못했습니다. 경로 격리를 유지합니다.\n" + retry.Error);
                continue;
            }
            string message = entry.Record is null
                ? $"삭제 저널이 손상되어 대상을 확인할 수 없습니다.\n{entry.File}\n{entry.Error}\n\n‘아니요’는 삭제 실패/취소로 확인하여 모든 기록을 보존합니다. 성공 처리는 대상을 알 수 없어 불가능합니다. 확인할 수 없으면 ‘취소’를 선택하세요. 감상과 라이브러리 쓰기는 보류됩니다."
                : $"삭제 성공 여부가 불명확합니다. 파일 부재만으로 성공 처리하지 않습니다.\n{entry.Record.Path}\n\n예: 삭제 성공을 확인함 — 대상 기록/진행 정리\n아니요: 실패/취소를 확인함 — 기록 보존\n취소: 지금 확인하지 않음 — 경로 격리 유지\n\n어떤 선택도 파일 삭제를 다시 실행하지 않습니다.";
            var answer = owner is null ? MessageBox.Show(message, "삭제 복구 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
                : MessageBox.Show(owner, message, "삭제 복구 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel) continue;
            if (coordinator is not null)
            {
                var result = await coordinator.ResolveDeletionAsync(Guid.NewGuid(), service, entry, answer == MessageBoxResult.Yes);
                if (result.Error is not null) Show(owner, result.Error);
            }
            else
            {
                var result = await service.ConfirmAsync(entry, answer == MessageBoxResult.Yes);
                if (!result.Resolved) Show(owner, result.Error ?? "격리를 유지합니다.");
            }
        }
    }
    private static void Show(Window? owner, string text)
    {
        if (owner is null) MessageBox.Show(text, "삭제 복구");
        else MessageBox.Show(owner, text, "삭제 복구");
    }
}
