namespace Pes17League.Clubs.Domain;

/// <summary>Bài tường thuật đăng sau trận. <see cref="ByAi"/> = model viết từ số liệu trận; false = bài mẫu từ cùng số liệu.</summary>
public class MatchArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public required string Title { get; set; }
    /// <summary>Các đoạn cách nhau bởi một dòng trống.</summary>
    public required string Body { get; set; }
    public bool ByAi { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

/// <summary>Ảnh cắt từ bản ghi hình, quanh bàn thắng. <see cref="FileName"/> tương đối trong thư mục tin, vd "{fixture:N}/1.jpg".</summary>
public class MatchPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArticleId { get; set; }
    public required string FileName { get; set; }
    public required string Caption { get; set; }
    public int Sort { get; set; }
}
