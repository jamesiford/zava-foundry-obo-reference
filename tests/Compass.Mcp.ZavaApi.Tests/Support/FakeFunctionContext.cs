using System.Collections.Immutable;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace Compass.Mcp.ZavaApi.Tests.Support;

/// <summary>
/// Minimal <see cref="FunctionContext"/> carrying just what the middleware reads: the MCP invocation context
/// (with its HTTP headers) and a function name. Hand-written because the real context is only built by the
/// Functions host.
/// </summary>
public sealed class FakeFunctionContext : FunctionContext
{
    private readonly FakeFunctionDefinition _definition = new("GetMemberSummaryTool");

    public FakeFunctionContext(IReadOnlyDictionary<string, string>? headers, bool publishInvocationContext = true)
    {
        if (publishInvocationContext)
        {
            Transport? transport = headers is null
                ? null
                : new HttpTransport("http") { Headers = new Dictionary<string, string>(headers) };

            Items["ToolInvocationContext"] = new ToolInvocationContext { Name = "get_member_summary", Transport = transport! };
        }
    }

    public override string InvocationId { get; } = Guid.NewGuid().ToString();

    public override string FunctionId { get; } = Guid.NewGuid().ToString();

    public override TraceContext TraceContext => throw new NotSupportedException();

    public override BindingContext BindingContext => throw new NotSupportedException();

    public override RetryContext RetryContext => throw new NotSupportedException();

    public override IServiceProvider InstanceServices { get; set; } = null!;

    public override FunctionDefinition FunctionDefinition => _definition;

    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

    public override IInvocationFeatures Features => throw new NotSupportedException();

    private sealed class FakeFunctionDefinition(string name) : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];

        public override string PathToAssembly => string.Empty;

        public override string EntryPoint => string.Empty;

        public override string Id => name;

        public override string Name => name;

        public override IImmutableDictionary<string, BindingMetadata> InputBindings =>
            ImmutableDictionary<string, BindingMetadata>.Empty;

        public override IImmutableDictionary<string, BindingMetadata> OutputBindings =>
            ImmutableDictionary<string, BindingMetadata>.Empty;
    }
}
