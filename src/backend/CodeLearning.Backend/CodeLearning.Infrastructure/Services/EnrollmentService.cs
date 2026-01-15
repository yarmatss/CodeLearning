using CodeLearning.Application.DTOs.Enrollment;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class EnrollmentService(ApplicationDbContext context) : IEnrollmentService
{
    public async Task<EnrollmentResponseDto> EnrollAsync(Guid courseId, Guid studentId)
    {
        var course = await context.Courses.FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException($"Course with ID {courseId} not found");

        if (course.Status != CourseStatus.Published)
        {
            throw new InvalidOperationException("Cannot enroll in unpublished course");
        }

        var existingEnrollment = await context.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == studentId);

        if (existingEnrollment != null)
        {
            throw new InvalidOperationException("Already enrolled in this course");
        }

        var enrollment = new StudentCourseProgress
        {
            StudentId = studentId,
            CourseId = courseId,
            EnrolledAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            CurrentBlockId = null,
            Student = null!,
            Course = null!
        };

        context.StudentCourseProgresses.Add(enrollment);
        await context.SaveChangesAsync();

        return new EnrollmentResponseDto
        {
            CourseId = courseId,
            Message = "Successfully enrolled in course",
            EnrolledAt = enrollment.EnrolledAt
        };
    }

    public async Task UnenrollAsync(Guid courseId, Guid studentId)
    {
        var enrollment = await context.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == studentId)
            ?? throw new KeyNotFoundException("Enrollment not found");

        var blockProgress = await context.StudentBlockProgresses
            .Where(bp => bp.StudentId == studentId && bp.Block.Subchapter.Chapter.CourseId == courseId)
            .ToListAsync();

        context.StudentBlockProgresses.RemoveRange(blockProgress);
        context.StudentCourseProgresses.Remove(enrollment);

        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<EnrolledCourseDto>> GetEnrolledCoursesAsync(Guid studentId)
    {
        var enrollments = await context.StudentCourseProgresses
            .Include(p => p.Course)
                .ThenInclude(c => c.Instructor)
            .Include(p => p.Course)
                .ThenInclude(c => c.Chapters)
                    .ThenInclude(ch => ch.Subchapters)
                        .ThenInclude(s => s.Blocks)
            .Where(p => p.StudentId == studentId)
            .ToListAsync();

        var completedBlocksByCourse = await context.StudentBlockProgresses
            .Where(bp => bp.StudentId == studentId && bp.IsCompleted)
            .Include(bp => bp.Block)
                .ThenInclude(b => b.Subchapter)
                    .ThenInclude(s => s.Chapter)
            .ToListAsync();

        var completedBlocksCountByCourse = completedBlocksByCourse
            .GroupBy(bp => bp.Block.Subchapter.Chapter.CourseId)
            .ToDictionary(g => g.Key, g => g.Count());

        var enrolledCourses = enrollments.Select(enrollment =>
        {
            var totalBlocks = enrollment.Course.Chapters
                .SelectMany(ch => ch.Subchapters)
                .SelectMany(s => s.Blocks)
                .Count();

            var completedBlocks = completedBlocksCountByCourse.GetValueOrDefault(enrollment.CourseId, 0);

            var progressPercentage = totalBlocks > 0
                ? (double)completedBlocks / totalBlocks * 100
                : 0;

            return new EnrolledCourseDto
            {
                CourseId = enrollment.CourseId,
                CourseTitle = enrollment.Course.Title,
                CourseDescription = enrollment.Course.Description,
                InstructorName = $"{enrollment.Course.Instructor.FirstName} {enrollment.Course.Instructor.LastName}",
                EnrolledAt = enrollment.EnrolledAt,
                LastActivityAt = enrollment.LastActivityAt,
                CurrentBlockId = enrollment.CurrentBlockId,
                CompletedBlocksCount = completedBlocks,
                TotalBlocksCount = totalBlocks,
                ProgressPercentage = Math.Round(progressPercentage, 2)
            };
        });

        return enrolledCourses.OrderByDescending(e => e.LastActivityAt);
    }

    public async Task<bool> IsEnrolledAsync(Guid courseId, Guid studentId)
    {
        return await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == courseId && p.StudentId == studentId);
    }
}
