using Base.Core;

namespace Base.Tests;

public class CoreTests
{
    [Fact]
    public void A_result_carries_a_value_or_an_error_never_both()
    {
        Result<int> ok = 5;
        Result<int> failed = Error.NotFound();
        Assert.True(ok.Ok);
        Assert.Equal(5, ok.Value);
        Assert.False(failed.Ok);
        Assert.Equal(404, failed.Error!.Status);
        Assert.Throws<InvalidOperationException>(() => failed.Value);
        Assert.True(Result.Success.Ok);
    }

    [Fact]
    public void The_validator_reports_every_wrong_field_at_once()
    {
        var v = new Validator();
        v.Text("title", "   ", 10);
        v.Text("name", new string('x', 11), 10);
        v.Text("line", "a\u0007b", 10);
        v.Int("count", 99, 1, 10);
        v.OneOf("kind", "other", ["a", "b"]);
        Assert.Equal("ok", v.OptionalText("note", " ok ", 10));
        var e = v.Error!;
        Assert.Equal(400, e.Status);
        Assert.Equal(Validator.Required, e.Fields!["title"]);
        Assert.Equal(Validator.TooLong, e.Fields["name"]);
        Assert.Equal(Validator.NotValid, e.Fields["line"]);
        Assert.Equal(Validator.OutOfRange, e.Fields["count"]);
        Assert.Equal(Validator.NotValid, e.Fields["kind"]);
        Assert.False(e.Fields.ContainsKey("note"));
    }

    [Fact]
    public void A_multiline_text_keeps_line_breaks_but_no_other_control_characters()
    {
        var v = new Validator();
        Assert.Equal("a\nb", v.Text("note", "a\nb", 10, multiline: true));
        Assert.True(v.Ok);
        v.Text("note", "a\u0000b", 10, multiline: true);
        Assert.False(v.Ok);
    }

    [Fact]
    public void Limits_are_clamped_and_never_trusted()
    {
        Assert.Equal(PageRequest.DefaultLimit, PageRequest.From(null, null).Limit);
        Assert.Equal(PageRequest.MaxLimit, PageRequest.From(100_000, null).Limit);
        Assert.Equal(1, PageRequest.From(-5, "").Limit);
        Assert.Null(PageRequest.From(10, "").Cursor);
    }

    [Fact]
    public void A_page_knows_whether_there_is_more()
    {
        var last = Page<int>.From([1, 2, 3], 3, i => i.ToString());
        Assert.Null(last.Next);
        var more = Page<int>.From([1, 2, 3, 4], 3, i => i.ToString());
        Assert.Equal([1, 2, 3], more.Items);
        Assert.Equal("3", more.Next);
    }

    [Fact]
    public void A_cursor_round_trips_and_garbage_is_refused()
    {
        var id = Ids.New();
        Assert.True(Cursor.TryDecode(Cursor.Encode(id), out var back));
        Assert.Equal(id, back);
        Assert.False(Cursor.TryDecode("not a cursor !", out _));
        Assert.False(Cursor.TryDecode(Cursor.Encode("a", "b"), out _));
        Assert.False(Cursor.TryDecode(null, out _));
    }

    [Fact]
    public void Ids_sort_by_time_and_tokens_are_long_and_different()
    {
        var a = Ids.New();
        Thread.Sleep(3);
        var b = Ids.New();
        Assert.True(string.CompareOrdinal(a.ToString(), b.ToString()) < 0);
        Assert.NotEqual(Ids.Token(), Ids.Token());
        Assert.Equal(43, Ids.Token().Length);           // 256 bit as Base64 without padding
        Assert.False(Ids.TryParse(Guid.Empty.ToString(), out _));
    }
}
