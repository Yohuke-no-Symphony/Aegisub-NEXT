using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static partial class AssDrawingParser
{
    private const int MAX_POINT_COUNT = 10000;
    private const int MAX_NUMBER_COUNT = 100000;

    internal static AssDrawingGeometry Parse(string drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if (NonFinitePattern().IsMatch(drawing))
        {
            throw new InvalidDataException("ASS drawing coordinates must be finite.");
        }
        var tokens = Tokenize(drawing);
        var contours = ImmutableArray.CreateBuilder<MaskContour>();
        var nodes = new List<MaskNode>();
        var minimum = new ScenePoint(double.PositiveInfinity, double.PositiveInfinity);
        var maximum = new ScenePoint(double.NegativeInfinity, double.NegativeInfinity);
        var pen = default(ScenePoint);
        var nodeCount = 0;
        for (var index = 0; index < tokens.Count;)
        {
            var token = tokens[index];
            switch (token.Command)
            {
                case AssDrawingCommand.MOVE:
                    Flush();
                    pen = token.Position;
                    Include(pen);
                    index++;
                    break;
                case AssDrawingCommand.MOVE_WITHOUT_CLOSING:
                    pen = token.Position;
                    Include(pen);
                    index++;
                    break;
                case AssDrawingCommand.LINE:
                    Include(token.Position);
                    if (nodes.Count == 0)
                    {
                        AddNode(new() { Position = pen });
                    }
                    AddNode(new() { Position = token.Position });
                    index++;
                    break;
                case AssDrawingCommand.CUBIC:
                case AssDrawingCommand.SPLINE:
                case AssDrawingCommand.EXTEND_SPLINE:
                    var firstIndex = token.Command == AssDrawingCommand.EXTEND_SPLINE ? index - 3 : index - 1;
                    var first = tokens[firstIndex];
                    var second = tokens[firstIndex + 1];
                    var third = tokens[firstIndex + 2];
                    var fourth = tokens[firstIndex + 3];
                    Include(first.Position);
                    Include(second.Position);
                    Include(third.Position);
                    Include(fourth.Position);
                    var points = token.Command == AssDrawingCommand.CUBIC
                        ? new[] { first.Position, second.Position, third.Position, fourth.Position }
                        : SplinePoints(first, second, third, fourth);
                    if (nodes.Count == 0)
                    {
                        AddNode(new() { Position = points[0] });
                    }
                    var previous = nodes[^1];
                    nodes[^1] = previous with
                    {
                        OutHandle = new(points[1].X - previous.Position.X, points[1].Y - previous.Position.Y)
                    };
                    AddNode(new()
                    {
                        Position = points[3],
                        InHandle = new(points[2].X - points[3].X, points[2].Y - points[3].Y)
                    });
                    index += token.Command == AssDrawingCommand.EXTEND_SPLINE ? 1 : 3;
                    break;
            }
        }
        Flush();
        return new(contours.ToImmutable(), tokens.Count == 0 ? default : minimum,
            tokens.Count == 0 ? default : maximum, tokens.Count);

        void Include(ScenePoint point)
        {
            minimum = new(Math.Min(minimum.X, point.X), Math.Min(minimum.Y, point.Y));
            maximum = new(Math.Max(maximum.X, point.X), Math.Max(maximum.Y, point.Y));
        }

        void AddNode(MaskNode node)
        {
            if (++nodeCount > MAX_POINT_COUNT)
            {
                throw new InvalidDataException("ASS drawing exceeds the 10,000-point geometry budget.");
            }
            nodes.Add(node);
        }

        void Flush()
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
            contours.Add(new() { Nodes = nodes.ToImmutableArray() });
            nodes.Clear();
        }
    }

    private static List<AssDrawingToken> Tokenize(string drawing)
    {
        var tokens = new List<AssDrawingToken>();
        var cursor = 0;
        var numberCount = 0;
        int? splineStart = null;
        var moveSeen = false;
        while (cursor < drawing.Length)
        {
            var command = drawing[cursor++];
            switch (command)
            {
                case 'm':
                    moveSeen = true;
                    if (tokens.Count == 0 && TryPoint(AssDrawingCommand.MOVE, out var move))
                    {
                        Add(move);
                    }
                    AddMany(AssDrawingCommand.MOVE, 1);
                    break;
                case 'n':
                    if (tokens.Count == 0)
                    {
                        if (!TryPoint(AssDrawingCommand.MOVE_WITHOUT_CLOSING, out var moveWithoutClosing))
                        {
                            break;
                        }
                        if (!moveSeen)
                        {
                            return [];
                        }
                        Add(moveWithoutClosing);
                    }
                    AddMany(AssDrawingCommand.MOVE_WITHOUT_CLOSING, 1);
                    break;
                case 'l':
                    if (tokens.Count > 0)
                    {
                        AddMany(AssDrawingCommand.LINE, 1);
                    }
                    break;
                case 'b':
                    if (tokens.Count > 0)
                    {
                        AddMany(AssDrawingCommand.CUBIC, 3);
                    }
                    break;
                case 's':
                    if (tokens.Count == 0)
                    {
                        break;
                    }
                    splineStart = tokens.Count - 1;
                    if (!AddBatch(AssDrawingCommand.SPLINE, 3))
                    {
                        splineStart = null;
                        break;
                    }
                    AddMany(AssDrawingCommand.EXTEND_SPLINE, 1);
                    break;
                case 'p':
                    if (tokens.Count >= 3)
                    {
                        AddMany(AssDrawingCommand.EXTEND_SPLINE, 1);
                    }
                    break;
                case 'c':
                    if (splineStart is { } start)
                    {
                        var closing = tokens.Skip(start).Take(3).ToArray();
                        foreach (var point in closing)
                        {
                            Add(point with { Command = AssDrawingCommand.EXTEND_SPLINE });
                        }
                        splineStart = null;
                    }
                    break;
            }
        }
        return tokens;

        void Add(AssDrawingToken token)
        {
            if (tokens.Count >= MAX_POINT_COUNT)
            {
                throw new InvalidDataException("ASS drawing exceeds the 10,000-point token budget.");
            }
            tokens.Add(token);
        }

        void AddMany(AssDrawingCommand command, int count)
        {
            while (AddBatch(command, count))
            {
            }
        }

        bool AddBatch(AssDrawingCommand command, int count)
        {
            var batch = new AssDrawingToken[count];
            for (var index = 0; index < count; index++)
            {
                if (!TryPoint(command, out batch[index]))
                {
                    return false;
                }
            }
            foreach (var token in batch)
            {
                Add(token);
            }
            return true;
        }

        bool TryPoint(AssDrawingCommand command, out AssDrawingToken token)
        {
            token = default;
            if (!TryNumber(out var x) || !TryNumber(out var y))
            {
                return false;
            }
            token = new(command, Quantize(x), Quantize(y));
            return true;
        }

        bool TryNumber(out double value)
        {
            while (cursor < drawing.Length && char.IsWhiteSpace(drawing[cursor]))
            {
                cursor++;
            }
            var match = NumberPattern().Match(drawing, cursor);
            if (!match.Success)
            {
                value = 0;
                return false;
            }
            cursor += match.Length;
            if (++numberCount > MAX_NUMBER_COUNT || !double.TryParse(match.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
            {
                throw new InvalidDataException("ASS drawing contains non-finite coordinates or exceeds the numeric budget.");
            }
            return true;
        }
    }

    private static int Quantize(double value)
    {
        var quantized = Math.Round(value * 64, MidpointRounding.ToEven);
        if (!double.IsFinite(quantized) || quantized < int.MinValue || quantized > int.MaxValue)
        {
            throw new InvalidDataException("ASS drawing coordinates exceed the signed 26.6-bit range.");
        }
        return (int)quantized;
    }

    private static ScenePoint[] SplinePoints(AssDrawingToken first, AssDrawingToken second,
        AssDrawingToken third, AssDrawingToken fourth)
    {
        var x01 = ((long)second.X - first.X) / 3;
        var y01 = ((long)second.Y - first.Y) / 3;
        var x12 = ((long)third.X - second.X) / 3;
        var y12 = ((long)third.Y - second.Y) / 3;
        var x23 = ((long)fourth.X - third.X) / 3;
        var y23 = ((long)fourth.Y - third.Y) / 3;
        return
        [
            new((second.X + ((x12 - x01) >> 1)) / 64d, (second.Y + ((y12 - y01) >> 1)) / 64d),
            new((second.X + x12) / 64d, (second.Y + y12) / 64d),
            new((third.X - x12) / 64d, (third.Y - y12) / 64d),
            new((third.X + ((x23 - x12) >> 1)) / 64d, (third.Y + ((y23 - y12) >> 1)) / 64d)
        ];
    }

    [GeneratedRegex(@"\G[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"(?<![A-Za-z])(?:NaN|[-+]?Infinity|[-+]?inf)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonFinitePattern();
}
