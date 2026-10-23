namespace AegiNext.Koumori;

internal sealed class KoumoriPeakDetector(float threshold, int radius)
{
    private readonly Queue<KoumoriFramePeak> window = new();
    private int count;
    private int nextCenter;

    public List<KoumoriFramePeak> Peaks { get; } = [];

    public void Append(float value)
    {
        window.Enqueue(new(count++, value));
        if (nextCenter + radius < count)
        {
            EvaluateNext();
        }
    }

    public void Complete()
    {
        while (nextCenter < count)
        {
            EvaluateNext();
        }
    }

    private void EvaluateNext()
    {
        var center = window.First(value => value.Frame == nextCenter);
        if (center.Score >= threshold && !window.Any(value => value.Score > center.Score
                || value.Frame < center.Frame && value.Score == center.Score))
        {
            Peaks.Add(center);
        }

        nextCenter++;
        while (window.TryPeek(out var first) && first.Frame < nextCenter - radius)
        {
            window.Dequeue();
        }
    }
}
