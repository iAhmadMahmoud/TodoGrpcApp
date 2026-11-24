# TodoGrpcApp

A Todo list backend built with **ASP.NET Core gRPC** and **EF Core + SQL Server**.

I built this project as a hands-on way to learn gRPC: how a `.proto` contract becomes
generated C# code, how a service is implemented and hosted over HTTP/2, how errors are
reported with status codes, and how a real database layer fits behind a gRPC service.

---

## What I learned

- **Proto-first contracts.** The API is defined in [`Protos/todo.proto`](TodoServer/Protos/todo.proto).
  Messages and the service are declared there, and the tooling generates the C# types and
  the `TodoService.TodoServiceBase` class the server inherits from.
- **Unary RPCs.** Each call is one request → one response, implemented as an
  `override async Task<TResponse> Method(TRequest, ServerCallContext)`.
- **`ServerCallContext`.** Gives the handler the caller's `CancellationToken`, which is
  passed straight into EF Core so a cancelled call stops the database work.
- **Errors are status codes, not exceptions leaking out.** Domain failures are thrown as
  `RpcException(new Status(StatusCode.InvalidArgument | NotFound, "..."))`, so clients get a
  structured gRPC status instead of a generic 500.
- **Server-side code generation.** The `.csproj` marks the proto with
  `GrpcServices="Server"`, so only server stubs are generated.
- **gRPC reflection.** `AddGrpcReflection()` + `MapGrpcReflectionService()` let tools such as
  `grpcurl` discover the service at runtime — no `.proto` file needed on the client.
- **HTTP/2 is the transport.** Kestrel is configured with `Protocols: Http2`, and browsers
  cannot call these endpoints directly, which is why `GET /` only returns a help string.

## Architecture

```
gRPC client (grpcurl / .NET client)
        │  HTTP/2 + protobuf
        ▼
TodoServiceImpl                     (Services/TodoServiceImpl.cs)
        │
        ▼
TodoDbContext  ──►  SQL Server      (Data/TodoDbContext.cs)
        │             ├── Todos
        │             └── TodoAuditLogs
        └── SaveChangesAsync override ──► timestamps + soft delete + audit rows
```

The service layer stays thin: validate, query through EF Core, map the entity to a protobuf
message. Cross-cutting persistence concerns (timestamps, soft delete, auditing) live in the
`TodoDbContext.SaveChangesAsync` override so every write goes through the same path.

## Tech stack

