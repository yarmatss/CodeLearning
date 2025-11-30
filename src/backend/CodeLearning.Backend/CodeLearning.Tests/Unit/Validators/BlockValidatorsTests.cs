using CodeLearning.Application.DTOs.Block;
using CodeLearning.Application.Validators.Block;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace CodeLearning.Tests.Unit.Validators;

public class BlockValidatorsTests
{
    #region CreateTheoryBlockDtoValidator Tests

    [Fact]
    public void TheoryBlockValidator_ValidDto_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new CreateTheoryBlockDtoValidator();
        var dto = new CreateTheoryBlockDto
        {
            Title = "Introduction to C#",
            Content = "# Hello World\n\nThis is Markdown."
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TheoryBlockValidator_EmptyTitle_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateTheoryBlockDtoValidator();
        var dto = new CreateTheoryBlockDto
        {
            Title = "",
            Content = "Content"
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Title)
            .WithErrorMessage("Title is required");
    }

    [Fact]
    public void TheoryBlockValidator_TitleTooLong_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateTheoryBlockDtoValidator();
        var dto = new CreateTheoryBlockDto
        {
            Title = new string('A', 201), // 201 characters
            Content = "Content"
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Title)
            .WithErrorMessage("Title must not exceed 200 characters");
    }

    [Fact]
    public void TheoryBlockValidator_EmptyContent_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateTheoryBlockDtoValidator();
        var dto = new CreateTheoryBlockDto
        {
            Title = "Title",
            Content = ""
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Content)
            .WithErrorMessage("Content is required");
    }

    [Fact]
    public void TheoryBlockValidator_ContentTooLong_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateTheoryBlockDtoValidator();
        var dto = new CreateTheoryBlockDto
        {
            Title = "Title",
            Content = new string('A', 50001) // 50,001 characters
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Content)
            .WithErrorMessage("Content must not exceed 50000 characters");
    }

    #endregion

    #region CreateVideoBlockDtoValidator Tests

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=ABC123DEF45")]
    public void VideoBlockValidator_ValidYouTubeUrl_ShouldNotHaveErrors(string videoUrl)
    {
        // Arrange
        var validator = new CreateVideoBlockDtoValidator();
        var dto = new CreateVideoBlockDto
        {
            Title = "Tutorial",
            VideoUrl = videoUrl
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("https://example.com/video")]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("not a url")]
    [InlineData("")]
    public void VideoBlockValidator_InvalidYouTubeUrl_ShouldHaveError(string videoUrl)
    {
        // Arrange
        var validator = new CreateVideoBlockDtoValidator();
        var dto = new CreateVideoBlockDto
        {
            Title = "Title",
            VideoUrl = videoUrl
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.VideoUrl);
    }

    [Fact]
    public void VideoBlockValidator_EmptyTitle_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateVideoBlockDtoValidator();
        var dto = new CreateVideoBlockDto
        {
            Title = "",
            VideoUrl = "https://www.youtube.com/watch?v=test1234567"
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    #endregion

    #region CreateQuizBlockDtoValidator Tests

    [Fact]
    public void QuizBlockValidator_ValidDto_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new CreateQuizBlockDtoValidator();
        var dto = new CreateQuizBlockDto
        {
            Title = "C# Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "What is C#?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "Language", IsCorrect = true },
                        new() { AnswerText = "Database", IsCorrect = false }
                    }
                }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void QuizBlockValidator_NoQuestions_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateQuizBlockDtoValidator();
        var dto = new CreateQuizBlockDto
        {
            Title = "Empty Quiz",
            Questions = new List<CreateQuizQuestionDto>()
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Questions)
            .WithErrorMessage("Quiz must have at least one question");
    }

    [Fact]
    public void QuizBlockValidator_TooManyQuestions_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateQuizBlockDtoValidator();
        var questions = Enumerable.Range(1, 51).Select(i => new CreateQuizQuestionDto
        {
            QuestionText = $"Q{i}",
            Type = "SingleChoice",
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true },
                new() { AnswerText = "B", IsCorrect = false }
            }
        }).ToList();

        var dto = new CreateQuizBlockDto
        {
            Title = "Big Quiz",
            Questions = questions
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Questions)
            .WithErrorMessage("Quiz cannot have more than 50 questions");
    }

    #endregion

    #region CreateQuizQuestionDtoValidator Tests

    [Fact]
    public void QuizQuestionValidator_ValidSingleChoice_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "What is 2+2?",
            Type = "SingleChoice",
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "3", IsCorrect = false },
                new() { AnswerText = "4", IsCorrect = true },
                new() { AnswerText = "5", IsCorrect = false }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void QuizQuestionValidator_SingleChoiceWithMultipleCorrect_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Question?",
            Type = "SingleChoice",
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true },
                new() { AnswerText = "B", IsCorrect = true } // Multiple correct!
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("SingleChoice question must have exactly one correct answer");
    }

    [Fact]
    public void QuizQuestionValidator_NoCorrectAnswer_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Question?",
            Type = "SingleChoice",
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = false },
                new() { AnswerText = "B", IsCorrect = false }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("Question must have at least one correct answer");
    }

    [Theory]
    [InlineData("SingleChoice")]
    [InlineData("MultipleChoice")]
    [InlineData("TrueFalse")]
    public void QuizQuestionValidator_ValidTypes_ShouldNotHaveErrors(string type)
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Q",
            Type = type,
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true },
                new() { AnswerText = "B", IsCorrect = false }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Type);
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("")]
    [InlineData(null)]
    public void QuizQuestionValidator_InvalidType_ShouldHaveError(string type)
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Q",
            Type = type,
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true },
                new() { AnswerText = "B", IsCorrect = false }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuizQuestionValidator_InvalidPoints_ShouldHaveError(int points)
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Q",
            Type = "SingleChoice",
            Points = points,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true },
                new() { AnswerText = "B", IsCorrect = false }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Points)
            .WithErrorMessage("Points must be greater than 0");
    }

    [Fact]
    public void QuizQuestionValidator_PointsOver100_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Q",
            Type = "SingleChoice",
            Points = 101,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true },
                new() { AnswerText = "B", IsCorrect = false }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Points)
            .WithErrorMessage("Points must not exceed 100");
    }

    [Fact]
    public void QuizQuestionValidator_LessThan2Answers_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateQuizQuestionDtoValidator();
        var dto = new CreateQuizQuestionDto
        {
            QuestionText = "Q",
            Type = "SingleChoice",
            Points = 10,
            Answers = new List<CreateQuizAnswerDto>
            {
                new() { AnswerText = "A", IsCorrect = true }
            }
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Answers)
            .WithErrorMessage("Question must have at least 2 answers");
    }

    #endregion

    #region CreateProblemBlockDtoValidator Tests

    [Fact]
    public void ProblemBlockValidator_ValidDto_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new CreateProblemBlockDtoValidator();
        var dto = new CreateProblemBlockDto
        {
            Title = "Two Sum Problem",
            ProblemId = Guid.NewGuid()
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ProblemBlockValidator_EmptyTitle_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateProblemBlockDtoValidator();
        var dto = new CreateProblemBlockDto
        {
            Title = "",
            ProblemId = Guid.NewGuid()
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void ProblemBlockValidator_EmptyProblemId_ShouldHaveError()
    {
        // Arrange
        var validator = new CreateProblemBlockDtoValidator();
        var dto = new CreateProblemBlockDto
        {
            Title = "Title",
            ProblemId = Guid.Empty
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProblemId);
    }

    #endregion
}
