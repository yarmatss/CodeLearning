using CodeLearning.Application.Extensions;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Extensions;

public class BlockContentExtensionsTests
{
    #region TheoryContentExtensions Tests

    [Fact]
    public void TheoryContent_ToDto_ShouldMapCorrectly()
    {
        // Arrange
        var content = new TheoryContent
        {
            Id = Guid.NewGuid(),
            Content = "# Hello World\n\nThis is **Markdown**.",
            Block = null!
        };

        // Act
        var result = content.ToDto();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(content.Id);
        result.Content.Should().Be(content.Content);
    }

    [Fact]
    public void TheoryContent_Null_ToDtoShouldReturnNull()
    {
        // Arrange
        TheoryContent? content = null;

        // Act
        var result = content.ToDto();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region VideoContentExtensions Tests

    [Fact]
    public void VideoContent_ToDto_ShouldMapCorrectly()
    {
        // Arrange
        var content = new VideoContent
        {
            Id = Guid.NewGuid(),
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            VideoId = "dQw4w9WgXcQ",
            DurationSeconds = 212,
            Block = null!
        };

        // Act
        var result = content.ToDto();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(content.Id);
        result.VideoUrl.Should().Be(content.VideoUrl);
        result.VideoId.Should().Be(content.VideoId);
        result.DurationSeconds.Should().Be(212);
    }

    [Fact]
    public void VideoContent_WithoutDuration_ToDtoShouldMapWithNull()
    {
        // Arrange
        var content = new VideoContent
        {
            Id = Guid.NewGuid(),
            VideoUrl = "https://youtu.be/ABC123DEF45",
            VideoId = "ABC123DEF45",
            DurationSeconds = null,
            Block = null!
        };

        // Act
        var result = content.ToDto();

        // Assert
        result.Should().NotBeNull();
        result!.DurationSeconds.Should().BeNull();
    }

    [Fact]
    public void VideoContent_Null_ToDtoShouldReturnNull()
    {
        // Arrange
        VideoContent? content = null;

        // Act
        var result = content.ToDto();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region QuizExtensions Tests

    [Fact]
    public void Quiz_ToDto_ShouldMapCorrectly()
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
                    Content = "What is C#?",
                    Type = QuestionType.SingleChoice,
                    OrderIndex = 1,
                    Quiz = null!,
                    Answers = new List<QuizAnswer>
                    {
                        new() { Id = Guid.NewGuid(), Text = "Language", IsCorrect = true, OrderIndex = 1, Question = null! },
                        new() { Id = Guid.NewGuid(), Text = "Database", IsCorrect = false, OrderIndex = 2, Question = null! }
                    }
                }
            }
        };

        // Act
        var result = quiz.ToDto();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(quiz.Id);
        result.Questions.Should().HaveCount(1);
        
        var question = result.Questions.First();
        question.Content.Should().Be("What is C#?");
        question.Type.Should().Be("SingleChoice");
        question.Points.Should().Be(0); // Hidden from students
        question.Answers.Should().HaveCount(2);
    }

    [Fact]
    public void QuizQuestion_ToDto_ShouldHideIsCorrect()
    {
        // Arrange
        var question = new QuizQuestion
        {
            Id = Guid.NewGuid(),
            Content = "Question?",
            Type = QuestionType.MultipleChoice,
            OrderIndex = 1,
            Quiz = null!,
            Answers = new List<QuizAnswer>
            {
                new() { Id = Guid.NewGuid(), Text = "Answer 1", IsCorrect = true, OrderIndex = 1, Question = null! },
                new() { Id = Guid.NewGuid(), Text = "Answer 2", IsCorrect = false, OrderIndex = 2, Question = null! }
            }
        };

        // Act
        var result = question.ToDto();

        // Assert
        result.Answers.Should().HaveCount(2);
        
        // Verify IsCorrect is NOT in DTO
        result.Answers.Should().AllSatisfy(a =>
        {
            a.Text.Should().NotBeNullOrEmpty();
            a.Id.Should().NotBeEmpty();
            // IsCorrect property doesn't exist in QuizAnswerDto!
        });
    }

    [Fact]
    public void QuizAnswer_ToDto_ShouldMapWithoutIsCorrect()
    {
        // Arrange
        var answer = new QuizAnswer
        {
            Id = Guid.NewGuid(),
            Text = "Test Answer",
            IsCorrect = true, // This should NOT be in DTO!
            OrderIndex = 1,
            Question = null!
        };

        // Act
        var result = answer.ToDto();

        // Assert
        result.Id.Should().Be(answer.Id);
        result.Text.Should().Be("Test Answer");
        result.OrderIndex.Should().Be(1);
        // IsCorrect is NOT exposed!
    }

    [Fact]
    public void Quiz_Null_ToDtoShouldReturnNull()
    {
        // Arrange
        Quiz? quiz = null;

        // Act
        var result = quiz.ToDto();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Quiz_WithMultipleQuestions_ShouldOrderByOrderIndex()
    {
        // Arrange
        var quiz = new Quiz
        {
            Id = Guid.NewGuid(),
            Block = null!,
            Questions = new List<QuizQuestion>
            {
                new() { Id = Guid.NewGuid(), Content = "Q3", Type = QuestionType.TrueFalse, OrderIndex = 3, Quiz = null!, Answers = new List<QuizAnswer>() },
                new() { Id = Guid.NewGuid(), Content = "Q1", Type = QuestionType.SingleChoice, OrderIndex = 1, Quiz = null!, Answers = new List<QuizAnswer>() },
                new() { Id = Guid.NewGuid(), Content = "Q2", Type = QuestionType.MultipleChoice, OrderIndex = 2, Quiz = null!, Answers = new List<QuizAnswer>() }
            }
        };

        // Act
        var result = quiz.ToDto();

        // Assert
        result!.Questions.Should().HaveCount(3);
        result.Questions[0].Content.Should().Be("Q1");
        result.Questions[1].Content.Should().Be("Q2");
        result.Questions[2].Content.Should().Be("Q3");
    }

    #endregion

    #region ProblemExtensions Tests

    [Fact]
    public void Problem_ToDto_ShouldMapCorrectly()
    {
        // Arrange
        var problem = new Problem
        {
            Id = Guid.NewGuid(),
            Title = "Two Sum",
            Description = "Find two numbers...",
            Difficulty = DifficultyLevel.Easy,
            AuthorId = Guid.NewGuid(),
            Author = null!
        };

        // Act
        var result = problem.ToDto();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(problem.Id);
        result.Title.Should().Be("Two Sum");
        result.Description.Should().Be("Find two numbers...");
        result.Difficulty.Should().Be("Easy");
    }

    [Theory]
    [InlineData(DifficultyLevel.Easy, "Easy")]
    [InlineData(DifficultyLevel.Medium, "Medium")]
    [InlineData(DifficultyLevel.Hard, "Hard")]
    public void Problem_ToDto_ShouldMapDifficulty(DifficultyLevel difficulty, string expected)
    {
        // Arrange
        var problem = new Problem
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Description = "Desc",
            Difficulty = difficulty,
            AuthorId = Guid.NewGuid(),
            Author = null!
        };

        // Act
        var result = problem.ToDto();

        // Assert
        result!.Difficulty.Should().Be(expected);
    }

    [Fact]
    public void Problem_Null_ToDtoShouldReturnNull()
    {
        // Arrange
        Problem? problem = null;

        // Act
        var result = problem.ToDto();

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
