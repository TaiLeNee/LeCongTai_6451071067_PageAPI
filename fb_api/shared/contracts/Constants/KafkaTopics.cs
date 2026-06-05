namespace FbApi.Contracts.Constants;

public static class KafkaTopics
{
    public const string RawEvents = "raw_events";
    public const string ReplyCommands = "reply_commands";
    public const string SendFailed = "send_failed";
    public const string SendRetry = "send_retry";
    public const string DeadLetter = "dead_letter";
}
