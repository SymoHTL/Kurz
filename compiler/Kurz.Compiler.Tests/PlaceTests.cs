using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The places of <c>kurz run</c> and <c>kurz exec</c> (design record, section 2), each under a directory of the test's own: where
/// the directory of the places is, what a place is made of and who may read it, which places a start removes and which it leaves
/// alone, that no child inherits the lock file, and that a removal that fails is passed over.
/// </summary>
public sealed class PlaceTests : IDisposable
{
    private static readonly UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string root = Directory.CreateTempSubdirectory("kurz-place-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Places => Path.Combine(root, "kurz");

    [Fact]
    public void The_directory_of_the_places_is_kurz_in_the_cache_directory_rusts_dirs_names()
    {
        // the owner, 2026-10-29, "As Rust's dirs": XDG_CACHE_HOME where it is absolute, else .cache under the home; LocalAppData on Windows
        var cache = Path.Combine(root, "cache");
        var home = Path.Combine(root, "home");
        var local = Path.Combine(root, "local");
        Assert.Equal(Path.Combine(cache, "kurz"), Place.Root(false, cache, home, local));
        Assert.Equal(Path.Combine(home, ".cache", "kurz"), Place.Root(false, "cache", home, local)); // a relative path is passed over
        Assert.Equal(Path.Combine(home, ".cache", "kurz"), Place.Root(false, "", home, local));
        Assert.Equal(Path.Combine(home, ".cache", "kurz"), Place.Root(false, null, home, local));
        Assert.Null(Place.Root(false, "cache", "", local)); // no home, and LocalAppData is not Linux's
        Assert.Equal(Path.Combine(local, "kurz"), Place.Root(true, cache, home, local)); // the variables are not Windows'
        Assert.Null(Place.Root(true, cache, home, ""));
    }

    [Fact]
    public void A_place_is_a_directory_of_its_own_with_its_marked_lock_file_held_and_the_user_alone_reads_it()
    {
        string directory;
        using (var place = Place.Make(Places))
        {
            directory = place.Directory;
            Assert.Equal(Places, Path.GetDirectoryName(directory));
            Assert.Matches("^place-[0-9a-f]{32}$", Path.GetFileName(directory));
            var lockFile = Path.Combine(directory, Place.LockName);
            Assert.Equal([lockFile], Directory.EnumerateFileSystemEntries(directory));
            if (!OperatingSystem.IsWindows())
            {
                // .NET opens a file with a lock of its own, which the held lock refuses; Windows refuses another handle the locked byte
                Assert.Equal((0, "kurz place 1\n", ""), Processes.Capture("cat", [lockFile], null, TimeSpan.FromMinutes(1)));

                // the owner, 2026-10-29, "Owner only, 0700"
                Assert.Equal((OwnerOnly, OwnerOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite), (File.GetUnixFileMode(Places), File.GetUnixFileMode(directory), File.GetUnixFileMode(lockFile)));
            }

            Place.RemoveLeft(Places); // the lock is held: a start leaves the place alone
            Assert.True(Directory.Exists(directory));
            File.WriteAllText(Path.Combine(directory, "program"), "");
            Directory.CreateDirectory(Path.Combine(directory, "work", "deeper"));
        }

        Assert.False(Directory.Exists(directory), "the place stayed after its run");
        Assert.True(Directory.Exists(Places));
    }

    [Fact]
    public void A_start_removes_the_places_whose_lock_it_takes_and_whose_lock_file_holds_the_mark_and_no_other()
    {
        var left = Make("place-left", Place.Mark);
        File.WriteAllText(Path.Combine(left, "program"), "");
        Directory.CreateDirectory(Path.Combine(left, "work"));
        File.WriteAllText(Path.Combine(left, "work", "program.ll"), "");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "kept"), "");
        var linked = Path.Combine(root, "linked");
        Directory.CreateDirectory(linked);
        File.WriteAllText(Path.Combine(linked, Place.LockName), Place.Mark);
        File.WriteAllText(Path.Combine(linked, "kept"), "");
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(Path.Combine(left, "link"), outside); // a link is removed, never followed
            Directory.CreateSymbolicLink(Path.Combine(Places, "place-link"), linked); // a link among the places is never followed either
        }

        var empty = Make("place-empty", "");
        var unmarked = Make("place-other", "kurz place 2\n");
        var longer = Make("place-longer", Place.Mark + "x");
        var other = Make("other", Place.Mark);
        var noLock = Path.Combine(Places, "place-nolock");
        Directory.CreateDirectory(noLock);
        File.WriteAllText(Path.Combine(noLock, "program"), "");
        var file = Path.Combine(Places, "place-file");
        File.WriteAllText(file, Place.Mark);
        using var held = Place.Make(Places);

        Place.RemoveLeft(Places);

        Assert.False(Directory.Exists(left), "a left place stayed");
        Assert.True(File.Exists(Path.Combine(outside, "kept")));
        Assert.True(File.Exists(Path.Combine(linked, "kept")), "a link among the places was followed");
        Assert.All([empty, unmarked, longer, other, noLock, held.Directory], d => Assert.True(Directory.Exists(d), $"{d} was removed"));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void On_linux_no_child_inherits_the_lock_file()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // a child's handles are read from /proc: nothing checks it on Windows
        }

        using var place = Place.Make(Places);
        var lockFile = Path.Combine(place.Directory, Place.LockName);
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", "ls -l /proc/self/fd"], null, TimeSpan.FromMinutes(1));
        Assert.Equal((0, ""), (code, error));
        Assert.DoesNotContain(lockFile, output, StringComparison.Ordinal);
        var control = Processes.Capture("/bin/sh", ["-c", "exec 3<\"$0\"; ls -l /proc/self/fd", lockFile], null, TimeSpan.FromMinutes(1));
        Assert.Contains(lockFile, control.Output, StringComparison.Ordinal); // what an inherited file looks like in that list
    }

    [Fact]
    public void A_removal_that_fails_is_passed_over_and_the_next_start_removes_the_place()
    {
        // the owner, 2026-10-29, "Silent"
        var place = Place.Make(Places);
        var program = Path.Combine(place.Directory, "program");
        File.WriteAllText(program, "");
        var work = Path.Combine(place.Directory, "work");
        Directory.CreateDirectory(work);
        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                place.Dispose(); // the program's file is open without delete sharing, as a program that outlives its command holds it
            }
        }
        else
        {
            // a file whose name is no UTF-8: .NET reads its name otherwise and cannot remove it, which no permission makes for a root user
            // (knowledge/dotnet-cannot-remove-a-name-that-is-not-utf-8.md)
            Assert.Equal(0, Processes.Capture("/bin/sh", ["-c", "touch \"$0/$(printf '\\377')\"", work], null, TimeSpan.FromMinutes(1)).Code);
            place.Dispose();
            Assert.True(Directory.Exists(work), "the removal did not fail");
            Assert.Equal(0, Processes.Capture("/bin/sh", ["-c", "rm \"$0\"/*", work], null, TimeSpan.FromMinutes(1)).Code);
        }

        Assert.Equal("kurz place 1\n", File.ReadAllText(Path.Combine(place.Directory, Place.LockName))); // the removal failed, the lock file last
        Place.RemoveLeft(Places);
        Assert.False(Directory.Exists(place.Directory));
    }

    /// <summary>A directory of the given name under the places, with a lock file of the text given, as a run cut short leaves one.</summary>
    private string Make(string name, string lockText)
    {
        var directory = Path.Combine(Places, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, Place.LockName), lockText);
        return directory;
    }
}
