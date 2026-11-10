using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Messages;

// Published by the Runner when a submission starts running and when it gets its final status.
// A hint, not the result: it carries only ids, and whoever is told reads the submission itself.
// Delivery is best effort, so nothing may depend on receiving it
public record SubmissionStatusChanged(Guid SubmissionId, Guid StudentId, SubmissionStatus Status);
