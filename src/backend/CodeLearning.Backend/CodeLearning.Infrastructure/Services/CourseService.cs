using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Extensions;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class CourseService(ApplicationDbContext context) : ICourseService
{
    public async Task<CourseResponseDto> CreateCourseAsync(CreateCourseDto dto, Guid instructorId)
    {
        var instructor = await context.Users.FindAsync(instructorId)
            ?? throw new BusinessRuleException("Instructor not found");

        var course = new Course
        {
            Title = dto.Title,
            Description = dto.Description,
            Status = CourseStatus.Draft,
            InstructorId = instructorId,
            Instructor = instructor
        };

        context.Courses.Add(course);
        await context.SaveChangesAsync();

        return await GetResponseAsync(course.Id);
    }

    public async Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId, Guid? viewerId = null)
    {
        return await ProjectToResponse(context.Courses.VisibleTo(viewerId).Where(c => c.Id == courseId))
                   .FirstOrDefaultAsync()
               ?? throw new NotFoundException($"Course with ID {courseId} not found");
    }

    // Public outline: titles, block types and order only. Block content needs enrollment (see BlockService)
    public async Task<CourseStructureDto> GetCourseStructureAsync(Guid courseId, Guid? viewerId = null)
    {
        var course = await context.Courses
            .AsNoTracking()
            .VisibleTo(viewerId)
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Subchapters)
                    .ThenInclude(s => s.Blocks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        var totalBlocks = course.Chapters
            .SelectMany(ch => ch.Subchapters)
            .SelectMany(s => s.Blocks)
            .Count();

        return new CourseStructureDto
        {
            CourseId = course.Id,
            CourseTitle = course.Title,
            CourseDescription = course.Description,
            TotalBlocksCount = totalBlocks,
            Chapters = course.Chapters
                .OrderBy(ch => ch.OrderIndex)
                .Select(ch => new ChapterStructureDto
                {
                    ChapterId = ch.Id,
                    Title = ch.Title,
                    OrderIndex = ch.OrderIndex,
                    Subchapters = ch.Subchapters
                        .OrderBy(s => s.OrderIndex)
                        .Select(s => new SubchapterStructureDto
                        {
                            SubchapterId = s.Id,
                            Title = s.Title,
                            OrderIndex = s.OrderIndex,
                            BlocksCount = s.Blocks.Count,
                            Blocks = s.Blocks
                                .OrderBy(b => b.OrderIndex)
                                .Select(b => new BlockStructureDto
                                {
                                    BlockId = b.Id,
                                    Title = b.Title,
                                    Type = b.Type,
                                    OrderIndex = b.OrderIndex
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public async Task<IEnumerable<CourseResponseDto>> GetInstructorCoursesAsync(Guid instructorId)
    {
        return await ProjectToResponse(context.Courses
                .Where(c => c.InstructorId == instructorId)
                .OrderByDescending(c => c.CreatedAt))
            .ToListAsync();
    }

    public async Task<TeacherStatsDto> GetInstructorStatsAsync(Guid instructorId)
    {
        var countsByStatus = await context.Courses
            .Where(c => c.InstructorId == instructorId)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalStudents = await context.StudentCourseProgresses
            .Where(p => p.Course.InstructorId == instructorId)
            .Select(p => p.StudentId)
            .Distinct()
            .CountAsync();

        return new TeacherStatsDto
        {
            PublishedCourses = countsByStatus.Where(c => c.Status == CourseStatus.Published).Sum(c => c.Count),
            DraftCourses = countsByStatus.Where(c => c.Status == CourseStatus.Draft).Sum(c => c.Count),
            TotalStudents = totalStudents
        };
    }

    public async Task<PagedResult<CourseResponseDto>> GetPublishedCoursesAsync(int? page = null, int? pageSize = null)
    {
        var (pageNumber, size) = PagedResult<CourseResponseDto>.Normalize(page, pageSize);

        var published = context.Courses.Where(c => c.Status == CourseStatus.Published);
        var totalCount = await published.CountAsync();

        var items = await ProjectToResponse(published
                .OrderByDescending(c => c.PublishedAt)
                .ThenBy(c => c.Id)
                .Skip((pageNumber - 1) * size)
                .Take(size))
            .ToListAsync();

        return new PagedResult<CourseResponseDto>
        {
            Items = items,
            Page = pageNumber,
            PageSize = size,
            TotalCount = totalCount
        };
    }

    public async Task<CourseResponseDto> UpdateCourseAsync(Guid courseId, UpdateCourseDto dto, Guid instructorId)
    {
        await using (var scope = await CourseEditScope.BeginAsync(context, courseId))
        {
            var course = await LoadOwnedCourseAsync(courseId, instructorId, "You can only update your own courses");

            if (course.Status == CourseStatus.Published)
            {
                throw new BusinessRuleException("Cannot update a published course");
            }

            course.Title = dto.Title;
            course.Description = dto.Description;

            await context.SaveChangesAsync();
            await scope.CommitAsync();
        }

        return await GetResponseAsync(courseId);
    }

    public async Task<CourseResponseDto> PublishCourseAsync(Guid courseId, Guid instructorId)
    {
        await using (var scope = await CourseEditScope.BeginAsync(context, courseId))
        {
            var course = await LoadOwnedCourseAsync(courseId, instructorId, "You can only publish your own courses");

            if (course.Status != CourseStatus.Published)
            {
                await ValidateCourseForPublishingAsync(courseId);

                course.Status = CourseStatus.Published;
                course.PublishedAt = DateTimeOffset.UtcNow;

                await context.SaveChangesAsync();
                await scope.CommitAsync();
            }
        }

        return await GetResponseAsync(courseId);
    }

    // Enrollments and progress are kept; enrolled students lose access to the content until the course is
    // published again, and the instructor can edit it in the meantime
    public async Task<CourseResponseDto> UnpublishCourseAsync(Guid courseId, Guid instructorId)
    {
        await using (var scope = await CourseEditScope.BeginAsync(context, courseId))
        {
            var course = await LoadOwnedCourseAsync(courseId, instructorId, "You can only unpublish your own courses");

            if (course.Status != CourseStatus.Draft)
            {
                course.Status = CourseStatus.Draft;
                course.PublishedAt = null;

                await context.SaveChangesAsync();
                await scope.CommitAsync();
            }
        }

        return await GetResponseAsync(courseId);
    }

    public async Task DeleteCourseAsync(Guid courseId, Guid instructorId)
    {
        await using var scope = await CourseEditScope.BeginAsync(context, courseId);

        var course = await LoadOwnedCourseAsync(courseId, instructorId, "You can only delete your own courses");

        if (course.Status == CourseStatus.Published)
        {
            throw new BusinessRuleException("Cannot delete a published course");
        }

        await BlockContentCleanup.RemoveForBlocksAsync(context, b => b.Subchapter.Chapter.CourseId == courseId);

        context.Courses.Remove(course);
        await context.SaveChangesAsync();
        await scope.CommitAsync();
    }

    private async Task<Course> LoadOwnedCourseAsync(Guid courseId, Guid instructorId, string forbiddenMessage)
    {
        var course = await context.Courses.FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException($"Course with ID {courseId} not found");

        if (course.InstructorId != instructorId)
        {
            throw new ForbiddenException(forbiddenMessage);
        }

        return course;
    }

    private async Task ValidateCourseForPublishingAsync(Guid courseId)
    {
        var chapters = await context.Chapters
            .AsNoTracking()
            .Where(ch => ch.CourseId == courseId)
            .OrderBy(ch => ch.OrderIndex)
            .Select(ch => new
            {
                ch.Title,
                Subchapters = ch.Subchapters
                    .OrderBy(s => s.OrderIndex)
                    .Select(s => new { s.Title, BlocksCount = s.Blocks.Count })
                    .ToList()
            })
            .ToListAsync();

        // A problem block is only solvable with at least one test to grade against and starter code in a
        // language students can submit in
        var problemBlocks = await context.CourseBlocks
            .AsNoTracking()
            .Where(b => b.Subchapter.Chapter.CourseId == courseId && b.Type == BlockType.Problem)
            .OrderBy(b => b.Subchapter.Chapter.OrderIndex)
                .ThenBy(b => b.Subchapter.OrderIndex)
                .ThenBy(b => b.OrderIndex)
            .Select(b => new
            {
                b.Title,
                HasProblem = b.ProblemId != null,
                HasTestCases = b.Problem != null && b.Problem.TestCases.Any(),
                HasEnabledStarterCode = b.Problem != null && b.Problem.StarterCodes.Any(sc => sc.Language.IsEnabled)
            })
            .ToListAsync();

        var validationErrors = new List<string>();

        if (chapters.Count == 0)
        {
            validationErrors.Add("Course must have at least one chapter");
        }

        foreach (var chapter in chapters)
        {
            if (chapter.Subchapters.Count == 0)
            {
                validationErrors.Add($"Chapter '{chapter.Title}' must have at least one subchapter");
            }

            validationErrors.AddRange(chapter.Subchapters
                .Where(subchapter => subchapter.BlocksCount == 0)
                .Select(subchapter => $"Subchapter '{subchapter.Title}' in chapter '{chapter.Title}' must have at least one block"));
        }

        foreach (var block in problemBlocks)
        {
            if (!block.HasProblem)
            {
                validationErrors.Add($"Problem block '{block.Title}' has no problem attached");
                continue;
            }

            if (!block.HasTestCases)
            {
                validationErrors.Add($"Problem in block '{block.Title}' must have at least one test case");
            }

            if (!block.HasEnabledStarterCode)
            {
                validationErrors.Add($"Problem in block '{block.Title}' must have starter code in at least one enabled language");
            }
        }

        if (validationErrors.Count > 0)
        {
            throw new BusinessRuleException($"Cannot publish course: {string.Join(", ", validationErrors)}");
        }
    }

    private async Task<CourseResponseDto> GetResponseAsync(Guid courseId) =>
        await ProjectToResponse(context.Courses.Where(c => c.Id == courseId)).FirstAsync();

    // Counts are computed in SQL, so listing courses never loads their chapters, subchapters or blocks
    private IQueryable<CourseResponseDto> ProjectToResponse(IQueryable<Course> courses) =>
        courses.Select(c => new CourseResponseDto
        {
            Id = c.Id,
            Title = c.Title,
            Description = c.Description,
            Status = c.Status,
            InstructorId = c.InstructorId,
            InstructorName = c.Instructor.FirstName + " " + c.Instructor.LastName,
            CreatedAt = c.CreatedAt,
            PublishedAt = c.PublishedAt,
            ChaptersCount = c.Chapters.Count,
            TotalBlocks = context.CourseBlocks.Count(b => b.Subchapter.Chapter.CourseId == c.Id)
        });
}
