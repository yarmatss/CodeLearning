using CodeLearning.Runner.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace CodeLearning.Runner.Tests.Unit.Services;

public class SubmissionQueueTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<ILogger<SubmissionQueue>> _loggerMock;
    private readonly SubmissionQueue _queue;

    public SubmissionQueueTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<SubmissionQueue>>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _queue = new SubmissionQueue(_redisMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task EnqueueAsync_ValidGuid_ShouldCallRedisPush()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        _databaseMock.Setup(db => db.ListRightPushAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _queue.EnqueueAsync(submissionId);

        // Assert
        _databaseMock.Verify(db => db.ListRightPushAsync(
            It.Is<RedisKey>(k => k == "submissions:pending"),
            It.Is<RedisValue>(v => v == submissionId.ToString()),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WhenRedisThrows_ShouldThrowException()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        _databaseMock.Setup(db => db.ListRightPushAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() => _queue.EnqueueAsync(submissionId));
    }

    [Fact]
    public async Task DequeueAsync_WhenValueExists_ShouldReturnGuid()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        _databaseMock.Setup(db => db.ListLeftPopAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(expectedId.ToString());

        // Act
        var result = await _queue.DequeueAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedId, result);

        _databaseMock.Verify(db => db.ListLeftPopAsync(
            It.Is<RedisKey>(k => k == "submissions:pending"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task DequeueAsync_WhenQueueEmpty_ShouldReturnNull()
    {
        // Arrange
        _databaseMock.Setup(db => db.ListLeftPopAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _queue.DequeueAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DequeueAsync_WhenInvalidGuid_ShouldReturnNull()
    {
        // Arrange
        _databaseMock.Setup(db => db.ListLeftPopAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync("invalid-guid");

        // Act
        var result = await _queue.DequeueAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DequeueAsync_WhenRedisThrows_ShouldReturnNull()
    {
        // Arrange
        _databaseMock.Setup(db => db.ListLeftPopAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var result = await _queue.DequeueAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetQueueLengthAsync_ShouldReturnLength()
    {
        // Arrange
        const long expectedLength = 5;
        _databaseMock.Setup(db => db.ListLengthAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(expectedLength);

        // Act
        var result = await _queue.GetQueueLengthAsync();

        // Assert
        Assert.Equal(expectedLength, result);

        _databaseMock.Verify(db => db.ListLengthAsync(
            It.Is<RedisKey>(k => k == "submissions:pending"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetQueueLengthAsync_WhenRedisThrows_ShouldReturn0()
    {
        // Arrange
        _databaseMock.Setup(db => db.ListLengthAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var result = await _queue.GetQueueLengthAsync();

        // Assert
        Assert.Equal(0, result);
    }
}
