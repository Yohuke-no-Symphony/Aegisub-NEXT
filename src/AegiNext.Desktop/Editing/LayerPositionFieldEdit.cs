namespace AegiNext.Desktop.Editing;

internal sealed record LayerPositionFieldEdit(string FieldKey, string? RawText) : LayerPositionDraftEdit
{
    internal override void ApplyTo(LayerPositionDraft target)
    {
        if (RawText is { } text)
        {
            target.ApplyFieldText(FieldKey, text);
        }
        else
        {
            target.RestoreField(FieldKey);
        }
    }
}
