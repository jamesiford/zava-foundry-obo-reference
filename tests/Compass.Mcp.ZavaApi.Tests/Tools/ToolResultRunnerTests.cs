using System.Text.Json;
using Compass.Mcp.ZavaApi.Host.Tools;
using Compass.Mcp.ZavaApi.Application.Queries;
using Compass.Mcp.ZavaApi.Tests.Support;

namespace Compass.Mcp.ZavaApi.Tests.Tools;

public sealed class ToolResultRunnerTests : IAsyncLifetime
{
    private ZavaApiTestHost _host = null!;
    private ToolResultRunner _runner = null!;

    public async Task InitializeAsync()
    {
        _host = await ZavaApiTestHost.CreateAsync();
        _runner = new ToolResultRunner(_host.Sender);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Serializes_a_result_as_camel_case_json()
    {
        var json = await _runner.RunAsync(new GetMemberSummaryQuery("ZCU-100234"), CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Daniel Park", doc.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("ZCU-100234", doc.RootElement.GetProperty("memberNumber").GetString());
    }

    [Fact]
    public async Task Reports_not_found_as_an_error_payload_instead_of_throwing()
    {
        var json = await _runner.RunAsync(new GetMemberSummaryQuery("ZCU-999999"), CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("member_not_found", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reports_invalid_input_as_an_error_payload_instead_of_throwing()
    {
        var json = await _runner.RunAsync(new ListRecentTransactionsQuery("100234-D02", 0), CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("invalid_request", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Tools_delegate_to_the_matching_query()
    {
        var accounts = await new GetAccountSummaryTool(_runner).RunAsync(null!, "ZCU-100871", CancellationToken.None);
        var transactions = await new ListRecentTransactionsTool(_runner).RunAsync(null!, "100871-D02", null, CancellationToken.None);
        var member = await new GetMemberSummaryTool(_runner).RunAsync(null!, "ZCU-100871", CancellationToken.None);

        using var accountsDoc = JsonDocument.Parse(accounts);
        using var transactionsDoc = JsonDocument.Parse(transactions);
        using var memberDoc = JsonDocument.Parse(member);

        Assert.Equal(2, accountsDoc.RootElement.GetProperty("accounts").GetArrayLength());
        Assert.Equal(ListRecentTransactionsQuery.DefaultCount, transactionsDoc.RootElement.GetProperty("transactions").GetArrayLength());
        Assert.Equal("Maya Lin", memberDoc.RootElement.GetProperty("fullName").GetString());
    }
}
