using System.Numerics;

namespace AegiNext.Koumori;

internal sealed class KoumoriEncoderFrontend
{
    private readonly KoumoriEncoderFrontendDefinition definition;
    private readonly Complex[] roots;
    private readonly int[] filterStarts;
    private readonly int[] filterEnds;

    public KoumoriEncoderFrontend(KoumoriEncoderFrontendDefinition definition)
    {
        this.definition = definition;
        roots = Enumerable.Range(0, definition.FftSize)
            .Select(index => Complex.FromPolarCoordinates(1, -2 * Math.PI * index / definition.FftSize)).ToArray();
        filterStarts = definition.MelFilters.Select(filter => Array.FindIndex(filter, value => value > 0)).ToArray();
        filterEnds = definition.MelFilters.Select(filter => Array.FindLastIndex(filter, value => value > 0) + 1).ToArray();
    }

    public float[] ExtractInput(ReadOnlySpan<float> pcm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pcm.Length != definition.InputSamples)
        {
            throw new ArgumentException("The encoder requires one complete zero-padded PCM chunk.", nameof(pcm));
        }

        foreach (var sample in pcm)
        {
            if (!float.IsFinite(sample))
            {
                throw new InvalidDataException("Koumori requires finite mono PCM samples.");
            }
        }

        var output = new float[definition.MelBins * definition.InputFrames];
        var window = new Complex[definition.FftSize];
        var spectrum = new Complex[definition.FftSize];
        var power = new double[definition.FftSize / 2 + 1];
        var maximum = float.NegativeInfinity;
        for (var frame = 0; frame < definition.InputFrames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = frame * definition.HopSamples - definition.WindowSamples / 2;
            for (var index = 0; index < window.Length; index++)
            {
                var sample = first + index;
                sample = sample < 0 ? -sample : sample >= pcm.Length ? 2 * pcm.Length - 2 - sample : sample;
                window[index] = new(pcm[sample] * definition.HannWindow[index], 0);
            }

            Transform(window, 0, 1, spectrum, 0, window.Length);
            for (var index = 0; index < power.Length; index++)
            {
                power[index] = spectrum[index].Real * spectrum[index].Real + spectrum[index].Imaginary * spectrum[index].Imaginary;
            }

            for (var band = 0; band < definition.MelBins; band++)
            {
                var energy = 0d;
                var filter = definition.MelFilters[band];
                for (var index = filterStarts[band]; index < filterEnds[band]; index++)
                {
                    energy += power[index] * filter[index];
                }

                var value = (float)Math.Log10(Math.Max(energy, definition.MelLogFloor));
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("The encoder log-mel frontend produced non-finite values.");
                }

                output[band * definition.InputFrames + frame] = value;
                maximum = Math.Max(maximum, value);
            }
        }

        var floor = maximum - (float)definition.DynamicRange;
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = (Math.Max(output[index], floor) + (float)definition.LogOffset) / (float)definition.LogScale;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    private void Transform(Complex[] input, int inputOffset, int stride, Complex[] output, int outputOffset, int length)
    {
        if (length == 1)
        {
            output[outputOffset] = input[inputOffset];
            return;
        }

        var radix = length % 2 == 0 ? 2 : 5;
        var width = length / radix;
        for (var group = 0; group < radix; group++)
        {
            Transform(input, inputOffset + group * stride, stride * radix, output, outputOffset + group * width, width);
        }

        Span<Complex> combined = stackalloc Complex[5];
        var step = definition.FftSize / length;
        for (var bin = 0; bin < width; bin++)
        {
            for (var lane = 0; lane < radix; lane++)
            {
                var sum = Complex.Zero;
                var frequency = bin + lane * width;
                for (var group = 0; group < radix; group++)
                {
                    sum += output[outputOffset + group * width + bin] * roots[frequency * group * step % roots.Length];
                }

                combined[lane] = sum;
            }

            for (var lane = 0; lane < radix; lane++)
            {
                output[outputOffset + lane * width + bin] = combined[lane];
            }
        }
    }
}
