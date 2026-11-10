using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Enrollment;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class EnrollmentService(ApplicationDbContext context) : IEnrollmentService
{
    public async Task<EnrollmentResponseDto> EnrollAsync(Guid courseId, Guid studentId)
    {
        var course = await context.Courses.FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        if (course.Status != CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot enroll in unpublished course");
        }

        var alreadyEnrolled = await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == courseId && p.StudentId == studentId);

        if (alreadyEnrolled)
        {
            throw new BusinessRuleException("Already enrolled in this course");
        }

        var now = DateTimeOffset.UtcNow;
        var enrollment = new StudentCourseProgress
        {
            StudentId = studentId,
            CourseId = courseId,
            EnrolledAt = now,
            LastActivityAt = now,
            CurrentBlockId = null,
            Student = null!,
            Course = null!
        };

        context.StudentCourseProgresses.Add(enrollment);

        // Quiz attempts and accepted submissions survive unenrolling, so a returning student keeps those
        // blocks completed and resumes at the first block still to do
        await ProgressWriter.RestoreCompletedWorkAsync(context, enrollment);

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
            ?? throw new NotFoundException("Enrollment not found");

        // Remove block progress; quiz attempts are kept so re-enrolling cannot reset the one-attempt rule
        var blockProgress = await context.StudentBlockProgresses
            .Where(bp => bp.StudentId == studentId && bp.Block.Subchapter.Chapter.CourseId == courseId)
            .ToListAsync();

        context.StudentBlockProgresses.RemoveRange(blockProgress);
        context.StudentCourseProgresses.Remove(enrollment);

        await context.SaveChangesAsync();
    }

    // Projected and paged in SQL: block counts are computed by the database, nothing is loaded per course
    public async Task<PagedResult<EnrolledCourseDto>> GetEnrolledCoursesAsync(Guid studentId, int? page = null, int? pageSize = null)
    {
        var (pageNumber, size) = PagedResult<EnrolledCourseDto>.Normalize(page, pageSize);

        var enrollments = context.StudentCourseProgresses.Where(p => p.StudentId == studentId);
        var totalCount = await enrollments.CountAsync();

        var rows = await enrollments
            .OrderByDescending(p => p.LastActivityAt)
            .ThenBy(p => p.CourseId)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(p => new
            {
                p.CourseId,
                p.Course.Title,
                p.Course.Description,
                p.Course.Status,
                InstructorName = p.Course.Instructor.FirstName + " " + p.Course.Instructor.LastName,
                p.EnrolledAt,
                p.LastActivityAt,
                p.CurrentBlockId,
                TotalBlocks = context.CourseBlocks.Count(b => b.Subchapter.Chapter.CourseId == p.CourseId),
                CompletedBlocks = context.StudentBlockProgresses.Count(bp =>
                    bp.StudentId == studentId
                    && bp.IsCompleted
                    && bp.Block.Subchapter.Chapter.CourseId == p.CourseId)
            })
            .ToListAsync();

        var items = rows.Select(r => new EnrolledCourseDto
        {
            CourseId = r.CourseId,
            CourseTitle = r.Title,
            CourseDescription = r.Description,
            CourseStatus = r.Status,
            InstructorName = r.InstructorName,
            EnrolledAt = r.EnrolledAt,
            LastActivityAt = r.LastActivityAt,
            CurrentBlockId = r.CurrentBlockId,
            CompletedBlocksCount = r.CompletedBlocks,
            TotalBlocksCount = r.TotalBlocks,
            ProgressPercentage = r.TotalBlocks > 0
                ? Math.Round((double)r.CompletedBlocks / r.TotalBlocks * 100, 2)
                : 0
        }).ToList();

        return new PagedResult<EnrolledCourseDto>
        {
            Items = items,
            Page = pageNumber,
            PageSize = size,
            TotalCount = totalCount
        };
    }

    public async Task<bool> IsEnrolledAsync(Guid courseId, Guid studentId)
    {
        return await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == courseId && p.StudentId == studentId);
    }
}
