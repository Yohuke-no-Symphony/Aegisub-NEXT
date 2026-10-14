using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证蒙版吸附的真实指针输入、坐标投影与手势生命周期。</summary>
public sealed class ClipMaskSnappingUiTests
{
    /// <summary>新绘制节点可对齐字幕四边及横竖中线，闭合后只发布最终轮廓。</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DrawnNodeSnapsToEachSubtitleEdgeAndCenterLine(int guide)
    {
        var document = Document();
        var geometry = Geometry(document);
        WithCanvas(document, CanvasEditMode.MASK_DRAW_VECTOR, (canvas, window) =>
        {
            var bounds = geometry.LocalBounds;
            var vertical = guide < 3;
            var local = vertical
                ? new Point(guide switch { 0 => bounds.Left, 1 => bounds.MidX, _ => bounds.Right }, bounds.Top + bounds.Height * 0.25)
                : new Point(bounds.Left + bounds.Width * 0.25, guide switch { 3 => bounds.Top, 4 => bounds.MidY, _ => bounds.Bottom });
            var target = local * SubtitleMatrix(canvas, document, geometry);
            var raw = target + (vertical ? new Vector(5, 0) : new Vector(0, 5));
            var commits = new List<CanvasMaskEditEventArgs>();
            canvas.MaskEdited += (_, e) => commits.Add(e);

            window.MouseDown(raw, MouseButton.Left);
            Assert.True(canvas.MaskSnapFeedback.HasSnap);
            Assert.Equal(vertical ? local.X : null, canvas.MaskSnapFeedback.XGuide);
            Assert.Equal(vertical ? null : local.Y, canvas.MaskSnapFeedback.YGuide);
            window.MouseUp(raw, MouseButton.Left);
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
            Assert.Empty(commits);
            CompleteTriangle(window, canvas, document);

            var mask = Assert.IsType<VectorClipMask>(Assert.Single(commits).Mask);
            AssertPoint(target, MaskPoint(canvas, document, mask, mask.Contours[0].Nodes[0].Position));
            Assert.False(canvas.HasOpenMaskContour);
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
        });
    }

    /// <summary>矩形首尾点遵循开关状态，草稿只在释放时提交一次并可一次撤销。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleSnapsBothEndpointsOnlyWhenEnabledAndCommitsOneUndo(bool enabled)
    {
        var document = Document();
        var geometry = Geometry(document);
        var editor = new ProjectEditor(document);
        WithCanvas(document, CanvasEditMode.MASK_RECTANGLE, (canvas, window) =>
        {
            Assert.True(canvas.IsMaskSnapEnabled);
            canvas.IsMaskSnapEnabled = enabled;
            var matrix = SubtitleMatrix(canvas, document, geometry);
            var first = new Point(geometry.LocalBounds.Left, geometry.LocalBounds.Top) * matrix;
            var last = new Point(geometry.LocalBounds.Right, geometry.LocalBounds.Bottom) * matrix;
            var rawFirst = first + new Vector(4, 4);
            var rawLast = last - new Vector(5, 5);
            var commits = 0;
            canvas.MaskEdited += (_, e) =>
            {
                commits++;
                editor.SetClipMask(e.LayerId, e.Mask);
            };

            window.MouseDown(rawFirst, MouseButton.Left);
            window.MouseMove(rawLast);
            Assert.True(canvas.HasActiveDrag);
            Assert.Equal(enabled, canvas.MaskSnapFeedback.HasSnap);
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(rawLast, MouseButton.Left);

            var mask = Assert.IsType<RectangleClipMask>(editor.Snapshot.Layers[0].Mask);
            var inverse = Inverse(Fit(canvas, document));
            var expectedFirst = (enabled ? first : rawFirst) * inverse;
            var expectedLast = (enabled ? last : rawLast) * inverse;
            AssertPoint(expectedFirst, new(mask.TopLeft.X, mask.TopLeft.Y));
            AssertPoint(expectedLast, new(mask.BottomRight.X, mask.BottomRight.Y));
            AssertPoint(new((expectedFirst.X + expectedLast.X) / 2, (expectedFirst.Y + expectedLast.Y) / 2),
                new(mask.ReferenceFrame.Origin.X + mask.Placement.Pivot.X * mask.ReferenceFrame.Size.X,
                    mask.ReferenceFrame.Origin.Y + mask.Placement.Pivot.Y * mask.ReferenceFrame.Size.Y));
            Assert.Equal(1, commits);
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        });
    }

