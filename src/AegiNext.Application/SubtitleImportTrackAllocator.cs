using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal sealed class SubtitleImportTrackAllocator
{
    private readonly MediaTime[] boundaries;
    private readonly int[] maxima;
    private readonly int[] pending;

    internal SubtitleImportTrackAllocator(IReadOnlyList<SubtitleLine> lines)
    {
        boundaries = lines.SelectMany(line => new[] { line.Start, line.End }).Distinct().Order().ToArray();
        maxima = new int[Math.Max(1, boundaries.Length * 4)];
        pending = new int[maxima.Length];
        Array.Fill(maxima, -1);
        Array.Fill(pending, -1);
    }

    internal int[] Allocate(IReadOnlyList<SubtitleLine> lines)
    {
        var result = new int[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Start >= line.End)
            {
                throw new InvalidDataException("The imported subtitle end time must be later than its start time.");
            }
            var start = Array.BinarySearch(boundaries, line.Start);
            var end = Array.BinarySearch(boundaries, line.End) - 1;
            var rank = Query(1, 0, boundaries.Length - 2, start, end) + 1;
            result[index] = rank;
            Update(1, 0, boundaries.Length - 2, start, end, rank);
        }
        return result;
    }

    private int Query(int node, int left, int right, int start, int end)
    {
        if (start <= left && right <= end)
        {
            return maxima[node];
        }
        Push(node);
        var middle = left + (right - left) / 2;
        var result = -1;
        if (start <= middle)
        {
            result = Query(node * 2, left, middle, start, end);
        }
        if (end > middle)
        {
            result = Math.Max(result, Query(node * 2 + 1, middle + 1, right, start, end));
        }
        return result;
    }

    private void Update(int node, int left, int right, int start, int end, int rank)
    {
        if (start <= left && right <= end)
        {
            Apply(node, rank);
            return;
        }
        Push(node);
        var middle = left + (right - left) / 2;
        if (start <= middle)
        {
            Update(node * 2, left, middle, start, end, rank);
        }
        if (end > middle)
        {
            Update(node * 2 + 1, middle + 1, right, start, end, rank);
        }
        maxima[node] = Math.Max(maxima[node * 2], maxima[node * 2 + 1]);
    }

    private void Push(int node)
    {
        if (pending[node] < 0)
        {
            return;
        }
        Apply(node * 2, pending[node]);
        Apply(node * 2 + 1, pending[node]);
        pending[node] = -1;
    }

    private void Apply(int node, int rank)
    {
        maxima[node] = Math.Max(maxima[node], rank);
        pending[node] = Math.Max(pending[node], rank);
    }
}
