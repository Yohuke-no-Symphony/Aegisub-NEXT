namespace AegiNext.Desktop.Editing;

internal abstract record LayerPositionDraftEdit
{
    internal abstract void ApplyTo(LayerPositionDraft target);
}
