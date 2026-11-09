namespace CodeLearning.Core.Grading;

// What GNU time -v reports about one test run. The TestRunner points time's -o file at a directory
// the submission cannot write to, so these values come from the trusted side of the sandbox
public sealed record TimeStats(long PeakMemoryKb, int? TerminatingSignal, int? ExitStatus)
{
    public static readonly TimeStats Unknown = new(0, null, null);

    private const string PeakMemoryMarker = "Maximum resident set size (kbytes):";
    private const string CommandMarker = "Command being timed:";
    private const string SignalMarker = "Command terminated by signal";
    private const string ExitStatusMarker = "Exit status:";

    public static TimeStats Parse(string? timeOutput)
    {
        if (string.IsNullOrEmpty(timeOutput))
        {
            return Unknown;
        }

        long? peakMemoryKb = null;
        int? signal = null;
        int? exitStatus = null;
        var reportStarted = false;

        using var reader = new StringReader(timeOutput);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(CommandMarker, StringComparison.Ordinal))
            {
                reportStarted = true;
            }

            // Only the first occurrence counts: time writes its report from the start of the file,
            // so anything else in it can only come after. The signal line precedes the report itself
            if (peakMemoryKb == null && trimmed.StartsWith(PeakMemoryMarker, StringComparison.Ordinal))
            {
                peakMemoryKb = ParseLong(trimmed[PeakMemoryMarker.Length..]);
            }
            else if (signal == null && !reportStarted && trimmed.StartsWith(SignalMarker, StringComparison.Ordinal))
            {
                signal = (int?)ParseLong(trimmed[SignalMarker.Length..]);
            }
            else if (exitStatus == null && trimmed.StartsWith(ExitStatusMarker, StringComparison.Ordinal))
            {
                exitStatus = (int?)ParseLong(trimmed[ExitStatusMarker.Length..]);
            }
        }

        return new TimeStats(peakMemoryKb ?? 0, signal, exitStatus);
    }

    private static long? ParseLong(string value) =>
        long.TryParse(value.Trim(), out var parsed) ? parsed : null;
}
