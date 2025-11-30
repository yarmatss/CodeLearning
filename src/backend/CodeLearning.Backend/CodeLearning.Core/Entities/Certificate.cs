namespace CodeLearning.Core.Entities;

public class Certificate : BaseEntity
{
    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }
    public Guid VerificationCode { get; set; }
    public required string CertificateUrl { get; set; }
    public DateTimeOffset IssuedAt { get; set; }

    public required User Student { get; set; }
    public required Course Course { get; set; }
}
