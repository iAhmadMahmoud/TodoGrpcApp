using Google.Protobuf.WellKnownTypes;
using TodoServer.Models;

namespace TodoServer.Mappers;

internal static class TodoMapper
{
    public static TodoItem ToProto(this TodoEntity entity)
    {
        return new TodoItem
        {
            Id = entity.Id,
            Title = entity.Title,
            IsCompleted = entity.IsCompleted,
            IsDeleted = entity.IsDeleted,
            DeletedAtUtc = entity.DeletedAtUtc is null
                ? null
                : Timestamp.FromDateTime(DateTime.SpecifyKind(entity.DeletedAtUtc.Value, DateTimeKind.Utc))
        };
    }

    public static IEnumerable<TodoItem> ToProtoList(this IEnumerable<TodoEntity> entities)
    {
        return entities.Select(ToProto);
    }
}