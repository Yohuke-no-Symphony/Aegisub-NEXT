using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>将 ASS 片段及蒙版动画作为一次可撤销事务导入。</summary>
    public void ImportSubtitleLines(AssImportResult imported, string name)
    {
        Apply("Import subtitle format", document => ProjectEditingOperations.ImportSubtitleLines(document, imported, name));
    }

    /// <summary>将字幕、独立轨道与合成层作为一次可撤销事务导入。</summary>
    public void ImportSubtitleLines(IEnumerable<SubtitleLine> lines, string name)
    {
        Apply("Import subtitle format", document => ProjectEditingOperations.ImportSubtitleLines(document, lines, name));
    }
}