| Piece | Version / choice |
| --- | --- |
| Runtime | .NET 10 (`net10.0`) |
| gRPC server | `Grpc.AspNetCore` 2.64.0 |
| gRPC reflection | `Grpc.AspNetCore.Server.Reflection` 2.85.0 |
| Data access | EF Core 10.0.15 (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Database | SQL Server (LocalDB / Express / Developer) |
| Transport | HTTP/2 (h2c on `http://localhost:5052`) |

## Project structure

```
TodoGrpcApp/
├── TodoGrpcApp.slnx                  # solution (new .slnx format, .NET 10 SDK)
└── TodoServer/
    ├── Program.cs                    # DI, DbContext, gRPC + reflection, endpoint mapping
    ├── TodoServer.csproj             # proto -> codegen, package references
    ├── appsettings.json              # logging, Kestrel HTTP/2
    ├── appsettings.Development.json  # local SQL Server connection string
    ├── Protos/
    │   └── todo.proto                # the gRPC contract (service + messages)
    ├── Services/
    │   └── TodoServiceImpl.cs        # implementation of every RPC
    ├── Data/
    │   └── TodoDbContext.cs          # DbSets, model config, soft delete + audit logic
    ├── Models/
    │   ├── TodoEntity.cs             # database entity for a todo
    │   └── TodoAuditLog.cs           # audit row + TodoAuditAction enum
    ├── Mappers/
    │   └── TodoMapper.cs             # entity -> protobuf message mapping
    ├── Migrations/                   # EF Core migrations (InitialCreate)
    └── Properties/
        └── launchSettings.json       # http :5052 and https :7044 profiles
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (LocalDB, Express, or Developer edition)
- EF Core CLI:
  ```powershell
  dotnet tool install --global dotnet-ef
  ```
- A gRPC client. This README uses [`grpcurl`](https://github.com/fullstorydev/grpcurl/releases)
  (install with `scoop install grpcurl` or grab a release binary). Postman also works.

## Getting started

1. **Clone and restore**

   ```powershell
   git clone <your-repo-url>
   cd TodoGrpcApp
   dotnet restore
   ```

2. **Point the app at your SQL Server instance**

   The connection string lives in
   [`TodoServer/appsettings.Development.json`](TodoServer/appsettings.Development.json):

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=TodoGrpcDb;Trusted_Connection=True;TrustServerCertificate=True;"
     }
   }
   ```

   Adjust `Server=` if your instance is named (e.g. `Server=(localdb)\\MSSQLLocalDB`).
   This is a local, password-less Windows-auth string, so it is safe to commit — but swap it
   for user secrets / environment variables if you ever add real credentials.

3. **Create the database**

   ```powershell
   dotnet ef database update --project TodoServer
   ```

   This applies the `InitialCreate` migration and creates the `Todos` and `TodoAuditLogs` tables.

4. **Run the server**

   ```powershell
   dotnet run --project TodoServer
   ```

   The `http` profile listens on **`http://localhost:5052`** with HTTP/2 (h2c).
   Opening that URL in a browser only shows a help message — gRPC needs a gRPC client.

## API reference

Service: `todo.TodoService`

| RPC | Request | Response | Notes |
| --- | --- | --- | --- |
| `CreateTodo` | `CreateTodoRequest { string title }` | `TodoItem` | Trims the title; blank title → `InvalidArgument` |
| `GetTodo` | `GetTodoRequest { int32 id }` | `TodoItem` | Missing id → `NotFound` |
| `ListTodo` | `EmptyRequest` | `TodoListResponse` | Active todos only, ordered by id |
| `ListDeletedTodos` | `EmptyRequest` | `TodoListResponse` | Deleted todos only (`IgnoreQueryFilters()` + `IsDeleted` filter) |
| `ToggleTodo` | `ToggleTodoRequest { int32 id }` | `TodoItem` | Flips `is_completed`; missing id → `NotFound` |
| `DeleteTodo` | `DeleteTodoRequest { int32 id }` | `DeleteTodoResponse { bool success }` | Soft delete; returns `success=false` when missing |
| `GetAllTodosIncludingDeleted` | `EmptyRequest` | `TodoListResponse` | Uses `IgnoreQueryFilters()` to bypass the soft-delete filter; each item carries `is_deleted` |

Messages:

```protobuf
import "google/protobuf/timestamp.proto";

message TodoItem {
  int32  id                        = 1;
  string title                     = 2;
  bool   is_completed              = 3;
  bool   is_deleted                = 4;
  google.protobuf.Timestamp deleted_at_utc = 5;  // null for active rows
}

message TodoListResponse {
  repeated TodoItem items = 1;
}
```

`is_deleted` is the flag that tells you whether a row came from the soft-delete bin, and
`deleted_at_utc` records when it happened. Both were added at tags `4` and `5`, so the change
is wire-compatible with clients generated from the older contract.

## Trying it with grpcurl

Reflection is enabled, so `grpcurl` can discover the contract at runtime.

```powershell
# Discover the service
grpcurl -plaintext localhost:5052 list
grpcurl -plaintext localhost:5052 describe todo.TodoService

# Create
grpcurl -plaintext -d '{"title":"Buy milk"}' localhost:5052 todo.TodoService/CreateTodo

# Read one / read all
grpcurl -plaintext -d '{"id":1}' localhost:5052 todo.TodoService/GetTodo
grpcurl -plaintext -d '{}'     localhost:5052 todo.TodoService/ListTodo

# Toggle completion
grpcurl -plaintext -d '{"id":1}' localhost:5052 todo.TodoService/ToggleTodo

# Soft delete, then inspect the deleted bin and the combined list
grpcurl -plaintext -d '{"id":1}' localhost:5052 todo.TodoService/DeleteTodo
grpcurl -plaintext -d '{}'       localhost:5052 todo.TodoService/ListDeletedTodos
grpcurl -plaintext -d '{}'       localhost:5052 todo.TodoService/GetAllTodosIncludingDeleted
```

`GetAllTodosIncludingDeleted` is the one that mixes both states, so look at `isDeleted` on each
item: `true` means the row was soft-deleted, `false` means it is still active. `ListDeletedTodos`
is the same query pre-filtered to `isDeleted: true`.

> **Why is `isDeleted: false` missing from the output?** Protobuf does not put default values on
> the wire, so `grpcurl` hides them. Add `-emit-defaults` to see `isDeleted: false` explicitly:
>
> ```powershell
> grpcurl -plaintext -emit-defaults -d '{}' localhost:5052 todo.TodoService/ListTodo
> ```
>
> Your generated C# client always reads the correct value (`false`) regardless.

If you run the `https` profile instead, target port `7044` and add `-insecure`:

```powershell
grpcurl -insecure localhost:7044 list
```

## Design decisions

- **Soft delete, not hard delete.** `DeleteTodo` calls `Remove`, but the
  `SaveChangesAsync` override intercepts `EntityState.Deleted`, switches it to `Modified`,
  and sets `IsDeleted` / `DeletedAtUtc` / `UpdatedAtUtc`. Rows are never physically removed.
- **Global query filter.** `TodoDbContext` declares `HasQueryFilter(t => !t.IsDeleted)`, so
  deleted todos disappear from `GetTodo` and `ListTodo` automatically.
  `GetAllTodosIncludingDeleted` opts out with `IgnoreQueryFilters()`.
- **Audit trail.** Every create / update / soft delete writes a `TodoAuditLogs` row with the
  action, a title snapshot, a completion snapshot, and `OccurredAtUtc`. The rows are written
  in the same `SaveChangesAsync` call as the todo change.
- **Deletion state is part of the contract.** `TodoItem` exposes `is_deleted` and
  `deleted_at_utc`, so a single list response is self-describing. `ListTodo` and
  `ListDeletedTodos` are just the same query with opposite `IsDeleted` filters, and
  `GetAllTodosIncludingDeleted` returns both.
- **UTC everywhere.** `CreatedAtUtc`, `UpdatedAtUtc`, `DeletedAtUtc`, and `OccurredAtUtc` are
  set server-side with `DateTime.UtcNow`.
- **Validation at the edge.** Blank titles are rejected before touching the database, and
  failures are surfaced as gRPC status codes rather than exceptions.
- **No tracking on reads.** List queries use `AsNoTracking()` since they are read-only.
- **Mapping is isolated.** `TodoMapper` keeps entity → protobuf conversion out of the service,
  so the persistence model and the wire contract can evolve separately.

## Next steps

Things I'd fix or add next:

- **`UpdateTodoRequest` is dead code.** The message exists in the proto but no RPC uses it.
  Add an `UpdateTodo` RPC or delete the message.
- **Inconsistent delete semantics.** `DeleteTodo` returns `success=false` for a missing id,
  while `GetTodo` / `ToggleTodo` throw `NotFound`. Pick one style — status codes are more
  gRPC-idiomatic.
- **No client app.** Right now testing is `grpcurl` only. A small console or Blazor client
  would show the generated client stubs in action.
- **No tests.** The service and the soft-delete/audit logic in `TodoDbContext` deserve
  integration tests against a throwaway database.
- **No auth or TLS config.** `MapGrpcService` is wide open and the `https` profile uses the
  dev certificate. Add auth and a real certificate before this leaves localhost.
- **No CI.** A GitHub Actions workflow running `dotnet build` / `dotnet test` would catch
  regressions.
