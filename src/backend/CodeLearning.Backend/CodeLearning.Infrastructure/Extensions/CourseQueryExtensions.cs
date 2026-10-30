using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;

namespace CodeLearning.Infrastructure.Extensions;

public static class CourseQueryExtensions
{
    extension(IQueryable<Course> courses)
    {
        public IQueryable<Course> VisibleTo(Guid? viewerId) =>
            courses.Where(c => c.Status == CourseStatus.Published || c.InstructorId == viewerId);
    }

}