    /// <summary>真实画布缩放变化时，吸附阈值仍以 8 DIP 衡量。</summary>
    [AvaloniaTheory]
    [InlineData(520, 7)]
    [InlineData(520, 9)]
    [InlineData(1020, 7)]
    [InlineData(1020, 9)]
    public void PointerThresholdRemainsEightDipAtDifferentCanvasScales(double width, double distance)
    {
        var document = Document();
        var geometry = Geometry(document);
        WithCanvas(document, CanvasEditMode.MASK_DRAW_VECTOR, (canvas, window) =>
        {
            var target = new Point(geometry.LocalBounds.Left, geometry.LocalBounds.Top + geometry.LocalBounds.Height * 0.25) *
                SubtitleMatrix(canvas, document, geometry);
            var raw = target + new Vector(distance, 0);
            CanvasMaskEditEventArgs? edited = null;
            canvas.MaskEdited += (_, e) => edited = e;

            window.MouseDown(raw, MouseButton.Left);
            Assert.Equal(distance < 8, canvas.MaskSnapFeedback.HasSnap);
            window.MouseUp(raw, MouseButton.Left);
            CompleteTriangle(window, canvas, document);

            Assert.NotNull(edited);
            var mask = Assert.IsType<VectorClipMask>(edited.Mask);
            AssertPoint(distance < 8 ? target : raw, MaskPoint(canvas, document, mask, mask.Contours[0].Nodes[0].Position));
        }, width);
    }

    /// <summary>已有节点通过字幕和蒙版各自的变换对齐显示中线，并保持相邻节点、柄与枢轴。</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(32)]
    public void ExistingNodeSnapsThroughSubtitleAndIndependentMaskTransforms(double rotation)
    {
        var document = Document(rotation: rotation, scaleX: 1.3, scaleY: 0.75);
        var geometry = Geometry(document);
        var targetWorld = new Point(geometry.LocalBounds.MidX, geometry.LocalBounds.MidY) * LocalToWorld(geometry);
        var pivot = new ScenePoint(450, 350);
        var transform = new MaskTransform { Position = new(23, -13), Scale = new(1.2, 0.8), Rotation = -17 };
        var firstLocal = (targetWorld - new Vector(110, 70)) * Inverse(MaskToWorld(transform, pivot));
        var first = new MaskNode { Position = new(firstLocal.X, firstLocal.Y), InHandle = new(-12, 7), OutHandle = new(18, -9) };
        var original = new VectorClipMask
        {
            Transform = transform,
            Placement = new() { Pivot = pivot, Offset = pivot },
            Contours = [new() { Nodes = [first, new() { Position = new(firstLocal.X + 120, firstLocal.Y) }, new() { Position = new(firstLocal.X + 60, firstLocal.Y + 130) }] }]
        };
        document = WithMask(document, original);
        WithCanvas(document, CanvasEditMode.MASK_VECTOR, (canvas, window) =>
        {
            var start = MaskPoint(canvas, document, original, first.Position);
            var target = targetWorld * Fit(canvas, document);
            var raw = new Point(geometry.LocalBounds.MidX + 2, geometry.LocalBounds.MidY + 2) *
                SubtitleMatrix(canvas, document, geometry);
            CanvasMaskEditEventArgs? edited = null;
            canvas.MaskEdited += (_, e) => edited = e;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(raw);
            Assert.True(canvas.MaskSnapFeedback.HasSnap);
            window.MouseUp(raw, MouseButton.Left);

            Assert.NotNull(edited);
            var changed = Assert.IsType<VectorClipMask>(edited.Mask);
            var node = changed.Contours[0].Nodes[0];
            Assert.Equal(first.Id, edited.NodeId);
            AssertPoint(target, MaskPoint(canvas, document, changed, node.Position));
            Assert.Equal(first.InHandle, node.InHandle);
            Assert.Equal(first.OutHandle, node.OutHandle);
            Assert.Equal(original.Contours[0].Nodes.Skip(1), changed.Contours[0].Nodes.Skip(1));
            Assert.Equal(original.Transform, changed.Transform);
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
        });
    }

