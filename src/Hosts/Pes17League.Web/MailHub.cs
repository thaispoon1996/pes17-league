namespace Pes17League.Web;

/// <summary>Hòm thư: báo có thư mới/đổi trạng thái để trang thư + huy hiệu chưa đọc trên menu cập nhật ngay.</summary>
public sealed class MailHub
{
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
