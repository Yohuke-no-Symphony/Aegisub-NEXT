using System.Numerics;

namespace AegiNext.Koumori;

internal sealed class KoumoriFrontend
{
    private readonly KoumoriFrontendDefinition definition;
    private readonly Complex[] roots;
    private readonly int[] reversed;

    public KoumoriFrontend(KoumoriFrontendDefinition definition)
    {
        this.definition = definition;
        roots = new Complex[definition.FftSize / 2];
        reversed = new int[definition.FftSize];
        var bits = BitOperations.Log2((uint)definition.FftSize);
        for (var index = 0; index < roots.Length; index++)
        {
            roots[index] = Complex.FromPolarCoordinates(1, -2 * Math.PI * index / definition.FftSize);
        }

        for (var index = 0; index < reversed.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < bits; bit++)
            {
                reversed[index] = (reversed[index] << 1) | (value & 1);
                value >>= 1;
            }
        }
    }

    public float[] ExtractInput(ReadOnlySpan<float> pcm, long pcmStartSample, long centerStartFrame, long totalFrames, CancellationToken cancellationToken)
    {
        var output = new float[definition.FeatureCount * definition.InputFrames];
        var firstFrame = centerStartFrame - definition.ContextFrames;
        var firstValidFrame = Math.Max(0, firstFrame);
        var lastValidFrame = Math.Min(totalFrames, firstFrame + definition.InputFrames);
        if (lastValidFrame <= firstValidFrame)
        {
            return output;
        }

        var spectrum = new Complex[definition.FftSize];
        var power = new double[definition.FftSize / 2 + 1];
        var previousMel = new float[definition.MelBins];
        var mel = new float[definition.MelBins];
        if (firstValidFrame > 0)
        {
            ExtractFrame(pcm, pcmStartSample, firstValidFrame - 1, spectrum, power, previousMel);
        }

        for (var frame = firstValidFrame; frame < lastValidFrame; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rms = ExtractFrame(pcm, pcmStartSample, frame, spectrum, power, mel);
            var outputFrame = (int)(frame - firstFrame);
            var flux = 0f;
            for (var feature = 0; feature < definition.MelBins; feature++)
            {
                output[feature * definition.InputFrames + outputFrame] = Normalize(mel[feature], feature);
                flux += Math.Max(0, mel[feature] - previousMel[feature]);
            }

            output[80 * definition.InputFrames + outputFrame] = Normalize(rms, 80);
            output[81 * definition.InputFrames + outputFrame] = Normalize(frame == 0 ? 0 : flux / definition.MelBins, 81);
            (mel, previousMel) = (previousMel, mel);
        }

        return output;
    }

    private float ExtractFrame(ReadOnlySpan<float> pcm, long pcmStartSample, long frame, Complex[] spectrum, double[] power, float[] mel)
    {
        Array.Clear(spectrum);
        var startSample = checked(frame * definition.HopSamples - definition.WindowSamples / 2);
        var squared = 0d;
        for (var index = 0; index < definition.WindowSamples; index++)
        {
            var sourceIndex = startSample + index - pcmStartSample;
            var value = sourceIndex >= 0 && sourceIndex < pcm.Length ? pcm[(int)sourceIndex] : 0;
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException("Koumori requires finite mono PCM samples.");
            }

            squared += (double)value * value;
            spectrum[reversed[index]] = new(value * definition.HannWindow[index], 0);
        }

        for (var length = 2; length <= spectrum.Length; length <<= 1)
        {
            var half = length / 2;
            var step = spectrum.Length / length;
            for (var begin = 0; begin < spectrum.Length; begin += length)
            {
                for (var offset = 0; offset < half; offset++)
                {
                    var even = spectrum[begin + offset];
                    var odd = spectrum[begin + offset + half] * roots[offset * step];
                    spectrum[begin + offset] = even + odd;
                    spectrum[begin + offset + half] = even - odd;
                }
            }
        }

        for (var index = 0; index < power.Length; index++)
        {
            power[index] = (spectrum[index].Real * spectrum[index].Real + spectrum[index].Imaginary * spectrum[index].Imaginary) * definition.PowerScale;
        }

        for (var band = 0; band < mel.Length; band++)
        {
            var energy = 0d;
            var filter = definition.MelFilters[band];
            for (var index = 0; index < power.Length; index++)
            {
                energy += power[index] * filter[index];
            }

            mel[band] = (float)Math.Log(Math.Max(energy, definition.MelLogFloor));
        }

        return (float)Math.Log(Math.Max(Math.Sqrt(squared / definition.WindowSamples), definition.RmsLogFloor));
    }

    private float Normalize(float value, int feature)
    {
        return (value - definition.NormalizationMean[feature]) / definition.NormalizationStd[feature];
    }
}
