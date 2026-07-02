namespace Greenhouse.Mqtt;

/// <summary>
/// Matches concrete MQTT topics against subscription filters using standard wildcard rules:
/// <c>+</c> matches exactly one level, <c>#</c> matches the remaining levels (and must be the
/// final character). Used to route inbound messages to the handlers that subscribed for them.
/// </summary>
internal static class MqttTopicMatcher
{
    public static bool Matches(string topicPattern, string topic)
    {
        if (topicPattern == topic)
        {
            return true;
        }

        var patternLevels = topicPattern.Split('/');
        var topicLevels = topic.Split('/');

        for (var i = 0; i < patternLevels.Length; i++)
        {
            var patternLevel = patternLevels[i];

            if (patternLevel == "#")
            {
                // Multi-level wildcard: matches this and every remaining level. Must be terminal.
                return i == patternLevels.Length - 1;
            }

            if (i >= topicLevels.Length)
            {
                return false;
            }

            if (patternLevel == "+")
            {
                // Single-level wildcard: matches exactly one level.
                continue;
            }

            if (patternLevel != topicLevels[i])
            {
                return false;
            }
        }

        // With no trailing '#', the pattern matches only when both have the same level count.
        return patternLevels.Length == topicLevels.Length;
    }
}
