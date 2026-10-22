namespace KeepClip.Tests.Infrastructure;

public class ExplorerLauncherTests
{
    [Fact]
    public void Explorer_uses_the_windows_executable_even_with_a_local_namesake()
    {
        var directory = TestEnvironment.NewTempDir("explorer");
        var namesake = Path.Combine(directory, "explorer.exe");
        File.WriteAllText(namesake, "test fixture, not an executable");

        var startInfo = ExplorerLauncher.CreateStartInfo(namesake);

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), startInfo.FileName);
        Assert.True(Path.IsPathFullyQualified(startInfo.FileName));
        Assert.NotEqual(namesake, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Empty(startInfo.Arguments);
        Assert.Equal($"/select,{namesake}", Assert.Single(startInfo.ArgumentList));
    }

    [Theory]
    [InlineData("clip with spaces.mp4")]
    [InlineData("clip,comma & calc.exe.mp4")]
    [InlineData("zażółć.mp4")]
    public void File_names_are_passed_as_one_argument_without_a_shell(string filename)
    {
        var path = Path.Combine(TestEnvironment.NewTempDir("explorer-file"), filename);
        File.WriteAllText(path, "test");

        var startInfo = ExplorerLauncher.CreateStartInfo(path);

        Assert.Equal($"/select,{path}", Assert.Single(startInfo.ArgumentList));
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void Directories_are_opened_without_selecting_a_file()
    {
        var directory = TestEnvironment.NewTempDir("explorer-directory");

        Assert.Equal(directory, Assert.Single(ExplorerLauncher.CreateStartInfo(directory).ArgumentList));
    }

    [Fact]
    public void Relative_paths_are_resolved_before_passing_them_to_explorer()
    {
        var directory = TestEnvironment.NewTempDir("explorer-relative");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, directory);

        Assert.Equal(directory, Assert.Single(ExplorerLauncher.CreateStartInfo(relative).ArgumentList));
    }
}