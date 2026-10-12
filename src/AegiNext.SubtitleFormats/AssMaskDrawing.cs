using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static partial class AssMaskDrawing
{
    internal static VectorClipMask Parse(string drawing, bool inverted, double scaleX, double scaleY)
    {
        var matches = TokenPattern().Matches(drawing);
        var tokens = matches.Select(match => match.Value).ToArray();
        var stripped = TokenPattern().Replace(drawing, string.Empty);
        if (!string.IsNullOrWhiteSpace(stripped) || tokens.Length > 100000)
        {
            throw new InvalidDataException("ASS 蒙版绘图包含非法命令或超过预算。");
        }
        var contours = ImmutableArray.CreateBuilder<MaskContour>();
        var nodes = new List<MaskNode>();
        var cursor = 0;
        var nodeCount = 0;
        var previousSourcePoint = default(ScenePoint);
        while (cursor < tokens.Length)
        {
            var command = tokens[cursor++];
            var points = new List<ScenePoint>();
            while (cursor < tokens.Length && !char.IsAsciiLetter(tokens[cursor][0]))
            {
                if (cursor + 1 >= tokens.Length || char.IsAsciiLetter(tokens[cursor + 1][0]))
                {
                    throw new InvalidDataException("ASS 蒙版绘图坐标必须成对。");
                }
                points.Add(new(AssFormatValues.Number(tokens[cursor++]) * scaleX, AssFormatValues.Number(tokens[cursor++]) * scaleY));
            }
            switch (command)
            {
                case "m":
                    if (points.Count == 0)
                    {
                        throw new InvalidDataException("ASS m 命令缺少坐标。");
                    }
                    FlushContour();
                    nodes.Add(new() { Position = points[^1] });
                    previousSourcePoint = points[^1];
                    break;
                case "n":
                    RequireStart();
                    if (points.Count == 0)
                    {
                        throw new InvalidDataException("ASS n 命令缺少坐标。");
                    }
                    if (nodes.Count == 1)
                    {
                        nodes[0] = nodes[0] with { Position = points[^1] };
                    }
                    previousSourcePoint = points[^1];
                    break;
                case "l":
                    RequireStart();
                    if (points.Count == 0)
                    {
                        throw new InvalidDataException("ASS l 命令缺少坐标。");
                    }
                    foreach (var point in points)
                    {
                        nodes.Add(new() { Position = point });
                    }
                    previousSourcePoint = points[^1];
                    break;
                case "b":
                    RequireStart();
                    if (points.Count == 0 || points.Count % 3 != 0)
                    {
                        throw new InvalidDataException("ASS b 命令必须有三点一组的三次贝塞尔坐标。");
                    }
                    for (var index = 0; index < points.Count; index += 3)
                    {
                        AddCubic(points[index], points[index + 1], points[index + 2]);
                    }
                    previousSourcePoint = points[^1];
                    break;
                case "s":
                    RequireStart();
                    if (points.Count < 3)
                    {
                        throw new InvalidDataException("ASS s 命令至少需要三个控制点。");
                    }
                    var spline = new List<ScenePoint> { previousSourcePoint };
                    spline.AddRange(points);
                    while (cursor < tokens.Length && tokens[cursor] == "p")
                    {
                        cursor++;
                        while (cursor + 1 < tokens.Length && !char.IsAsciiLetter(tokens[cursor][0]))
                        {
                            spline.Add(new(AssFormatValues.Number(tokens[cursor++]) * scaleX, AssFormatValues.Number(tokens[cursor++]) * scaleY));
                        }
                    }
                    if (cursor < tokens.Length && tokens[cursor] == "c")
                    {
                        cursor++;
                        spline.AddRange(spline.Take(3).ToArray());
                    }
                    previousSourcePoint = spline[^1];
                    for (var index = 0; index + 3 < spline.Count; index++)
                    {
                        var first = Weighted(spline[index], spline[index + 1], spline[index + 2], 1, 4, 1, 6);
                        if (nodes.Count == 1 && index == 0)
                        {
                            nodes[0] = nodes[0] with { Position = first };
                        }
                        AddCubic(Weighted(spline[index + 1], spline[index + 2], default, 2, 1, 0, 3),
                            Weighted(spline[index + 1], spline[index + 2], default, 1, 2, 0, 3),
                            Weighted(spline[index + 1], spline[index + 2], spline[index + 3], 1, 4, 1, 6));
                    }
                    break;
                default:
                    throw new InvalidDataException($"ASS 蒙版绘图命令 {command} 没有合法的前置曲线。");
            }
            if (nodeCount + nodes.Count > 10000)
            {
                throw new InvalidDataException("ASS 蒙版总节点数超过 10,000 点预算。");
            }
        }
        FlushContour();
        if (contours.Count == 0)
        {
            throw new InvalidDataException("ASS 蒙版绘图不能为空。");
        }
        var allNodes = contours.SelectMany(contour => contour.Nodes).ToArray();
        return new()
        {
            Inverted = inverted,
            Contours = contours.ToImmutable(),
            Transform = new() { Pivot = new((allNodes.Min(node => node.Position.X) + allNodes.Max(node => node.Position.X)) / 2,
                (allNodes.Min(node => node.Position.Y) + allNodes.Max(node => node.Position.Y)) / 2) }
        };

        void RequireStart()
        {
            if (nodes.Count == 0)
            {
                throw new InvalidDataException("ASS 蒙版绘图必须以 m 命令开始。");
            }
        }

        void AddCubic(ScenePoint first, ScenePoint second, ScenePoint end)
        {
            var previous = nodes[^1];
            nodes[^1] = previous with { OutHandle = new(first.X - previous.Position.X, first.Y - previous.Position.Y) };
            nodes.Add(new() { Position = end, InHandle = new(second.X - end.X, second.Y - end.Y) });
        }

        void FlushContour()
        {
            if (nodes.Count == 0)
            {
                return;
            }
            if (nodes.Count > 1 && nodes[^1].Position == nodes[0].Position && nodes[^1].OutHandle == default)
            {
                nodes[0] = nodes[0] with { InHandle = nodes[^1].InHandle };
                nodes.RemoveAt(nodes.Count - 1);
            }
            nodeCount += nodes.Count;
            contours.Add(new() { Nodes = nodes.ToImmutableArray() });
            nodes.Clear();
        }
    }

    private static ScenePoint Weighted(ScenePoint first, ScenePoint second, ScenePoint third, double a, double b, double c, double divisor)
    {
        return new((first.X * a + second.X * b + third.X * c) / divisor, (first.Y * a + second.Y * b + third.Y * c) / divisor);
    }

    [GeneratedRegex(@"[mlbnspc]|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
