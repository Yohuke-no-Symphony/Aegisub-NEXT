namespace AegiNext.Rendering.Images;

internal sealed class ImageSessionLease(ImageDecodeSession session, bool ownsSession) : IDisposable
{
    internal ImageDecodeSession Session { get; } = session;

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsSession)
        {
            Session.Dispose();
        }
    }
}
