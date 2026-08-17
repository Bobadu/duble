#nullable enable
using System;
using Duble.Core.Results;
using Xunit;

namespace Duble.Tests;

public class ResultTests
{
    [Fact]
    public void Ok_carries_the_value()
    {
        var r = Result<int>.Ok(42);
        Assert.True(r.IsSuccess);
        Assert.False(r.IsFailure);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public void Fail_carries_the_code_and_the_message()
    {
        var r = Result<int>.Fail(ErrorCodes.SourceMissing, @"no such folder: C:\nope");
        Assert.True(r.IsFailure);
        Assert.Equal("source.missing", r.Error.Code);
        Assert.Contains(@"C:\nope", r.Error.Message);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws_instead_of_returning_a_default()
    {
        var r = Result<string>.Fail(ErrorCodes.SourceMissing, "gone");
        var thrown = Assert.Throws<InvalidOperationException>(() => r.Value);
        Assert.Contains("source.missing", thrown.Message);
    }

    [Fact]
    public void Match_runs_the_branch_that_matches_the_outcome()
    {
        Assert.Equal("ok:7", Result<int>.Ok(7).Match(v => "ok:" + v, e => "err:" + e.Code));
        Assert.Equal("err:source.missing",
            Result<int>.Fail(ErrorCodes.SourceMissing, "x").Match(v => "ok:" + v, e => "err:" + e.Code));
    }

    [Fact]
    public void A_result_without_a_value_reports_both_outcomes()
    {
        Assert.True(Result.Ok().IsSuccess);
        var f = Result.Fail(ErrorCodes.ApplyIo, "target is locked");
        Assert.True(f.IsFailure);
        Assert.Equal("apply.io", f.Error.Code);
        Assert.Equal("apply.io: target is locked", f.Error.ToString());
    }
}
