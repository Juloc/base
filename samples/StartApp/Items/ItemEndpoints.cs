using Base.Core;
using Base.Web;
using Base.Web.Auth;

namespace StartApp.Items;

public sealed record ItemRequest(string? Title, string? Note, bool? Done);

/// <summary>
/// The API of the list. The pattern of every endpoint: who asks (<c>ctx.Person()</c>, the guard has checked the session), check the input
/// with a <see cref="Validator"/>, call the store, answer with the value or an <see cref="Error"/>.
/// </summary>
internal static class ItemEndpoints
{
    public const int MaxTitle = 200, MaxNote = 4000;

    public static void MapItems(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/items", async (int? limit, string? cursor, string? q, bool? open, HttpContext ctx, ItemStore items) =>
            await items.ListAsync(ctx.Person().UserId, PageRequest.From(limit, cursor), q is { Length: <= 100 } ? q.Trim() : null, open ?? false, ctx.RequestAborted));

        app.MapGet("/api/items/{id:guid}", async (Guid id, HttpContext ctx, ItemStore items) =>
            await items.FindAsync(ctx.Person().UserId, id, ctx.RequestAborted) is { } item ? Results.Ok(item) : Error.NotFound().ToHttp());

        app.MapPost("/api/items", async (ItemRequest body, HttpContext ctx, ItemStore items) =>
        {
            var v = new Validator();
            var title = v.Text("title", body.Title, MaxTitle);
            var note = v.OptionalText("note", body.Note, MaxNote, multiline: true);
            if (v.Error is { } error) return error.ToHttp();
            return Results.Ok(await items.CreateAsync(ctx.Person().UserId, title, note, ctx.RequestAborted));
        });

        app.MapPut("/api/items/{id:guid}", async (Guid id, ItemRequest body, HttpContext ctx, ItemStore items) =>
        {
            var v = new Validator();
            var title = v.Text("title", body.Title, MaxTitle);
            var note = v.OptionalText("note", body.Note, MaxNote, multiline: true);
            if (v.Error is { } error) return error.ToHttp();
            return await items.UpdateAsync(ctx.Person().UserId, id, title, note, body.Done ?? false, ctx.RequestAborted) is { } item ? Results.Ok(item) : Error.NotFound().ToHttp();
        });

        app.MapDelete("/api/items/{id:guid}", async (Guid id, HttpContext ctx, ItemStore items) =>
            await items.DeleteAsync(ctx.Person().UserId, id, ctx.RequestAborted) ? Results.NoContent() : Error.NotFound().ToHttp());
    }
}
