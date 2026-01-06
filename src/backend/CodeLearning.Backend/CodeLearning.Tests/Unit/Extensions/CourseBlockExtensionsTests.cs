using CodeLearning.Application.Extensions;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Extensions;

public class CourseBlockExtensionsTests
{
    [Fact]
    public void TheoryBlock_ToResponseDto_ShouldMapCorrectly()
    {
        // Arrange
        var theoryContent = new TheoryContent
        {
            Id = Guid.NewGuid(),
            Content = "# Markdown content",
            Block = null!
        };

        var block = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Introduction",
            Type = BlockType.Theory,
            OrderIndex = 1,
            SubchapterId = Guid.NewGuid(),
            Subchapter = null!,
            TheoryContent = theoryContent
        };

        // Act
        var result = block.ToResponseDto();

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(block.Id);
        result.Title.Should().Be("Introduction");
        result.Type.Should().Be(BlockType.Theory);
        result.OrderIndex.Should().Be(1);
        result.TheoryContent.Should().NotBeNull();
        result.TheoryContent!.Content.Should().Be("# Markdown content");
        result.VideoContent.Should().BeNull();
        result.Quiz.Should().BeNull();
        result.Problem.Should().BeNull();
    }

    [Fact]
    public void VideoBlock_ToResponseDto_ShouldMapCorrectly()
    {
        // Arrange
        var videoContent = new VideoContent
        {
            Id = Guid.NewGuid(),
            VideoUrl = "https://www.youtube.com/watch?v=test",
            VideoId = "test1234567",
            Block = null!
        };

        var block = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Tutorial Video",
            Type = BlockType.Video,
            OrderIndex = 2,
            SubchapterId = Guid.NewGuid(),
            Subchapter = null!,
            VideoContent = videoContent
        };

        // Act
        var result = block.ToResponseDto();

        // Assert
        result.Type.Should().Be(BlockType.Video);
        result.VideoContent.Should().NotBeNull();
        result.VideoContent!.VideoId.Should().Be("test1234567");
        result.TheoryContent.Should().BeNull();
        result.Quiz.Should().BeNull();
        result.Problem.Should().BeNull();
    }

    [Fact]
    public void QuizBlock_ToResponseDto_ShouldMapCorrectly()
    {
        // Arrange
        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            Block = null!,
            Questions = new List<QuizQuestion>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Content = "Q1",
                    Type = QuestionType.SingleChoice,
                    OrderIndex = 1,
                    Quiz = null!,
                    Answers = new List<QuizAnswer>
                    {
                        new() { Id = Guid.NewGuid(), Text = "A", IsCorrect = true, OrderIndex = 1, Question = null! }
                    }
                }
            }
        };

        var block = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "C# Quiz",
            Type = BlockType.Quiz,
            OrderIndex = 3,
            SubchapterId = Guid.NewGuid(),
            Subchapter = null!,
            Quiz = quiz
        };

        // Act
        var result = block.ToResponseDto();

        // Assert
        result.Type.Should().Be(BlockType.Quiz);
        result.Quiz.Should().NotBeNull();
        result.Quiz!.Questions.Should().HaveCount(1);
        result.TheoryContent.Should().BeNull();
        result.VideoContent.Should().BeNull();
        result.Problem.Should().BeNull();
    }

    [Fact]
    public void ProblemBlock_ToResponseDto_ShouldMapCorrectly()
    {
        // Arrange
        var problem = new Problem
        {
            Id = Guid.NewGuid(),
            Title = "Two Sum",
            Description = "Problem description",
            Difficulty = DifficultyLevel.Medium,
            AuthorId = Guid.NewGuid(),
            Author = null!
        };

        var block = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Problem: Two Sum",
            Type = BlockType.Problem,
            OrderIndex = 4,
            SubchapterId = Guid.NewGuid(),
            Subchapter = null!,
            Problem = problem,
            ProblemId = problem.Id
        };

        // Act
        var result = block.ToResponseDto();

        // Assert
        result.Type.Should().Be(BlockType.Problem);
        result.Problem.Should().NotBeNull();
        result.Problem!.Title.Should().Be("Two Sum");
        result.Problem.Difficulty.Should().Be("Medium");
        result.TheoryContent.Should().BeNull();
        result.VideoContent.Should().BeNull();
        result.Quiz.Should().BeNull();
    }

    [Fact]
    public void ToResponseDto_NullBlock_ShouldThrowArgumentNullException()
    {
        // Arrange
        CourseBlock? block = null;

        // Act
        var act = () => block!.ToResponseDto();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToResponseDtos_MultipleBlocks_ShouldMapAll()
    {
        // Arrange
        var blocks = new List<CourseBlock>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Block 1",
                Type = BlockType.Theory,
                OrderIndex = 1,
                SubchapterId = Guid.NewGuid(),
                Subchapter = null!,
                TheoryContent = new TheoryContent { Id = Guid.NewGuid(), Content = "C1", Block = null! }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Block 2",
                Type = BlockType.Video,
                OrderIndex = 2,
                SubchapterId = Guid.NewGuid(),
                Subchapter = null!,
                VideoContent = new VideoContent { Id = Guid.NewGuid(), VideoUrl = "url", VideoId = "id", Block = null! }
            }
        };

        // Act
        var result = blocks.ToResponseDtos().ToList();

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Block 1");
        result[0].Type.Should().Be(BlockType.Theory);
        result[1].Title.Should().Be("Block 2");
        result[1].Type.Should().Be(BlockType.Video);
    }

    [Fact]
    public void ToResponseDtos_EmptyCollection_ShouldReturnEmpty()
    {
        // Arrange
        var blocks = new List<CourseBlock>();

        // Act
        var result = blocks.ToResponseDtos().ToList();

        // Assert
        result.Should().BeEmpty();
    }
}
