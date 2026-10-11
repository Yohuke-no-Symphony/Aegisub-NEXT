using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>图形片段创建结果；新增相邻轨道与片段属于同一快照事务。</summary>
public sealed record GraphicClipCreationResult(ProjectDocument Document, Guid ClipId, Guid TrackId, Guid? CreatedTrackId);