    /// <summary>矩形角点在自身旋转缩放下仍吸附到字幕中线，其他角与独立枢轴保持不变。</summary>
    [AvaloniaFact]
    public void ExistingRectangleCornerSnapsInItsOwnLocalCoordinates()
    {
        var document = Document();
        var geometry = Geometry(document);
        var targetWorld = new Point(geometry.LocalBounds.MidX, geometry.LocalBounds.MidY) * LocalToWorld(geometry);
        var pivot = new ScenePoint(430, 330);
        var transform = new MaskTransform { Position = new(17, -11), Scale = new(1.1, 0.85), Rotation = 21 };
        var targetLocal = targetWorld * Inverse(MaskToWorld(transform, pivot));
        var original = new RectangleClipMask
        {
            TopLeft = new(targetLocal.X - 160, targetLocal.Y - 130),
            BottomRight = new(targetLocal.X - 60, targetLocal.Y - 40),
            Transform = transform,
            Placement = new() { Pivot = pivot, Offset = pivot }
        };
        document = WithMask(document, original);
        WithCanvas(document, CanvasEditMode.MASK_RECTANGLE, (canvas, window) =>
        {
            var start = MaskPoint(canvas, document, original, original.BottomRight);
            var target = targetWorld * Fit(canvas, document);
            var raw = target + new Vector(4, 3);
            CanvasMaskEditEventArgs? edited = null;
            canvas.MaskEdited += (_, e) => edited = e;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(raw);
            window.MouseUp(raw, MouseButton.Left);

            Assert.NotNull(edited);
            var changed = Assert.IsType<RectangleClipMask>(edited.Mask);
            AssertPoint(target, MaskPoint(canvas, document, changed, changed.BottomRight));
            Assert.Equal(original.TopLeft, changed.TopLeft);
            Assert.Equal(original.Transform, changed.Transform);
        });
    }

