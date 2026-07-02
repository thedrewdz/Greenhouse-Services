using Greenhouse.Core.Setup;

namespace Greenhouse.Core.Tests.Setup;

public class WriteMainConfigTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static WriteMainConfig Create(FakeMainConfigRepository repository) =>
        new(repository, new FixedTimeProvider(FixedNow));

    [Fact]
    public async Task Returns_Success_and_persists_with_timestamps()
    {
        var repository = new FakeMainConfigRepository();

        var result = await Create(repository).ExecuteAsync("North", "Block A", "desc");

        Assert.IsType<WriteMainConfigResult.Success>(result);
        Assert.NotNull(repository.Current);
        Assert.Equal("North", repository.Current!.GreenhouseName);
        Assert.Equal(FixedNow.UtcDateTime, repository.Current.CreatedAt);
        Assert.Equal(FixedNow.UtcDateTime, repository.Current.UpdatedAt);
    }

    [Fact]
    public async Task Returns_ValidationError_for_missing_name_and_does_not_persist()
    {
        var repository = new FakeMainConfigRepository();

        var result = await Create(repository).ExecuteAsync("  ", "Block A", null);

        var error = Assert.IsType<WriteMainConfigResult.ValidationError>(result);
        Assert.Equal("greenhouseName", error.Field);
        Assert.Equal(0, repository.CreateCount);
    }

    [Fact]
    public async Task Returns_ValidationError_for_oversized_name()
    {
        var repository = new FakeMainConfigRepository();
        var longName = new string('x', MainConfigValidation.GreenhouseNameMaxLength + 1);

        var result = await Create(repository).ExecuteAsync(longName, "Block A", null);

        var error = Assert.IsType<WriteMainConfigResult.ValidationError>(result);
        Assert.Equal("greenhouseName", error.Field);
    }

    [Fact]
    public async Task Returns_AlreadyExists_when_config_present_and_does_not_persist_again()
    {
        var repository = new FakeMainConfigRepository
        {
            Current = new Core.Configuration.MainConfig("Existing", "Loc", null, FixedNow.UtcDateTime, FixedNow.UtcDateTime),
        };

        var result = await Create(repository).ExecuteAsync("North", "Block A", null);

        Assert.IsType<WriteMainConfigResult.AlreadyExists>(result);
        Assert.Equal(0, repository.CreateCount);
    }
}
