using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using TodoServer.Data;
using TodoServer.Mappers;
using TodoServer.Models;

namespace TodoServer.Services;

public class TodoServiceImpl : TodoService.TodoServiceBase
{
    private readonly TodoDbContext _db;

    public TodoServiceImpl(TodoDbContext db)
    {
        _db = db;
    }

    public override async Task<TodoItem> CreateTodo(CreateTodoRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,"This is required."));
        }

        var entity = new TodoEntity
        {
            Title = request.Title.Trim(),
            IsCompleted = false
        };

        _db.Todos.Add(entity);
        await _db.SaveChangesAsync(context.CancellationToken);

        return entity.ToProto();
    }

    public override async Task<TodoItem> GetTodo(GetTodoRequest request, ServerCallContext context)
    {
        var entity = await _db.Todos
        .FirstOrDefaultAsync(t=>t.Id==request.Id,context.CancellationToken);

        if(entity is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Todo {request.Id} not found."));
        }

        return entity.ToProto();
    }

    public override async Task<TodoListResponse> ListTodo(EmptyRequest request, ServerCallContext context)
    {
        var entities = await _db.Todos
        .AsNoTracking()
        .OrderBy(t=>t.Id)
        .ToListAsync(context.CancellationToken);

        var response = new TodoListResponse();
        response.Items.AddRange(entities.ToProtoList());
        return response;
    }
    public override async Task<TodoItem> ToggleTodo(ToggleTodoRequest request, ServerCallContext context)
    {
        var entity = await _db.Todos
            .FirstOrDefaultAsync(t => t.Id == request.Id, context.CancellationToken);

        if (entity is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Todo {request.Id} not found."));
        }

        entity.IsCompleted = !entity.IsCompleted;
        await _db.SaveChangesAsync(context.CancellationToken);

        return entity.ToProto();
    }

    public override async Task<DeleteTodoResponse> DeleteTodo(DeleteTodoRequest request, ServerCallContext context)
    {
        var entity = await _db.Todos
            .FirstOrDefaultAsync(t => t.Id == request.Id, context.CancellationToken);

        if (entity is null)
        {
            return new DeleteTodoResponse { Success = false };
        }

        _db.Todos.Remove(entity);
        await _db.SaveChangesAsync(context.CancellationToken);

        return new DeleteTodoResponse { Success = true };
    }

    public override async Task<TodoListResponse> GetAllTodosIncludingDeleted(EmptyRequest request, ServerCallContext context)
    {
        var entities = await _db.Todos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .ToListAsync(context.CancellationToken);

        var response = new TodoListResponse();
        response.Items.AddRange(entities.ToProtoList());
        return response;
    }
}
