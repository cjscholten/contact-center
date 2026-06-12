using ContactCenter.Api.Agents;

namespace ContactCenter.Tests;

public class ChannelNamesTests
{
    [Theory]
    [InlineData("PJSIP/agent1001-0000000d", "PJSIP/agent1001", true)]
    [InlineData("pjsip/AGENT1001-0000000d", "PJSIP/agent1001", true)]
    [InlineData("PJSIP/agent1001-0000000d", "PJSIP/agent100", false)] // prefix-val: agent100 ≠ agent1001
    [InlineData("PJSIP/sbc-00000004", "PJSIP/agent1001", false)]
    [InlineData("PJSIP/agent1001", "PJSIP/agent1001", false)] // endpoint zelf is geen kanaal
    public void Kanaal_endpoint_matching(string channel, string endpoint, bool expected)
        => Assert.Equal(expected, ChannelNames.BelongsToEndpoint(channel, endpoint));
}
