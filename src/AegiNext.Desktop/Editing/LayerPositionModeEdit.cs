namespace AegiNext.Desktop.Editing;

internal sealed record LayerPositionModeEdit(bool IsExplicit) : LayerPositionDraftEdit
{
    internal override void ApplyTo(LayerPositionDraft target)
    {
        target.IsExplicit = IsExplicit;
    }
}
