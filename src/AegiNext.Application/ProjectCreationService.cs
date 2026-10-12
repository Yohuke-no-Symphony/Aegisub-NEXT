using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>校验工程位置，并在同父目录暂存完整工程后以不覆盖方式提交。</summary>
public static class ProjectCreationService
{
    private const int MAXIMUM_NAME_LENGTH = 128;
    private const string INVALID_NAME_CHARACTERS = "<>:\"/\\|?*";

    /// <summary>创建包含默认轨道和一行空白字幕的初始工程快照。</summary>
    public static ProjectDocument CreateDocument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var document = new ProjectDocument { Name = name };
        return ProjectEditingOperations.CreateSubtitleClips(document, [new()], document.Tracks[0].Id);
    }

    /// <summary>验证名称、绝对父目录和目标冲突；不修改文件系统。</summary>
    public static void Validate(ProjectCreationRequest request)
    {
        var path = GetProjectPath(request);
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) || File.Exists(directory))
        {
            throw new IOException("同名项目目录已存在，请使用其他名称或位置。");
        }
    }

    /// <summary>校验输入形式并返回规范的项目主文件绝对路径；参数名用于标识请求中的错误字段。</summary>
    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "对话框按请求字段标识定位名称和位置错误，公开契约保持 Name 与 ParentDirectory。")]
    public static string GetProjectPath(ProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Length > MAXIMUM_NAME_LENGTH || name != name.Trim() ||
            name is "." or ".." || name.EndsWith('.') || name.Any(char.IsControl) ||
            name.Any(INVALID_NAME_CHARACTERS.Contains) || IsReservedName(name))
        {
            throw new ArgumentException("项目名称不是有效的跨平台目录名称。", nameof(request.Name));
        }

        try
        {
            ProjectValidator.ValidateText(name);
        }
        catch (InvalidDataException error)
        {
            throw new ArgumentException("项目名称包含无效文本。", nameof(request.Name), error);
        }

        var parent = request.ParentDirectory;
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent) || parent.Any(char.IsControl))
        {
            throw new ArgumentException("项目位置必须是本机绝对目录。", nameof(request.ParentDirectory));
        }

        try
        {
            return Path.Combine(Path.GetFullPath(parent), name, name + ".aeginext");
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("项目位置不是有效的本机目录。", nameof(request.ParentDirectory), error);
        }
    }

    /// <summary>创建主工程与 backup 目录；取消或冲突不覆盖已有目录并清理暂存内容。</summary>
    public static async Task<CreatedProject> CreateAsync(ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        var path = GetProjectPath(request);
        var directory = Path.GetDirectoryName(path)!;
        var parent = Path.GetDirectoryName(directory)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".aeginext-create-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporary);
            Directory.CreateDirectory(Path.Combine(temporary, "backup"));
            var document = CreateDocument(request.Name);
            await ProjectStore.CreateAsync(document, Path.Combine(temporary, request.Name + ".aeginext"),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporary, directory);
            return new(path, document);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }

    private static bool IsReservedName(string name)
    {
        var stem = name.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }
}
