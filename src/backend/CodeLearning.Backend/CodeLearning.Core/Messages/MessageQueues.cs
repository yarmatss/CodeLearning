namespace CodeLearning.Core.Messages;

public static class MessageQueues
{
    // Declared by both the API (publish topology) and the Runner (receive endpoint), so the names must match
    public const string Submissions = "submissions";
}
