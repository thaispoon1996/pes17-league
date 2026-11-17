namespace Pes17League.Web;

/// <summary>Ảnh mặt cầu thủ chụp trong game (tools/faces, PO 11/11): $DATA/faces/{PlayerId:N}.jpg, phục vụ tĩnh ở <see cref="RequestPath"/>.</summary>
public sealed record FaceStore(string Dir)
{
    public const string RequestPath = "/img/mat";

    /// <summary>Đường dẫn web (kèm ?v= theo giờ sửa file → chụp lại thì trình duyệt lấy ảnh mới); null = chưa có ảnh.</summary>
    public string? WebPath(Guid playerId)
    {
        var file = new FileInfo(Path.Combine(Dir, playerId.ToString("N") + ".jpg"));
        return file.Exists ? $"{RequestPath}/{file.Name}?v={file.LastWriteTimeUtc.Ticks:x}" : null;
    }
}
