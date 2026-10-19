using System.Globalization;
using System.Runtime.InteropServices;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using AegiNext.Rendering.Fonts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace AegiNext.Desktop.Controls;

/// <summary>使用工程排版及渲染的富文本输入控件；输入、选区及 IME 共用字素几何。</summary>
public sealed class RichSubtitleEditor : Control, IDisposable
{
    private readonly RichSubtitleInputMethodClient inputMethod;
    private readonly Cursor textCursor = new(StandardCursorType.Ibeam);
    private ProjectSceneRenderer? renderer;
    private SystemFontCatalog? fontCatalog;
    private string? directory;
    private ProjectDocument? document;
    private SubtitleLine? line;
    private SubtitleTextLayout? layout;
    private WriteableBitmap? bitmap;
    private Rect imageSource;
    private SKMatrix localToWorld = SKMatrix.Identity;
    private bool dragging;
    private IPointer? capturedPointer;
    private int selectionStart;
    private int selectionEnd;
    private string preedit = string.Empty;
    private string renderedPreedit = string.Empty;
    private int renderedSelectionStart;
    private int renderedSelectionEnd;
    private int preeditCaret;
    private SubtitlePreeditProjection? preeditProjection;
    private MediaTime? renderedTime;
    private SubtitlePreviewMode previewMode;
    private bool disposed;

    /// <summary>建立原生输入法接口及本地输入事件。</summary>
    public RichSubtitleEditor()
    {
        Focusable = true;
        Cursor = textCursor;
        ClipToBounds = true;
        MinHeight = 120;
        inputMethod = new(this);
        TextInputMethodClientRequested += (_, e) => e.Client = inputMethod;
    }

    public event EventHandler<SubtitleTextEditEventArgs>? TextEditRequested;
    public event EventHandler? SelectionChanged;
    public event EventHandler? RestoreRequested;
    public int SelectionStart => selectionStart;
    public int SelectionEnd => selectionEnd;
    public string Text => line?.Text ?? string.Empty;
    /// <summary>没有输入法组合文本时，Escape 是否恢复该控件的业务草稿。</summary>
    public bool RestoreOnEscape { get; set; } = true;
    internal string Preedit => preedit;
    internal bool IsSelectionGestureActive => capturedPointer is not null;
    internal string? RenderDiagnostic { get; private set; }
    internal Rect CaretRectangle => layout is null ? new(12, 12, 1, 24) : Map(layout.GetCaretBounds(preeditProjection?.Caret ?? selectionEnd));

    /// <summary>同步不可变内容及工程资源，保持同一行的选区；不发出业务编辑。</summary>
    public void SetContent(ProjectDocument source, SubtitleLine? value, string projectDirectory,
        MediaTime? localTime = null, SubtitlePreviewMode previewMode = SubtitlePreviewMode.TIMED, SystemFontCatalog? fonts = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var previousId = line?.Id;
        if (!Enum.IsDefined(previewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(previewMode));
        }
        var previewTime = previewMode == SubtitlePreviewMode.TIMED ? localTime : null;
        var sameContent = ReferenceEquals(document, source) && line == value && directory == projectDirectory &&
            ReferenceEquals(fontCatalog, fonts) && this.previewMode == previewMode && renderedTime == previewTime && renderedPreedit == preedit &&
            (preedit.Length == 0 || renderedSelectionStart == selectionStart && renderedSelectionEnd == selectionEnd);
        this.previewMode = previewMode;
        document = source;
        line = value;
        if (previousId != value?.Id)
        {
            selectionStart = selectionEnd = 0;
            preedit = string.Empty;
            CancelSelectionGesture();
        }
        else
        {
            selectionStart = Boundary(Math.Min(selectionStart, Text.Length));
            selectionEnd = Boundary(Math.Min(selectionEnd, Text.Length));
        }
        if (renderer is null || directory != projectDirectory || !ReferenceEquals(fontCatalog, fonts))
        {
            renderer?.Dispose();
            directory = projectDirectory;
            fontCatalog = fonts;
            renderer = new(new DirectoryProjectAssetResolver(projectDirectory), fonts);
        }
        if (!sameContent)
        {
            Rebuild(previewTime);
        }
        inputMethod.NotifyContent();
    }

