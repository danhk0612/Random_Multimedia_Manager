using Microsoft.Win32;
using RandomMultimediaManager.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RandomMultimediaManager.App.Media.Comic;

public partial class ComicViewerWindow : Window
{
    private static readonly SKSamplingOptions HighQualitySampling = new(SKCubicResampler.Mitchell);
    private readonly ComicViewerViewModel _viewModel = new();
    private CancellationTokenSource? _operation;
    private Point? _dragStart;
    private double _dragPanX;
    private double _dragPanY;
    private bool _closing;

    public ComicViewerWindow()
    {
        InitializeComponent();
        UpdateUi();
    }

    public async Task OpenAsync(string path, PlaybackProgress? progress = null)
    {
        CancellationTokenSource next = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _operation, next);
        previous?.Cancel();
        previous?.Dispose();

        StatusText.Text = "준비 중...";
        try
        {
            ComicPrepareResult result = await _viewModel.PrepareAsync(path, progress, next.Token);
            if (next.IsCancellationRequested)
                return;
            if (result.Status != ComicPrepareStatus.Ready || result.Comic is null)
            {
                StatusText.Text = result.Status == ComicPrepareStatus.Cancelled
                    ? "준비 취소"
                    : $"열기 실패: {result.Error ?? result.Status.ToString()}";
                return;
            }

            _viewModel.Activate(result.Comic, progress);
            await EnsureVisiblePagesAsync(next.Token);
            StatusText.Text = System.IO.Path.GetFileName(path);
            UpdateUi();
            Canvas.InvalidateVisual();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "준비 취소";
        }
    }

    public PlaybackProgress GetProgress() => _viewModel.GetProgress();

    private async void OpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "만화 압축 (*.zip;*.cbz)|*.zip;*.cbz|모든 파일 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            await OpenAsync(dialog.FileName);
    }

    private async void PreviousPage(object sender, RoutedEventArgs e) => await MoveAsync(-1);
    private async void NextPage(object sender, RoutedEventArgs e) => await MoveAsync(1);

    private async Task MoveAsync(int delta)
    {
        if (!_viewModel.IsReady)
            return;
        CancellationToken token = _operation?.Token ?? CancellationToken.None;
        if (await _viewModel.MoveAsync(delta, token))
        {
            await EnsureVisiblePagesAsync(token);
            UpdateUi();
            Canvas.InvalidateVisual();
        }
    }

    private async Task EnsureVisiblePagesAsync(CancellationToken cancellationToken)
    {
        if (!_viewModel.IsReady)
            return;
        await _viewModel.EnsurePageAsync(_viewModel.CurrentPageIndex, cancellationToken);
        if (_viewModel.DisplayMode == ComicDisplayMode.TwoPage)
        {
            int second = _viewModel.GetSecondPageIndex();
            if (second >= 0)
                await _viewModel.EnsurePageAsync(second, cancellationToken);
        }
        else if (_viewModel.DisplayMode == ComicDisplayMode.VerticalScroll)
        {
            int previous = _viewModel.CurrentPageIndex - 1;
            int next = _viewModel.CurrentPageIndex + 1;
            if (previous >= 0)
                await _viewModel.EnsurePageAsync(previous, cancellationToken);
            if (next < _viewModel.PageCount)
                await _viewModel.EnsurePageAsync(next, cancellationToken);
        }
    }

    private async void DisplayModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DisplayModeBox.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag?.ToString(), out ComicDisplayMode mode))
        {
            _viewModel.DisplayMode = mode;
            if (_viewModel.IsReady)
                await EnsureVisiblePagesAsync(_operation?.Token ?? CancellationToken.None);
            UpdateUi();
            Canvas?.InvalidateVisual();
        }
    }

    private void DirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DirectionBox.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag?.ToString(), out ComicReadingDirection direction))
        {
            _viewModel.ReadingDirection = direction;
            Canvas?.InvalidateVisual();
        }
    }

    private void FitModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FitModeBox.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag?.ToString(), out ComicFitMode mode))
        {
            _viewModel.SetFitMode(mode);
            UpdateUi();
            Canvas?.InvalidateVisual();
        }
    }

    private async void ViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.IsReady)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _viewModel.AdjustZoom(e.Delta);
            UpdateUi();
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_viewModel.DisplayMode == ComicDisplayMode.VerticalScroll)
        {
            await _viewModel.ScrollVerticalAsync(e.Delta > 0 ? -0.12 : 0.12, _operation?.Token ?? CancellationToken.None);
            await EnsureVisiblePagesAsync(_operation?.Token ?? CancellationToken.None);
            UpdateUi();
            Canvas.InvalidateVisual();
        }
        else
        {
            await MoveAsync(e.Delta > 0 ? -1 : 1);
        }
        e.Handled = true;
    }

    private void ViewerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsReady)
            return;
        _dragStart = e.GetPosition(ViewerHost);
        _dragPanX = _viewModel.PanX;
        _dragPanY = _viewModel.PanY;
        ViewerHost.CaptureMouse();
    }

    private void ViewerMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = e.GetPosition(ViewerHost);
        _viewModel.PanX = _dragPanX + current.X - start.X;
        _viewModel.PanY = _dragPanY + current.Y - start.Y;
        Canvas.InvalidateVisual();
    }

    private void ViewerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ViewerHost.ReleaseMouseCapture();
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Right or Key.PageDown)
        {
            await MoveAsync(_viewModel.ReadingDirection == ComicReadingDirection.LeftToRight ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.PageUp)
        {
            await MoveAsync(_viewModel.ReadingDirection == ComicReadingDirection.LeftToRight ? -1 : 1);
            e.Handled = true;
        }
    }

    private void PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(22, 22, 22));
        if (!_viewModel.IsReady)
            return;

        if (_viewModel.DisplayMode == ComicDisplayMode.SinglePage)
            DrawSingle(canvas, e.Info.Width, e.Info.Height);
        else if (_viewModel.DisplayMode == ComicDisplayMode.TwoPage)
            DrawSpread(canvas, e.Info.Width, e.Info.Height);
        else
            DrawVertical(canvas, e.Info.Width, e.Info.Height);
    }

    private void DrawSingle(SKCanvas canvas, int width, int height)
    {
        SKBitmap? bitmap = _viewModel.GetPage(_viewModel.CurrentPageIndex);
        if (bitmap is null)
            return;
        SKRect rect = CalculateDestination(bitmap, width, height, 0, 0, width, height);
        canvas.DrawBitmap(bitmap, rect, HighQualitySampling);
    }

    private void DrawSpread(SKCanvas canvas, int width, int height)
    {
        (int leftIndex, int rightIndex) = _viewModel.GetSpreadOrder();
        float half = width / 2f;
        if (leftIndex >= 0 && _viewModel.GetPage(leftIndex) is SKBitmap left)
        {
            SKRect rect = CalculateDestination(left, width, height, 0, 0, half, height);
            canvas.DrawBitmap(left, rect, HighQualitySampling);
        }
        if (rightIndex >= 0 && _viewModel.GetPage(rightIndex) is SKBitmap right)
        {
            SKRect rect = CalculateDestination(right, width, height, half, 0, half, height);
            canvas.DrawBitmap(right, rect, HighQualitySampling);
        }
    }

    private void DrawVertical(SKCanvas canvas, int width, int height)
    {
        int current = _viewModel.CurrentPageIndex;
        float anchorY = (float)(-_viewModel.CurrentPageOffset * height);
        DrawVerticalPage(canvas, current, width, height, anchorY);
        if (current > 0)
            DrawVerticalPage(canvas, current - 1, width, height, anchorY - height);
        if (current + 1 < _viewModel.PageCount)
            DrawVerticalPage(canvas, current + 1, width, height, anchorY + height);
    }

    private void DrawVerticalPage(SKCanvas canvas, int index, int width, int height, float y)
    {
        SKBitmap? bitmap = _viewModel.GetPage(index);
        if (bitmap is null)
            return;
        SKRect rect = CalculateDestination(bitmap, width, height, 0, y, width, height);
        canvas.DrawBitmap(bitmap, rect, HighQualitySampling);
    }

    private SKRect CalculateDestination(SKBitmap bitmap, float surfaceWidth, float surfaceHeight,
        float regionX, float regionY, float regionWidth, float regionHeight)
    {
        double scale = _viewModel.FitMode switch
        {
            ComicFitMode.Original => 1.0,
            ComicFitMode.FitWidth => regionWidth / bitmap.Width,
            ComicFitMode.FitHeight => regionHeight / bitmap.Height,
            ComicFitMode.Custom => _viewModel.Zoom,
            _ => Math.Min(regionWidth / bitmap.Width, regionHeight / bitmap.Height)
        };
        if (_viewModel.FitMode == ComicFitMode.Custom)
            scale = _viewModel.Zoom;
        else
            scale *= _viewModel.Zoom;

        float drawWidth = (float)(bitmap.Width * scale);
        float drawHeight = (float)(bitmap.Height * scale);
        float x = regionX + (regionWidth - drawWidth) / 2f + (float)_viewModel.PanX;
        float y = regionY + (regionHeight - drawHeight) / 2f + (float)_viewModel.PanY;
        return new SKRect(x, y, x + drawWidth, y + drawHeight);
    }

    private void UpdateUi()
    {
        if (PageText is null)
            return;
        PageText.Text = _viewModel.PageCount == 0
            ? "0 / 0"
            : $"{_viewModel.CurrentPageIndex + 1} / {_viewModel.PageCount}";
        ZoomText.Text = _viewModel.FitMode == ComicFitMode.Custom
            ? $"{_viewModel.Zoom * 100:0}%"
            : _viewModel.FitMode.ToString();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closing)
        {
            base.OnClosing(e);
            return;
        }
        _closing = true;
        CancellationTokenSource? operation = Interlocked.Exchange(ref _operation, null);
        operation?.Cancel();
        operation?.Dispose();
        _viewModel.Dispose();
        base.OnClosing(e);
    }
}