    /// <summary>整体移动可吸附蒙版外框中点，同时保留矩形或矢量几何及独立变换的其他分量。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeMaskDragSnapsToProjectedSubtitleWorldBoundsForRotationOrConstantDepth(bool constantDepth)
    {
        var document = Document();
        var layer = document.Layers[0];
        document = document with
        {
            Layers = [layer with
            {
                Transform = layer.Transform with
                {
                    RotationX = constantDepth ? 0 : 35,
                    RotationY = constantDepth ? 0 : -25,
                    PerspectiveDistance = 500,
                    ProjectionTranslation = constantDepth ? new(0, 0, 150) : default
                }
            }]
        };
        var geometry = Geometry(document);
        Assert.True(geometry.Projection.M31 != 0 || geometry.Projection.M32 != 0 || geometry.Projection.M33 != 1);
        var bounds = geometry.WorldBounds;
        var targetWorld = new Point(bounds.MidX, bounds.MidY);
        var center = targetWorld - new Vector(140, 110);
        var original = ClipMaskPlacementGeometry.CreatePlacement(new RectangleClipMask
        {
            TopLeft = new(center.X - 30, center.Y - 16), BottomRight = new(center.X + 30, center.Y + 16)
        }, new(document.Width, document.Height));
        document = WithMask(document, original);
        WithCanvas(document, CanvasEditMode.MASK_RECTANGLE, (canvas, window) =>
        {
            var start = MaskPoint(canvas, document, original, new(center.X, center.Y));
            var target = targetWorld * Fit(canvas, document);
            var raw = target + new Vector(4, 3);
            CanvasMaskEditEventArgs? edited = null;
            canvas.MaskEdited += (_, args) => edited = args;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(raw);

            Assert.True(canvas.MaskSnapFeedback.HasSnap);
            Assert.Equal(targetWorld.X, canvas.MaskSnapFeedback.XGuide!.Value, 4);
            Assert.Equal(targetWorld.Y, canvas.MaskSnapFeedback.YGuide!.Value, 4);
            window.MouseUp(raw, MouseButton.Left);
            Assert.NotNull(edited);
            var movedCenter = MaskPoint(canvas, document, edited.Mask, new(center.X, center.Y));
            Assert.Equal(target.X, movedCenter.X, 4);
            Assert.Equal(target.Y, movedCenter.Y, 4);
            Assert.Equal(original.Placement, edited.Mask.Placement);
            Assert.Equal(original.ReferenceFrame, edited.Mask.ReferenceFrame);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingTheWholeMaskSnapsItsCenterWithoutChangingItsShape(bool vector)
    {
        var document = Document();
        var geometry = Geometry(document);
        var targetWorld = new Point(geometry.LocalBounds.MidX, geometry.LocalBounds.MidY) * LocalToWorld(geometry);
        var center = targetWorld - new Vector(140, 110);
        var transform = new MaskTransform { Position = new(17, 13), Scale = new(1.1, 0.9), Rotation = 23 };
        var placement = new LayerPosition { Pivot = new(center.X, center.Y), Offset = new(center.X, center.Y) };
        ClipMask original = vector ? new VectorClipMask
        {
            Transform = transform, Placement = placement,
            Contours = [new() { Nodes =
            [
                new() { Position = new(center.X - 30, center.Y - 16) },
                new() { Position = new(center.X + 30, center.Y - 16) },
                new() { Position = new(center.X + 30, center.Y + 16) },
                new() { Position = new(center.X - 30, center.Y + 16) }
            ] }]
        } : new RectangleClipMask
        {
            Transform = transform, Placement = placement,
            TopLeft = new(center.X - 30, center.Y - 16), BottomRight = new(center.X + 30, center.Y + 16)
        };
        document = WithMask(document, original);
        WithCanvas(document, vector ? CanvasEditMode.MASK_VECTOR : CanvasEditMode.MASK_RECTANGLE, (canvas, window) =>
        {
            var start = MaskPoint(canvas, document, original, new(center.X, center.Y));
            var target = targetWorld * Fit(canvas, document);
            var raw = target + new Vector(4, 3);
            CanvasMaskEditEventArgs? edited = null;
            canvas.MaskEdited += (_, e) => edited = e;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(raw);
            window.MouseUp(raw, MouseButton.Left);

            Assert.NotNull(edited);
            AssertPoint(target, MaskPoint(canvas, document, edited.Mask, new(center.X, center.Y)));
            Assert.Equal(original, edited.Mask with { Transform = original.Transform });
            Assert.Equal(original.Transform with { Position = edited.Mask.Transform.Position }, edited.Mask.Transform);
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
        });
    }

    /// <summary>Alt 及 Alt+Shift 编辑控制柄时保留原始指针位置，不将柄末端吸附到字幕。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BezierHandleModifiersDoNotSnapOrMoveTheNode(bool incoming)
    {
        var document = Document();
        var geometry = Geometry(document);
        var targetWorld = new Point(geometry.LocalBounds.MidX, geometry.LocalBounds.MidY) * LocalToWorld(geometry);
        var first = new MaskNode { Position = new(targetWorld.X - 100, targetWorld.Y - 70) };
        var original = new VectorClipMask
        {
            Contours = [new() { Nodes = [first, new() { Position = new(targetWorld.X + 90, targetWorld.Y - 70) }, new() { Position = new(targetWorld.X, targetWorld.Y + 100) }] }]
        };
        document = WithMask(document, original);
        WithCanvas(document, CanvasEditMode.MASK_VECTOR, (canvas, window) =>
        {
            var start = MaskPoint(canvas, document, original, first.Position);
            var raw = targetWorld * Fit(canvas, document) + new Vector(4, 3);
            var modifiers = RawInputModifiers.Alt | (incoming ? RawInputModifiers.Shift : RawInputModifiers.None);
            CanvasMaskEditEventArgs? edited = null;
            canvas.MaskEdited += (_, e) => edited = e;

            window.MouseDown(start, MouseButton.Left, modifiers);
            window.MouseMove(raw, modifiers);
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
            window.MouseUp(raw, MouseButton.Left, modifiers);

            Assert.NotNull(edited);
            var changed = Assert.IsType<VectorClipMask>(edited.Mask);
            var node = changed.Contours[0].Nodes[0];
            var handle = incoming ? node.InHandle : node.OutHandle;
            Assert.Equal(first.Position, node.Position);
            AssertPoint(raw, MaskPoint(canvas, document, changed, new(node.Position.X + handle.X, node.Position.Y + handle.Y)));
            Assert.Equal(default, incoming ? node.OutHandle : node.InHandle);
            Assert.Equal(3, changed.Contours[0].Nodes.Length);
        });
    }

    /// <summary>同一源快照的时间刷新不会在拖动中改变字幕吸附参照，释放后新手势使用新时间。</summary>
    [AvaloniaFact]
    public void SnappingReferenceTimeRemainsFrozenUntilTheMaskGestureEnds()
    {
        var document = Document();
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Tracks = [new(AnimationProperty.POSITION, [new(MediaTime.Zero, new ScenePoint(0, 0)), new(new(1), new ScenePoint(150, 110))])]
            }]
        };
        var initial = Geometry(document);
        var later = Geometry(document, new(1));
        WithCanvas(document, CanvasEditMode.MASK_RECTANGLE, (canvas, window) =>
        {
            var commits = new List<CanvasMaskEditEventArgs>();
            canvas.MaskEdited += (_, e) => commits.Add(e);
            var first = new Point(initial.LocalBounds.Left, initial.LocalBounds.Top) * SubtitleMatrix(canvas, document, initial);
            var last = new Point(initial.LocalBounds.Right, initial.LocalBounds.Bottom) * SubtitleMatrix(canvas, document, initial);

            window.MouseDown(first + new Vector(4, 4), MouseButton.Left);
            canvas.SetScene(document, document.Layers[0], new(1), AppContext.BaseDirectory);
            window.MouseMove(last - new Vector(4, 4));
            window.MouseUp(last - new Vector(4, 4), MouseButton.Left);

            var frozen = Assert.IsType<RectangleClipMask>(Assert.Single(commits).Mask);
            AssertPoint(first, MaskPoint(canvas, document, frozen, frozen.TopLeft));
            AssertPoint(last, MaskPoint(canvas, document, frozen, frozen.BottomRight));
            canvas.SetScene(document, document.Layers[0], new(1), AppContext.BaseDirectory);
            var laterFirst = new Point(later.LocalBounds.Left, later.LocalBounds.Top) * SubtitleMatrix(canvas, document, later);
            var laterLast = new Point(later.LocalBounds.Right, later.LocalBounds.Bottom) * SubtitleMatrix(canvas, document, later);
            window.MouseDown(laterFirst + new Vector(4, 4), MouseButton.Left);
            window.MouseMove(laterLast - new Vector(4, 4));
            window.MouseUp(laterLast - new Vector(4, 4), MouseButton.Left);

            Assert.Equal(2, commits.Count);
            var resumed = Assert.IsType<RectangleClipMask>(commits[1].Mask);
            AssertPoint(laterFirst, MaskPoint(canvas, document, resumed, resumed.TopLeft));
            AssertPoint(laterLast, MaskPoint(canvas, document, resumed, resumed.BottomRight));
        });
    }

