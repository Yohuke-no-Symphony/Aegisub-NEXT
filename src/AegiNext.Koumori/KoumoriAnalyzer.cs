using Microsoft.ML.OnnxRuntime;
using System.Text.Json;

namespace AegiNext.Koumori;

/// <summary>
/// Runs the versioned Koumori frontend, CPU ONNX model and subtitle decoder on bounded PCM windows.
/// </summary>
public sealed class KoumoriAnalyzer : IDisposable, IAsyncDisposable
{
    public const int SAMPLE_RATE = 16000;
    public const int HOP_SAMPLES = 160;

    private readonly KoumoriModelPackage package;
    private readonly KoumoriFrontend frontend;
    private readonly KoumoriEncoderFrontend? encoderFrontend;
    private readonly InferenceSession session;
    private readonly InferenceSession? encoderSession;
    private readonly InferenceSession? snapSession;
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private int disposed;

    private KoumoriAnalyzer(KoumoriModelPackage package)
    {
        this.package = package;
        frontend = new(package.Frontend);
        if (package.EncoderFrontend is { } encoderDefinition)
        {
            encoderFrontend = new(encoderDefinition);
        }
        using var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        options.IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        options.InterOpNumThreads = 1;
        options.AddSessionConfigEntry("optimization.enable_gelu_approximation", "0");
        options.AddSessionConfigEntry("mlas.enable_gemm_fastmath_arm64_bfloat16", "0");
        try
        {
            session = new(package.Model, options);
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
        try
        {
            var inputCount = package.Manifest.SchemaVersion is 2 or 3 ? 2 : 1;
            ValidateTensor(session.InputMetadata, package.Manifest.InputName, [1, 82, 2048], inputCount);
            ValidateTensor(session.OutputMetadata, package.Manifest.OutputName, [1, 1024, package.Manifest.Slots, 3]);
            if (package.Manifest.SchemaVersion is 2 or 3)
            {
                ValidateTensor(session.InputMetadata, package.Manifest.ContextInputName!, [1, 768, 512], inputCount);
                encoderSession = new(package.EncoderModel!, options);
                ValidateTensor(encoderSession.InputMetadata, package.Manifest.EncoderInputName!, [1, 80, 3000]);
                ValidateTensor(encoderSession.OutputMetadata, package.Manifest.EncoderOutputName!, [1, 1500, 768]);
            }
            if (package.Manifest.SchemaVersion == 3)
            {
                snapSession = new(package.SnapModel!, options);
                ValidateTensor(snapSession.InputMetadata, package.Manifest.InputName, [1, 82, 2048], inputCount);
                ValidateTensor(snapSession.InputMetadata, package.Manifest.ContextInputName!, [1, 768, 512], inputCount);
                ValidateTensor(snapSession.OutputMetadata, package.Manifest.OutputName, [1, 1024, package.Manifest.Slots, 3]);
            }
        }
        catch
        {
            DisposeSessions();
            throw;
        }
    }

    public static string DefaultModelDirectory => Path.Combine(AppContext.BaseDirectory, "models", "koumori");

    public string ModelId => package.Manifest.ModelId;

    public int SampleRate => SAMPLE_RATE;

    public string QualityStatus => package.Manifest.Training.ValueKind == JsonValueKind.Object
        && package.Manifest.Training.TryGetProperty("qualityAcceptance", out var acceptance)
        && acceptance.ValueKind == JsonValueKind.Object && acceptance.TryGetProperty("status", out var status)
        && status.ValueKind == JsonValueKind.String ? status.GetString()! : "unassessed";

    public bool IsExperimental => QualityStatus != "passed";

    /// <summary>
    /// Validates a complete model package and creates its CPU session. Missing or invalid packages fail explicitly.
    /// </summary>
    public static async Task<KoumoriAnalyzer> LoadAsync(string modelDirectory, CancellationToken cancellationToken = default)
    {
        var package = await KoumoriModelPackage.LoadAsync(modelDirectory, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var analyzer = new KoumoriAnalyzer(package);
            if (cancellationToken.IsCancellationRequested)
            {
                analyzer.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return analyzer;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Analyzes a media range with global frame alignment and automatic surrounding context reads.
    /// Concurrent requests serialize; cancellation interrupts native inference and leaves no partial result.
    /// </summary>
    public Task<KoumoriAnalysisResult> AnalyzeAsync(IKoumoriAudioSource source, KoumoriAnalysisRange? range = null,
        IProgress<KoumoriAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return AnalyzeProfileAsync(source, "default", range, progress, cancellationToken);
    }

    /// <summary>
    /// Analyzes using a named package profile; unknown profile names use the package default.
    /// </summary>
    public async Task<KoumoriAnalysisResult> AnalyzeProfileAsync(IKoumoriAudioSource source, string profile,
        KoumoriAnalysisRange? range = null, IProgress<KoumoriAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(source.SampleCount);
        var requested = range ?? new KoumoriAnalysisRange(0, source.SampleCount);
        if (requested.StartSample < 0 || requested.EndSample < requested.StartSample || requested.EndSample > source.SampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "The analysis range must lie inside the media sample range.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return await Task.Run(() => AnalyzeCoreAsync(source, requested, profile, progress, token), token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        gate.Wait();
        try
        {
            DisposeSessions();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await lifetime.CancelAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeSessions();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<KoumoriAnalysisResult> AnalyzeCoreAsync(IKoumoriAudioSource source, KoumoriAnalysisRange requested,
        string profile, IProgress<KoumoriAnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requested.StartSample == requested.EndSample)
        {
            progress?.Report(new(0, 0));
            return new(ModelId, requested, [], []);
        }

        var outputFrames = package.Frontend.OutputFrames;
        var sampleCount = source.SampleCount;
        var totalFrames = FrameCount(sampleCount);
        var firstFrame = requested.StartSample / HOP_SAMPLES / outputFrames * outputFrames;
        var lastFrame = Math.Min(totalFrames, checked((FrameCount(requested.EndSample) + outputFrames - 1) / outputFrames * outputFrames));
        var decoder = new KoumoriDecoder(package.Decoder, package.Manifest.Slots, firstFrame, profile);
        progress?.Report(new(0, lastFrame - firstFrame));
        for (var centerFrame = firstFrame; centerFrame < lastFrame; centerFrame += outputFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstInputFrame = Math.Max(0, centerFrame - package.Frontend.ContextFrames);
            var lastInputFrame = Math.Min(totalFrames, centerFrame + outputFrames + package.Frontend.RightContextFrames);
            var readStart = Math.Max(0, checked((firstInputFrame - 1) * HOP_SAMPLES - package.Frontend.WindowSamples / 2));
            var readEnd = Math.Min(sampleCount, checked((lastInputFrame - 1) * HOP_SAMPLES + package.Frontend.WindowSamples / 2));
            var pcmStart = readStart;
            var pcmLength = checked((int)Math.Max(0, readEnd - readStart));
            if (package.EncoderFrontend is { } encoderDefinition)
            {
                pcmStart = checked(centerFrame * HOP_SAMPLES - encoderDefinition.LeftContextSamples);
                pcmLength = encoderDefinition.InputSamples;
                readStart = Math.Max(0, pcmStart);
                readEnd = Math.Min(sampleCount, checked(pcmStart + pcmLength));
            }

            var pcm = new float[pcmLength];
            var readOffset = checked((int)(readStart - pcmStart));
            var readLength = checked((int)Math.Max(0, readEnd - readStart));
            var read = 0;
            while (read < readLength)
            {
                var count = await source.ReadAsync(readStart + read, pcm.AsMemory(readOffset + read, readLength - read), cancellationToken).ConfigureAwait(false);
                if (count < 0 || count > readLength - read)
                {
                    throw new InvalidDataException("The PCM source returned an invalid sample count.");
                }

                if (count == 0)
                {
                    sampleCount = Math.Min(sampleCount, readStart + read);
                    totalFrames = FrameCount(sampleCount);
                    lastFrame = Math.Min(lastFrame, totalFrames);
                    break;
                }

                read += count;
            }

            if (centerFrame >= lastFrame)
            {
                break;
            }

            var features = frontend.ExtractInput(pcm, pcmStart, centerFrame, totalFrames, cancellationToken);
            var countFrames = (int)Math.Min(outputFrames, lastFrame - centerFrame);
            AnalyzeChunk(features, pcm, countFrames, decoder, cancellationToken);
            progress?.Report(new(centerFrame + countFrames - firstFrame, lastFrame - firstFrame));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var actualRange = new KoumoriAnalysisRange(Math.Min(requested.StartSample, sampleCount), Math.Min(requested.EndSample, sampleCount));
        return decoder.Complete(ModelId, actualRange, sampleCount, cancellationToken);
    }

    private static long FrameCount(long sampleCount)
    {
        return sampleCount / HOP_SAMPLES + (sampleCount % HOP_SAMPLES == 0 ? 0 : 1);
    }

    private void AnalyzeChunk(float[] features, float[] pcm, int frameCount, KoumoriDecoder decoder,
        CancellationToken cancellationToken)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(features, [1, 82, 2048]);
        using var run = new RunOptions();
        using var registration = cancellationToken.Register(static state => ((RunOptions)state!).Terminate = true, run);
        try
        {
            if (encoderSession is null)
            {
                using var outputs = session.Run(run, [package.Manifest.InputName], [input], [package.Manifest.OutputName]);
                AppendOutput(outputs[0], frameCount, decoder, cancellationToken);
            }
            else
            {
                var context = ExtractContext(pcm, run, cancellationToken);
                using var contextInput = OrtValue.CreateTensorValueFromMemory(context, [1, 768, 512]);
                using var outputs = session.Run(run, [package.Manifest.InputName, package.Manifest.ContextInputName!],
                    [input, contextInput], [package.Manifest.OutputName]);
                if (snapSession is null)
                {
                    AppendOutput(outputs[0], frameCount, decoder, cancellationToken);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var snapOutputs = snapSession.Run(run, [package.Manifest.InputName, package.Manifest.ContextInputName!],
                        [input, contextInput], [package.Manifest.OutputName]);
                    var generation = ReadProbabilities(outputs[0], cancellationToken);
                    var snap = ReadProbabilities(snapOutputs[0], cancellationToken);
                    decoder.Append(generation, snap, frameCount, cancellationToken);
                }
            }
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private float[] ExtractContext(float[] pcm, RunOptions run, CancellationToken cancellationToken)
    {
        var definition = package.EncoderFrontend!;
        var mel = encoderFrontend!.ExtractInput(pcm, cancellationToken);
        using var input = OrtValue.CreateTensorValueFromMemory(mel, [1, definition.MelBins, definition.InputFrames]);
        using var outputs = encoderSession!.Run(run, [package.Manifest.EncoderInputName!], [input], [package.Manifest.EncoderOutputName!]);
        cancellationToken.ThrowIfCancellationRequested();
        var hidden = outputs[0].GetTensorDataAsSpan<float>();
        if (hidden.Length != definition.HiddenFrames * definition.HiddenSize)
        {
            throw new InvalidDataException("Koumori encoder returned an invalid hidden tensor.");
        }

        for (var index = 0; index < hidden.Length; index++)
        {
            if (!float.IsFinite(hidden[index]))
            {
                throw new InvalidDataException("Koumori encoder returned non-finite hidden values.");
            }
        }

        var context = new float[definition.HiddenSize * definition.ContextFrames];
        for (var frame = 0; frame < definition.ContextFrames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = (definition.ContextStartFrame + frame) * definition.HiddenSize;
            for (var feature = 0; feature < definition.HiddenSize; feature++)
            {
                context[feature * definition.ContextFrames + frame] = hidden[offset + feature];
            }
        }

        return context;
    }

    private void AppendOutput(OrtValue output, int frameCount, KoumoriDecoder decoder, CancellationToken cancellationToken)
    {
        decoder.Append(ReadProbabilities(output, cancellationToken), frameCount, cancellationToken);
    }

    private ReadOnlySpan<float> ReadProbabilities(OrtValue output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probabilities = output.GetTensorDataAsSpan<float>();
        if (probabilities.Length != package.Frontend.OutputFrames * package.Manifest.Slots * 3)
        {
            throw new InvalidDataException("Koumori returned an invalid probability tensor.");
        }

        for (var index = 0; index < probabilities.Length; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (!float.IsFinite(probabilities[index]) || probabilities[index] is < 0 or > 1)
            {
                throw new InvalidDataException("Koumori output must contain finite sigmoid probabilities.");
            }
        }
        return probabilities;
    }

    private void DisposeSessions()
    {
        try
        {
            snapSession?.Dispose();
        }
        finally
        {
            try
            {
                session.Dispose();
            }
            finally
            {
                try
                {
                    encoderSession?.Dispose();
                }
                finally
                {
                    lifetime.Dispose();
                }
            }
        }
    }

    private static void ValidateTensor(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, int[] shape, int expectedCount = 1)
    {
        if (metadata.Count != expectedCount || !metadata.TryGetValue(name, out var node) || !node.IsTensor
            || node.ElementType != typeof(float) || !node.Dimensions.SequenceEqual(shape))
        {
            throw new InvalidDataException($"Koumori ONNX tensor '{name}' does not match its manifest.");
        }
    }
}
