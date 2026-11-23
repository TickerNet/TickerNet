using TickerNet.Hub.Internal;
using TickerNet.Hub.Protocol;
using TickerNet.Hub.Protocol.V1;
using TickerNet.Utilities;

namespace TickerNet.Hub.Tests;

public sealed class ProtocolTests
{
    private const string Secret = HubTestHost.Secret;

    private static Hello NewHello()
    {
        var hello = new Hello
        {
            NodeName = "billing",
            WorkerVersion = "1.0.0",
            ProtocolVersion = HelloSignature.ProtocolVersion,
            Nonce = Guid.NewGuid().ToString("N"),
            UnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        hello.Functions.Add(new FunctionDescriptor { Name = "SendInvoices", CronExpression = "0 3 * * *" });
        hello.Functions.Add(new FunctionDescriptor { Name = "Charge" });
        hello.Signature = HelloSignature.ForHello(Secret, hello);
        return hello;
    }

    private static bool Verify(string secret, Hello hello)
        => HelloSignature.Matches(HelloSignature.ForHello(secret, hello), hello.Signature);

    [Fact]
    public void A_signed_hello_verifies()
        => Assert.True(Verify(Secret, NewHello()));

    [Fact]
    public void A_hello_signed_with_another_secret_fails()
        => Assert.False(Verify("another-secret-that-is-also-long-enough-0", NewHello()));

    [Fact]
    public void The_ack_signature_binds_the_nonce_and_worker_id()
    {
        var ack = HelloSignature.ForAck(Secret, "nonce-1", "worker-1");
        Assert.True(HelloSignature.Matches(HelloSignature.ForAck(Secret, "nonce-1", "worker-1"), ack));
        Assert.False(HelloSignature.Matches(HelloSignature.ForAck(Secret, "nonce-2", "worker-1"), ack));
        Assert.False(HelloSignature.Matches(HelloSignature.ForAck(Secret, "nonce-1", "worker-2"), ack));
    }

    [Fact]
    public void Extreme_timestamps_are_stale_without_overflowing()
    {
        var guard = new HelloReplayGuard();
        Assert.False(guard.IsFresh(long.MinValue, DateTimeOffset.UtcNow));
        Assert.False(guard.IsFresh(long.MaxValue, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("a-b-c", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("new\nline", false)]
    public void Nonces_must_be_plain(string nonce, bool valid)
        => Assert.Equal(valid, HelloReplayGuard.IsWellFormedNonce(nonce.Replace("\\n", "\n")));

    [Fact]
    public void Changing_a_signed_function_list_breaks_the_signature()
    {
        var hello = NewHello();
        hello.Functions[0].CronExpression = "* * * * *";
        Assert.False(Verify(Secret, hello));
    }

    [Fact]
    public void Function_order_doesnt_change_the_signature()
    {
        var hello = NewHello();
        var reordered = hello.Clone();
        reordered.Functions.Clear();
        reordered.Functions.Add(hello.Functions.Reverse());
        Assert.True(Verify(Secret, reordered));
    }

    [Fact]
    public void A_nonce_is_accepted_once()
    {
        var guard = new HelloReplayGuard();
        var now = DateTimeOffset.UtcNow;
        Assert.True(guard.TryUse("nonce-1", now));
        Assert.False(guard.TryUse("nonce-1", now));
        Assert.True(guard.TryUse("nonce-2", now));
        Assert.False(guard.TryUse("bad nonce", now));
    }

    [Fact]
    public void Timestamps_more_than_five_minutes_off_are_stale()
    {
        var guard = new HelloReplayGuard();
        var now = DateTimeOffset.UtcNow;
        Assert.True(guard.IsFresh(now.ToUnixTimeSeconds() - 299, now));
        Assert.False(guard.IsFresh(now.ToUnixTimeSeconds() - 301, now));
        Assert.False(guard.IsFresh(now.ToUnixTimeSeconds() + 301, now));
    }

    [Theory]
    [InlineData("Send@billing", true, "billing", "Send")]
    [InlineData("Send", false, "", "Send")]
    [InlineData("@billing", false, "", "@billing")]
    [InlineData("Send@", false, "", "Send@")]
    public void Remote_function_names_split_into_function_and_node(string name, bool isRemote, string node, string function)
    {
        Assert.Equal(isRemote, RemoteFunctionName.IsRemote(name));
        Assert.Equal(node, RemoteFunctionName.GetNodeName(name));
        Assert.Equal(function, RemoteFunctionName.GetFunctionName(name));
    }

    [Fact]
    public void Qualify_adds_the_node_once()
    {
        Assert.Equal("Send@billing", RemoteFunctionName.Qualify("Send", "billing"));
        Assert.Equal("Send@billing", RemoteFunctionName.Qualify("Send@billing", "other"));
        Assert.Equal("Send", RemoteFunctionName.Qualify("Send", ""));
    }

    [Theory]
    [InlineData("billing", true)]
    [InlineData("billing-eu.1", true)]
    [InlineData("", false)]
    [InlineData("bill ing", false)]
    [InlineData("bill@ing", false)]
    public void Node_names_are_checked(string node, bool valid)
        => Assert.Equal(valid, RemoteFunctionName.IsValidNodeName(node));
}