    /// <summary>捕获丢失、取消和清除选择均取消吸附草稿并立即清除参考线反馈。</summary>
    [AvaloniaTheory]
    [InlineData("capture")]
    [InlineData("escape")]
    [InlineData("selection")]
    public void CancellingASnappedGestureClearsFeedbackWithoutPublishingItsDraft(string cancellation)
    {
        var document = Document();
        var geometry = Geometry(document);
        WithCanvas(document, CanvasEditMode.MASK_RECTANGLE, (canvas, window) =>
        {
            var first = new Point(geometry.LocalBounds.Left, geometry.LocalBounds.Top) * SubtitleMatrix(canvas, document, geometry);
            var last = new Point(geometry.LocalBounds.Right, geometry.LocalBounds.Bottom) * SubtitleMatrix(canvas, document, geometry);
            var commits = 0;
            var cancellations = 0;
            IPointer? pointer = null;
            canvas.MaskEdited += (_, _) => commits++;
            canvas.MaskGestureCancelled += (_, _) => cancellations++;
            canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);

            window.MouseDown(first + new Vector(4, 4), MouseButton.Left);
            window.MouseMove(last - new Vector(4, 4));
            Assert.True(canvas.MaskSnapFeedback.HasSnap);
            if (cancellation == "capture")
            {
                Assert.NotNull(pointer);
                pointer.Capture(null);
            }
            else if (cancellation == "escape")
            {
                UiTestActions.Press(window, Key.Escape);
            }
            else
            {
                canvas.SetScene(document, null, MediaTime.Zero, AppContext.BaseDirectory);
            }
            Assert.False(canvas.MaskSnapFeedback.HasSnap);
            window.MouseUp(last - new Vector(4, 4), MouseButton.Left);

            Assert.False(canvas.HasActiveDrag);
            Assert.Equal(0, commits);
            Assert.Equal(1, cancellations);
        });
    }

    private static ProjectDocument Document(double rotation = 0, double scaleX = 1, double scaleY = 1)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = "Mask ABC 123\nMask ABC 123",
            End = new(5),
            Style = new()
            {
                FontAssetId = font.Id,
                FontSize = 80,
                Scaling = new() { ReferenceWidth = 1000, ReferenceHeight = 700 },
                Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) },
                StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent
            }
        };
        return new()
        {
            Width = 1000,
            Height = 700,
            Assets = [font],
            Subtitles = [line],
            Layers = [new()
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                End = line.End,
                Transform = new(ScaleX: scaleX, ScaleY: scaleY, Rotation: rotation)
            }]
        };
    }

    private static ProjectDocument WithMask(ProjectDocument document, ClipMask mask) =>
        document with { Layers = [document.Layers[0] with { Mask = mask }] };

    private static ProjectLayerGeometry Geometry(ProjectDocument document, MediaTime time = default)
    {
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        return Assert.IsType<ProjectLayerGeometry>(renderer.GetLayerGeometry(document, time, document.Layers[0].Id));
    }

    private static void WithCanvas(ProjectDocument document, CanvasEditMode mode, Action<EffectCanvasControl, Window> action, double width = 900)
    {
        using var canvas = new EffectCanvasControl();
        canvas.EditMode = mode;
        canvas.SetScene(document, document.Layers[0], MediaTime.Zero, AppContext.BaseDirectory);
        var window = new Window { Width = width, Height = width * 0.7 + 30, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            action(canvas, window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void CompleteTriangle(Window window, EffectCanvasControl canvas, ProjectDocument document)
    {
        var first = new Point(100, 100) * Fit(canvas, document);
        var second = new Point(100, 600) * Fit(canvas, document);
        window.MouseDown(first, MouseButton.Left);
        window.MouseUp(first, MouseButton.Left);
        window.MouseDown(second, MouseButton.Left);
        window.MouseUp(second, MouseButton.Left);
        UiTestActions.Press(window, Key.Enter);
    }

    private static Matrix Fit(EffectCanvasControl canvas, ProjectDocument document)
    {
        var board = canvas.ProjectRectangle;
        return Matrix.CreateScale(board.Width / document.Width, board.Height / document.Height) * Matrix.CreateTranslation(board.X, board.Y);
    }

    private static Matrix LocalToWorld(ProjectLayerGeometry geometry)
    {
        var matrix = geometry.LocalToWorld;
        return new(matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TransX, matrix.TransY);
    }

    private static Matrix SubtitleMatrix(EffectCanvasControl canvas, ProjectDocument document, ProjectLayerGeometry geometry) =>
        LocalToWorld(geometry) * Fit(canvas, document);

    private static Matrix MaskToWorld(MaskTransform transform, ScenePoint pivot) =>
        Matrix.CreateTranslation(-pivot.X, -pivot.Y) * Matrix.CreateScale(transform.Scale.X, transform.Scale.Y) *
        Matrix.CreateRotation(transform.Rotation * Math.PI / 180) *
        Matrix.CreateTranslation(pivot.X + transform.Position.X, pivot.Y + transform.Position.Y);

    private static Point MaskPoint(EffectCanvasControl canvas, ProjectDocument document, ClipMask mask, ScenePoint point)
    {
        var projected = ClipMaskPlacementGeometry.CreateMatrix(mask, new(document.Width, document.Height)).Map(point);
        return new Point(projected.X, projected.Y) * Fit(canvas, document);
    }

    private static Matrix Inverse(Matrix matrix)
    {
        Assert.True(matrix.TryInvert(out var inverse));
        return inverse;
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
    }
}
