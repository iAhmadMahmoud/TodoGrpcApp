namespace TodoServer.Models;

public enum TodoAuditAction
{
    Created =1,
    Updated = 2,
    SoftDeleted =3
}

public class TodoAuditLog
{
    public int Id { get; set; }
    public int TodoId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TitleSnapshot { get; set; }
    public bool? IsCompletedSnapshot { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
