namespace ContactCenter.Api.Agents;

public static class ChannelNames
{
    /// <summary>Hoort kanaal "PJSIP/agent1001-0000000d" bij endpoint "PJSIP/agent1001"?</summary>
    public static bool BelongsToEndpoint(string channelName, string endpoint)
        => channelName.StartsWith(endpoint + "-", StringComparison.OrdinalIgnoreCase);
}
