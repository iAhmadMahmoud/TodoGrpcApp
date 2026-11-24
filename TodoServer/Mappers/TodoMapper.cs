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
            IsCompleted = entity.IsCompleted
        };
    }

    public static IEnumerable<TodoItem> ToProtoList(this IEnumerable<TodoEntity> entities)
    {
        return entities.Select(ToProto);
    }
}