    /// <summary>设置字素安全选区，保持选区用于工具栏样式应用。</summary>
    public void SetSelection(int start, int end)
    {
        selectionStart = Boundary(Math.Clamp(start, 0, Text.Length));
        selectionEnd = Boundary(Math.Clamp(end, 0, Text.Length));
        InvalidateVisual();
        inputMethod.NotifySelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetPreedit(string? text, int? cursor)
    {
        preedit = text ?? string.Empty;
        preeditCaret = Math.Clamp(cursor ?? preedit.Length, 0, preedit.Length);
        Rebuild(renderedTime);
        inputMethod.NotifySelection();
    }

    private void Rebuild(MediaTime? localTime)
    {
        RenderDiagnostic = null;
        bitmap?.Dispose();
        bitmap = null;
        layout = null;
        if (line is null || document is null || renderer is null)
        {
            InvalidateVisual();
            return;
        }
        try
        {
            RebuildText(localTime);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        {
            RenderDiagnostic = error.Message;
            layout = null;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void RebuildText(MediaTime? localTime)
    {
        preeditProjection = null;
        renderedPreedit = preedit;
        renderedSelectionStart = selectionStart;
        renderedSelectionEnd = selectionEnd;
        var viewLine = line!;
        if (preedit.Length > 0)
        {
            preeditProjection = SubtitlePreeditProjection.Create(viewLine, selectionStart, selectionEnd, preedit, preeditCaret);
            viewLine = preeditProjection.Line;
        }
        renderedTime = localTime;
        layout = renderer!.MeasureSubtitleTextLayout(document!, viewLine);
        localToWorld = SKMatrix.Identity;
        var bounds = layout.Bounds;
        var padding = PreviewPadding(viewLine.Style);
        var clipIndex = 0;
        var rangeIndex = 0;
        foreach (var run in layout.Runs.OrderBy(value => value.Utf16Start))
        {
            padding = Math.Max(padding, PreviewPadding(run.Style));
            padding = Math.Max(padding, PreviewPadding(KaraokeVisualStyleResolver.ResolveActive(run.Style, viewLine.KaraokeStyle, null)));
            while (clipIndex < viewLine.Karaoke.Length &&
                viewLine.Karaoke[clipIndex].Utf16Start + viewLine.Karaoke[clipIndex].Utf16Length <= run.Utf16Start)
            {
                clipIndex++;
            }
            for (var index = clipIndex; index < viewLine.Karaoke.Length &&
                viewLine.Karaoke[index].Utf16Start < run.Utf16Start + run.Utf16Length; index++)
            {
                var clip = viewLine.Karaoke[index];
                padding = Math.Max(padding, PreviewPadding(KaraokeVisualStyleResolver.ResolveActive(
                    run.Style, viewLine.KaraokeStyle, clip)));
                padding = Math.Max(padding, PreviewPadding(KaraokeVisualStyleResolver.ResolveInactive(run.Style, clip)));
            }
            while (rangeIndex < viewLine.KaraokeStyleSpans.Length &&
                viewLine.KaraokeStyleSpans[rangeIndex].Utf16Start + viewLine.KaraokeStyleSpans[rangeIndex].Utf16Length <= run.Utf16Start)
            {
                rangeIndex++;
            }
            for (var index = rangeIndex; index < viewLine.KaraokeStyleSpans.Length &&
                viewLine.KaraokeStyleSpans[index].Utf16Start < run.Utf16Start + run.Utf16Length; index++)
            {
                var range = viewLine.KaraokeStyleSpans[index];
                padding = Math.Max(padding, PreviewPadding(KaraokeVisualStyleResolver.ResolveActive(
                    run.Style, viewLine.KaraokeStyle, null, range.ActiveStyle)));
                padding = Math.Max(padding, PreviewPadding(KaraokeVisualStyleResolver.ResolveInactive(run.Style, null, range.InactiveStyle)));
            }
        }
        var left = bounds.Left - padding;
        var top = bounds.Top - padding;
        var right = Math.Max(left + 1, bounds.Right + padding);
        var bottom = Math.Max(top + 24, bounds.Bottom + padding);
        imageSource = new(left, top, Math.Max(1, right - left), Math.Max(24, bottom - top));
        using var surface = renderer.RenderSubtitlePreview(document!, viewLine, localTime ?? MediaTime.Zero,
            new((float)left, (float)top, (float)right, (float)bottom), previewMode);
        var pixels = surface.CopySrgbBgra();
        var width = surface.Info.Width;
        var height = surface.Info.Height;
        bitmap = new(new(width, height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = bitmap.Lock())
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(pixels, row * width * 4, locked.Address + row * locked.RowBytes, width * 4);
            }
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static double PreviewPadding(SubtitleStyle style) => Math.Max(16,
        Math.Max(style.FillBlur * 4, style.StrokeWidth + style.StrokeBlur * 4) + style.ShadowBlur * 4 +
        Math.Max(Math.Abs(style.ShadowOffset.X), Math.Abs(style.ShadowOffset.Y)) + 1);

    private double Scale => Math.Min(1, Math.Max(0.05, (Bounds.Width - 24) / Math.Max(1, imageSource.Width)));
    private Rect Destination => new(12, 12, imageSource.Width * Scale, imageSource.Height * Scale);

    private Rect Map(SKRect rectangle)
    {
        var world = localToWorld.MapRect(rectangle);
        return new(12 + (world.Left - imageSource.X) * Scale, 12 + (world.Top - imageSource.Y) * Scale,
            Math.Max(1, world.Width * Scale), Math.Max(1, world.Height * Scale));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(availableSize.Width, Math.Max(320, imageSource.Width + 24)),
        Math.Max(120, imageSource.Height * Math.Min(1, Math.Max(0.05, (availableSize.Width - 24) / Math.Max(1, imageSource.Width))) + 24));

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (bitmap is not null)
        {
            context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), Destination);
        }
        if (layout is not null && preeditProjection is null && selectionStart != selectionEnd)
        {
            var brush = new SolidColorBrush(Color.FromArgb(90, 70, 130, 230));
            foreach (var rect in layout.GetSelectionRects(Math.Min(selectionStart, selectionEnd), Math.Abs(selectionEnd - selectionStart)))
            {
                context.DrawRectangle(brush, null, Map(rect));
            }
        }
        if (IsFocused)
        {
            var caret = CaretRectangle;
            if (preedit.Length > 0 && layout is not null)
            {
                caret = Map(layout.GetCaretBounds(preeditProjection!.Caret));
                foreach (var rect in layout.GetSelectionRects(preeditProjection!.Start, preeditProjection.Length))
                {
                    var mapped = Map(rect);
                    context.DrawLine(new Pen(Brushes.DodgerBlue), mapped.BottomLeft, mapped.BottomRight);
                }
            }
            context.DrawLine(new Pen(Brushes.DodgerBlue, 1.5), caret.TopLeft, caret.BottomLeft);
        }
    }

    private int Hit(Point point)
    {
        if (layout is null || !localToWorld.TryInvert(out var inverse))
        {
            return 0;
        }
        var world = new SKPoint((float)((point.X - 12) / Scale + imageSource.X), (float)((point.Y - 12) / Scale + imageSource.Y));
        return Boundary(Math.Clamp(layout.HitTest(inverse.MapPoint(world)).Utf16Offset, 0, Text.Length));
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        Focus();
        var offset = Hit(e.GetPosition(this));
        if (e.ClickCount >= 3)
        {
            SetSelection(0, Text.Length);
        }
        else if (e.ClickCount == 2)
        {
            var start = offset;
            var end = offset;
            while (start > 0 && !char.IsWhiteSpace(Text[start - 1]))
            {
                start--;
            }
            while (end < Text.Length && !char.IsWhiteSpace(Text[end]))
            {
                end++;
            }
            SetSelection(start, end);
        }
        else
        {
            SetSelection((e.KeyModifiers & KeyModifiers.Shift) != 0 ? selectionStart : offset, offset);
        }
        dragging = e.ClickCount == 1;
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging)
        {
            SetSelection(selectionStart, Hit(e.GetPosition(this)));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        CancelSelectionGesture();
        base.OnPointerReleased(e);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        dragging = false;
        capturedPointer = null;
        base.OnPointerCaptureLost(e);
    }

    private void CancelSelectionGesture()
    {
        dragging = false;
        var pointer = capturedPointer;
        capturedPointer = null;
        pointer?.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text))
        {
            preedit = string.Empty;
            ReplaceSelection(e.Text);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var primary = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (primary && e.Key == Key.A)
        {
            SetSelection(0, Text.Length);
        }
        else if (primary && e.Key is Key.C or Key.X or Key.V)
        {
            _ = ClipboardAsync(e.Key);
        }
        else if (e.Key is Key.Left or Key.Right)
        {
            var boundaries = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToArray();
            var next = e.Key == Key.Left ? boundaries.LastOrDefault(value => value < selectionEnd) :
                boundaries.FirstOrDefault(value => value > selectionEnd, Text.Length);
            if ((e.KeyModifiers & KeyModifiers.Shift) == 0 && selectionStart != selectionEnd)
            {
                next = e.Key == Key.Left ? Math.Min(selectionStart, selectionEnd) : Math.Max(selectionStart, selectionEnd);
            }
            SetSelection((e.KeyModifiers & KeyModifiers.Shift) != 0 ? selectionStart : next, next);
        }
        else if (e.Key is Key.Up or Key.Down && layout is not null)
        {
            var caret = CaretRectangle;
            var next = Hit(new(caret.X, e.Key == Key.Up ? caret.Y - caret.Height / 2 : caret.Bottom + caret.Height / 2));
            SetSelection((e.KeyModifiers & KeyModifiers.Shift) != 0 ? selectionStart : next, next);
        }
        else if (e.Key is Key.Home or Key.End)
        {
            var next = e.Key == Key.Home ? 0 : Text.Length;
            SetSelection((e.KeyModifiers & KeyModifiers.Shift) != 0 ? selectionStart : next, next);
        }
        else if (e.Key is Key.Back or Key.Delete)
        {
            if (selectionStart == selectionEnd)
            {
                var boundaries = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToArray();
                SetSelection(selectionEnd, e.Key == Key.Back ? boundaries.LastOrDefault(value => value < selectionEnd) :
                    boundaries.FirstOrDefault(value => value > selectionEnd, Text.Length));
            }
            ReplaceSelection(string.Empty);
        }
        else if (e.Key == Key.Enter)
        {
            ReplaceSelection("\n");
        }
        else if (e.Key == Key.Escape)
        {
            if (IsSelectionGestureActive)
            {
                CancelSelectionGesture();
            }
            else if (preedit.Length > 0)
            {
                SetPreedit(null, null);
            }
            else if (RestoreOnEscape)
            {
                RestoreRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                return;
            }
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    internal void ReplaceSelection(string replacement)
    {
        var start = Math.Min(selectionStart, selectionEnd);
        var length = Math.Abs(selectionEnd - selectionStart);
        TextEditRequested?.Invoke(this, new(start, length, replacement));
        SetSelection(Math.Min(Text.Length, start + replacement.Length), Math.Min(Text.Length, start + replacement.Length));
    }

    internal async Task ClipboardAsync(Key key)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }
        var id = line?.Id;
        var content = Text;
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        if (key is Key.C or Key.X)
        {
            await clipboard.SetTextAsync(Text[start..end]);
            if (key == Key.X && line?.Id == id && Text == content && Math.Min(selectionStart, selectionEnd) == start && Math.Max(selectionStart, selectionEnd) == end)
            {
                ReplaceSelection(string.Empty);
            }
        }
        else if (await clipboard.TryGetTextAsync() is { } text && line?.Id == id && Text == content &&
            Math.Min(selectionStart, selectionEnd) == start && Math.Max(selectionStart, selectionEnd) == end)
        {
            ReplaceSelection(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        }
    }

    private int Boundary(int offset) => StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).LastOrDefault(value => value <= offset);

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        CancelSelectionGesture();
        bitmap?.Dispose();
        textCursor.Dispose();
        bitmap = null;
        renderer?.Dispose();
        renderer = null;
    }
}
