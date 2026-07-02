namespace Greenhouse.Mqtt.Tests;

public class MqttTopicMatcherTests
{
    [Theory]
    [InlineData("gh/edge-1/telemetry", "gh/edge-1/telemetry", true)]   // exact
    [InlineData("gh/+/telemetry", "gh/edge-1/telemetry", true)]        // single-level wildcard
    [InlineData("gh/+/telemetry", "gh/edge-1/heartbeat", false)]       // single-level mismatch
    [InlineData("gh/+/telemetry", "gh/edge-1/room/telemetry", false)]  // + spans exactly one level
    [InlineData("gh/#", "gh/edge-1/telemetry", true)]                  // multi-level wildcard
    [InlineData("gh/#", "gh", true)]                                    // # matches parent level
    [InlineData("+", "gh", true)]                                       // + at root
    [InlineData("+", "gh/edge-1", false)]                               // + is one level only
    [InlineData("gh/a", "gh/a/b", false)]                               // pattern shorter than topic
    [InlineData("gh/a/b", "gh/a", false)]                               // pattern longer than topic
    public void Matches_follows_mqtt_wildcard_rules(string pattern, string topic, bool expected)
    {
        Assert.Equal(expected, MqttTopicMatcher.Matches(pattern, topic));
    }
}
