using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace ContactCenter.Api.Ami;

/// <summary>
/// Minimale AMI-client: verbinding per actie (login → actie → sluiten).
/// Volume is laag (alleen login/logout/nawerktijd van agents), dus geen
/// persistente verbinding om te bewaken.
/// </summary>
public sealed class AmiClient(IOptions<AmiOptions> options, ILogger<AmiClient> logger) : IQueueMemberControl
{
    public async Task AddAsync(string queue, string endpoint, string memberName, CancellationToken ct = default)
    {
        var response = await SendActionAsync("QueueAdd",
        [
            ("Queue", queue),
            ("Interface", endpoint),
            ("MemberName", memberName),
            ("StateInterface", endpoint),
        ], ct);

        // idempotent: nogmaals inloggen is geen fout
        if (IsError(response) && !Message(response).Contains("Already there", StringComparison.OrdinalIgnoreCase))
            throw new AmiActionException("QueueAdd", Message(response));
    }

    public async Task RemoveAsync(string queue, string endpoint, CancellationToken ct = default)
    {
        var response = await SendActionAsync("QueueRemove",
        [
            ("Queue", queue),
            ("Interface", endpoint),
        ], ct);

        if (IsError(response))
            logger.LogWarning("QueueRemove {Endpoint} uit '{Queue}': {Melding}", endpoint, queue, Message(response));
    }

    public async Task SetPausedAsync(string endpoint, bool paused, string reason, CancellationToken ct = default)
    {
        var response = await SendActionAsync("QueuePause",
        [
            ("Interface", endpoint),
            ("Paused", paused ? "true" : "false"),
            ("Reason", reason),
        ], ct);

        if (IsError(response))
            logger.LogWarning("QueuePause({Paused}) voor {Endpoint}: {Melding}", paused, endpoint, Message(response));
    }

    private async Task<Dictionary<string, string>> SendActionAsync(
        string action, (string Key, string Value)[] fields, CancellationToken ct)
    {
        var opts = options.Value;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(opts.Host, opts.Port, ct);
        await using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        await reader.ReadLineAsync(ct); // bannerregel "Asterisk Call Manager/x.y"

        await WriteActionAsync(writer, "Login",
        [
            ("Username", opts.Username),
            ("Secret", opts.Secret),
            ("Events", "off"),
        ]);
        var login = await ReadBlockAsync(reader, ct);
        if (IsError(login))
            throw new AmiActionException("Login", Message(login));

        await WriteActionAsync(writer, action, fields);
        return await ReadBlockAsync(reader, ct);
    }

    private static async Task WriteActionAsync(
        StreamWriter writer, string action, (string Key, string Value)[] fields)
    {
        await writer.WriteLineAsync($"Action: {action}");
        foreach (var (key, value) in fields)
            await writer.WriteLineAsync($"{key}: {value}");
        await writer.WriteLineAsync();
    }

    private static async Task<Dictionary<string, string>> ReadBlockAsync(StreamReader reader, CancellationToken ct)
    {
        var block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync(ct) is { } line && line.Length > 0)
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator > 0)
                block[line[..separator]] = line[(separator + 2)..];
        }
        return block;
    }

    private static bool IsError(Dictionary<string, string> response)
        => !response.TryGetValue("Response", out var r) || !r.Equals("Success", StringComparison.OrdinalIgnoreCase);

    private static string Message(Dictionary<string, string> response)
        => response.GetValueOrDefault("Message", "(geen melding)");
}

public sealed class AmiActionException(string action, string message)
    : Exception($"AMI {action} faalde: {message}");
