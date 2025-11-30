using CodeLearning.Application.Extensions;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Extensions;

public class YouTubeExtensionsTests
{
    #region ExtractYouTubeVideoId Tests

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=ABC123DEF45", "ABC123DEF45")]
    [InlineData("https://youtu.be/ABC123DEF45", "ABC123DEF45")]
    public void ExtractYouTubeVideoId_ValidUrls_ShouldReturnVideoId(string url, string expectedVideoId)
    {
        // Act
        var result = url.ExtractYouTubeVideoId();

        // Assert
        result.Should().Be(expectedVideoId);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123")]  // Too short (6 chars)
    [InlineData("https://example.com/video")]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractYouTubeVideoId_InvalidUrls_ShouldReturnNull(string url)
    {
        // Act
        var result = url?.ExtractYouTubeVideoId();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&feature=share")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLxyz")]
    public void ExtractYouTubeVideoId_WithQueryParams_ShouldExtractVideoId(string url)
    {
        // Act
        var result = url.ExtractYouTubeVideoId();

        // Assert
        result.Should().Be("dQw4w9WgXcQ");
    }

    [Fact]
    public void ExtractYouTubeVideoId_CaseInsensitive_ShouldWork()
    {
        // Arrange
        var url = "HTTPS://WWW.YOUTUBE.COM/WATCH?V=dQw4w9WgXcQ";

        // Act
        var result = url.ExtractYouTubeVideoId();

        // Assert
        result.Should().Be("dQw4w9WgXcQ");
    }

    #endregion

    #region IsValidYouTubeUrl Tests

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=ABC123DEF45")]
    public void IsValidYouTubeUrl_ValidUrls_ShouldReturnTrue(string url)
    {
        // Act
        var result = url.IsValidYouTubeUrl();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123")] // Too short
    [InlineData("https://example.com/video")]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidYouTubeUrl_InvalidUrls_ShouldReturnFalse(string url)
    {
        // Act
        var result = url.IsValidYouTubeUrl();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ToYouTubeEmbedUrl Tests

    [Fact]
    public void ToYouTubeEmbedUrl_ValidVideoId_ShouldReturnEmbedUrl()
    {
        // Arrange
        var videoId = "dQw4w9WgXcQ";

        // Act
        var result = videoId.ToYouTubeEmbedUrl();

        // Assert
        result.Should().Be("https://www.youtube.com/embed/dQw4w9WgXcQ");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ToYouTubeEmbedUrl_EmptyOrNullVideoId_ShouldThrowException(string videoId)
    {
        // Act
        var act = () => videoId.ToYouTubeEmbedUrl();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw-4w9WgXcQ")] // Hyphen in ID
    [InlineData("https://www.youtube.com/watch?v=dQw_4w9WgXc")]  // Underscore in ID
    public void ExtractYouTubeVideoId_SpecialCharactersInId_ShouldExtract(string url)
    {
        // Act
        var result = url.ExtractYouTubeVideoId();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveLength(11);
    }

    [Fact]
    public void ExtractYouTubeVideoId_WhitespaceUrl_ShouldReturnNull()
    {
        // Arrange
        var url = "   ";

        // Act
        var result = url.ExtractYouTubeVideoId();

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
