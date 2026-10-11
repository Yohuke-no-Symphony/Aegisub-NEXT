using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Panels.Styles;

internal interface IStyleEditor
{
    LayerKind Kind { get; }
    Guid? LayerId { get; }
    bool HasDrafts { get; }
    void Load(ProjectDocument document, ProjectLayer layer);
    ProjectDocument Prepare(ProjectDocument document, bool preview);
    void Accept();
    bool RestoreField(string fieldKey);
}
