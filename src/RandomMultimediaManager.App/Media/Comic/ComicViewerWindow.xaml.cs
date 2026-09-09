using Microsoft.Win32;
using RandomMultimediaManager.Core;
using SkiaSharp;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RandomMultimediaManager.App.Media.Comic;

public partial class ComicViewerWindow : Window
{
    private static readonly string RenderLogPath = Path.Combine(Path.GetTempPath(), "RandomMultimediaManager-T08-render.log");
    private static readonly string RenderFramePath = Path.Combine(Path.GetTempPath(), "RandomMultimediaManager-T08-frame.png");
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
            RenderCanvas();
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
            RenderCanvas();
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
            RenderCanvas();
        }
    }

    private void DirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DirectionBox.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag?.ToString(), out ComicReadingDirection direction))
        {
            _viewModel.ReadingDirection = direction;
            RenderCanvas();
        }
    }

    private void FitModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FitModeBox.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag?.ToString(), out ComicFitMode mode))
        {
            _viewModel.SetFitMode(mode);
            UpdateUi();
            RenderCanvas();
        }
    }

    private void ViewerSizeChanged(object sender, SizeChangedEventArgs e) => RenderCanvas();

    private async void ViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.IsReady)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _viewModel.AdjustZoom(e.Delta);
            UpdateUi();
            RenderCanvas();
            e.Handled = true;
            return;
        }

        if (_viewModel.DisplayMode == ComicDisplayMode.VerticalScroll)
        {
            await _viewModel.ScrollVerticalAsync(e.Delta > 0 ? -0.12 : 0.12, _operation?.Token ?? CancellationToken.None);
            await EnsureVisiblePagesAsync(_operation?.Token ?? CancellationToken.None);
            UpdateUi();
            RenderCanvas();
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
        RenderCanvas();
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

    private void RenderCanvas()
    {
        if (Canvas is null || ViewerHost is null || ViewerHost.ActualWidth <= 0 || ViewerHost.ActualHeight <= 0)
            return;

        if (!_viewModel.IsReady)
        {
            Canvas.Source = null;
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Max(1, (int)Math.Ceiling(ViewerHost.ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(ViewerHost.ActualHeight * dpi.DpiScaleY));
        AppendRenderLog($"Render mode={_viewModel.DisplayMode} host={ViewerHost.ActualWidth:0.##}x{ViewerHost.ActualHeight:0.##} dpi={dpi.DpiScaleX:0.##}x{dpi.DpiScaleY:0.##} frame={width}x{height} imageControl={Canvas.ActualWidth:0.##}x{Canvas.ActualHeight:0.##}");

        using var frame = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(frame);
        canvas.Clear(new SKColor(22, 22, 22));

        if (_viewModel.DisplayMode == ComicDisplayMode.SinglePage)
            DrawSingle(canvas, width, height);
        else if (_viewModel.DisplayMode == ComicDisplayMode.TwoPage)
            DrawSpread(canvas, width, height);
        else
            DrawVertical(canvas, width, height);

        canvas.Flush();
        DumpFrameDiagnostics(frame);

        int bufferSize = checked(frame.RowBytes * frame.Height);
        byte[] pixels = new byte[bufferSize];
        Marshal.Copy(frame.GetPixels(), pixels, 0, bufferSize);
        BitmapSource source = BitmapSource.Create(
            width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
            PixelFormats.Bgra32, null, pixels, frame.RowBytes);
        source.Freeze();
        Canvas.Source = source;
        AppendRenderLog($"Source pixel={source.PixelWidth}x{source.PixelHeight} dip={source.Width:0.##}x{source.Height:0.##} stride={frame.RowBytes}");
    }

    private static SKSamplingOptions CreateHighQualitySampling()
        => new(new SKCubicResampler(1f / 3f, 1f / 3f));

    private void DrawSingle(SKCanvas canvas, int width, int height)
    {
        SKBitmap? bitmap = _viewModel.GetPage(_viewModel.CurrentPageIndex);
        if (bitmap is null)
            return;
        SKRect rect = CalculateDestination(bitmap, 0, 0, width, height);
        AppendRenderLog($"Single page={_viewModel.CurrentPageIndex} bitmap={bitmap.Width}x{bitmap.Height} color={bitmap.ColorType}/{bitmap.AlphaType} rect=({rect.Left:0.##},{rect.Top:0.##})-({rect.Right:0.##},{rect.Bottom:0.##}) {rect.Width:0.##}x{rect.Height:0.##} fit={_viewModel.FitMode} zoom={_viewModel.Zoom:0.###}");
        canvas.DrawBitmap(bitmap, rect, CreateHighQualitySampling());
    }

    private void DrawSpread(SKCanvas canvas, int width, int height)
    {
        (int leftIndex, int rightIndex) = _viewModel.GetSpreadOrder();
        float half = width / 2f;
        if (leftIndex >= 0 && _viewModel.GetPage(leftIndex) is SKBitmap left)
        {
            SKRect rect = CalculateDestination(left, 0, 0, half, height);
            canvas.DrawBitmap(left, rect, CreateHighQualitySampling());
        }
        if (rightIndex >= 0 && _viewModel.GetPage(rightIndex) is SKBitmap right)
        {
            SKRect rect = CalculateDestination(right, half, 0, half, height);
            canvas.DrawBitmap(right, rect, CreateHighQualitySampling());
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
        SKRect rect = CalculateDestination(bitmap, 0, y, width, height);
        canvas.DrawBitmap(bitmap, rect, CreateHighQualitySampling());
    }

    private SKRect CalculateDestination(SKBitmap bitmap, float regionX, float regionY, float regionWidth, float regionHeight)
    {
        double scale = _viewModel.FitMode switch
        {
            ComicFitMode.Original => 1.0,
            ComicFitMode.FitWidth => regionWidth / bitmap.Width,
            ComicFitMode.FitHeight => regionHeight / bitmap.Height,
            ComicFitMode.Custom => _viewModel.Zoom,
            _ => Math.Min(regionWidth / bitmap.Width, regionHeight / bitmap.Height)
        };
        if (_viewModel.FitMode != ComicFitMode.Custom)
            scale *= _viewModel.Zoom;

        float drawWidth = (float)(bitmap.Width * scale);
        float drawHeight = (float)(bitmap.Height * scale);
        float x = regionX + (regionWidth - drawWidth) / 2f + (float)(_viewModel.PanX * VisualTreeHelper.GetDpi(this).DpiScaleX);
        float y = regionY + (regionHeight - drawHeight) / 2f + (float)(_viewModel.PanY * VisualTreeHelper.GetDpi(this).DpiScaleY);
        return new SKRect(x, y, x + drawWidth, y + drawHeight);
    }

    private static void DumpFrameDiagnostics(SKBitmap frame)
    {
        try
        {
            SKColor top = frame.GetPixel(frame.Width / 2, Math.Min(1, frame.Height - 1));
            SKColor middle = frame.GetPixel(frame.Width / 2, frame.Height / 2);
            SKColor bottom = frame.GetPixel(frame.Width / 2, Math.Max(0, frame.Height - 2));
            AppendRenderLog($"Frame pixels top={top} middle={middle} bottom={bottom}");

            using SKImage image = SKImage.FromBitmap(frame);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream output = File.Create(RenderFramePath);
            data.SaveTo(output);
            AppendRenderLog($"Frame PNG={RenderFramePath} bytes={data.Size}");
        }
        catch (Exception ex)
        {
            AppendRenderLog($"Frame diagnostic failed: {ex}");
        }
    }

    private static void AppendRenderLog(string message)
    {
        try
        {
            File.AppendAllText(RenderLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void UpdateUi()
    {
        if (PageText is null || ZoomText is null)
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
        Canvas.Source = null;
        base.OnClosing(e);
    }
}
