using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Kurz.Compiler.Build;

/// <summary>A place could not be made: the system's message.</summary>
public sealed class PlaceException(string message) : Exception(message);

/// <summary>
/// The places of <c>kurz run</c> and <c>kurz exec</c> (design record, section 2): a directory of its own per run, under the
/// directory <c>kurz</c> in the user's cache directory, as Rust's <c>dirs</c> crate names it (the owner, 2026-10-29, "As Rust's
/// dirs"), made readable, writable and searchable by the user alone on Linux (the owner, 2026-10-29, "Owner only, 0700"). A
/// place holds a lock file, which the command that made it holds the operating system's lock on, <c>flock</c>'s on Linux and
/// <c>LockFileEx</c>'s on Windows, with a mark written into it before anything else is written to the place, and opened so that
/// no child inherits it. A start removes every place whose lock it can take and whose lock file holds the mark, and leaves a
/// place whose lock file is missing or empty alone; a removal takes the other files and directories first and the lock file
/// last, and one that fails is passed over without a word (the owner, 2026-10-29, "Silent").
/// </summary>
public sealed class Place : IDisposable
{
    /// <summary>What every place's name starts with; a start reads no other directory of the command's.</summary>
    public const string Prefix = "place-";

    /// <summary>The name of the lock file in a place.</summary>
    public const string LockName = "lock";

    /// <summary>The mark a lock file holds once its place is the command's.</summary>
    public const string Mark = "kurz place 1\n";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly SafeFileHandle lockFile;

    private Place(string directory, SafeFileHandle lockFile)
    {
        Directory = directory;
        this.lockFile = lockFile;
    }

    /// <summary>The place's directory.</summary>
    public string Directory { get; }

    /// <summary>
    /// The directory of the places, <c>kurz</c> under the user's cache directory: on Linux the one <c>XDG_CACHE_HOME</c> names where
    /// it is an absolute path, else <c>.cache</c> under the home directory, <c>HOME</c> or the user database's entry, which .NET's
    /// folder of the user's profile reads in that order; on Windows the known folder LocalAppData. Null where there is none.
    /// </summary>
    public static string? Root() => Root(
        OperatingSystem.IsWindows(),
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>As above, from what the system gave: the variable, the home directory and the known folder, the last two empty where there is none.</summary>
    internal static string? Root(bool windows, string? cacheHome, string home, string localAppData)
    {
        if (windows)
        {
            return localAppData.Length > 0 ? Path.Combine(localAppData, "kurz") : null;
        }

        if (!string.IsNullOrEmpty(cacheHome) && Path.IsPathFullyQualified(cacheHome))
        {
            return Path.Combine(cacheHome, "kurz");
        }

        return home.Length > 0 ? Path.Combine(home, ".cache", "kurz") : null;
    }

    /// <summary>Makes a place of its own under <paramref name="root"/>: its directory, its lock file, the lock, which this call waits for, and the mark.</summary>
    public static Place Make(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var directory = Path.Combine(root, Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.IO.Directory.CreateDirectory(root);
                System.IO.Directory.CreateDirectory(directory);
            }
            else
            {
                System.IO.Directory.CreateDirectory(root, OwnerOnly);
                System.IO.Directory.CreateDirectory(directory, OwnerOnly);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PlaceException(e.Message);
        }

        SafeFileHandle? lockFile = null;
        try
        {
            lockFile = Open(Path.Combine(directory, LockName), create: true);
            Lock(lockFile, wait: true);
            RandomAccess.Write(lockFile, System.Text.Encoding.UTF8.GetBytes(Mark), 0);
            return new Place(directory, lockFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            lockFile?.Dispose();
            Remove(directory);
            throw new PlaceException(e.Message);
        }
    }

    /// <summary>Removes every place under <paramref name="root"/> whose lock this call can take and whose lock file holds the mark: the places a run cut short left.</summary>
    public static void RemoveLeft(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        IEnumerable<string> places;
        try
        {
            places = System.IO.Directory.EnumerateDirectories(root, Prefix + "*").ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var place in places)
        {
            bool left;
            try
            {
                using var lockFile = Open(Path.Combine(place, LockName), create: false);
                left = Lock(lockFile, wait: false) && Marked(lockFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
            {
                continue; // a lock file that is missing or cannot be opened: a command is making the place, or a run was cut short before its lock file
            }

            if (left)
            {
                Remove(place);
            }
        }
    }

    /// <summary>Closes the lock file, which lets the lock go, and removes the place.</summary>
    public void Dispose()
    {
        lockFile.Dispose();
        Remove(Directory);
    }

    /// <summary>Whether the lock file holds the mark and nothing else.</summary>
    private static bool Marked(SafeFileHandle lockFile)
    {
        var expected = System.Text.Encoding.UTF8.GetBytes(Mark);
        var buffer = new byte[expected.Length + 1];
        var read = RandomAccess.Read(lockFile, buffer, 0);
        return buffer.AsSpan(0, read).SequenceEqual(expected);
    }

    /// <summary>Removes the place, its other files and directories first and its lock file last; a removal that fails ends it without a word.</summary>
    private static void Remove(string place)
    {
        try
        {
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(place).ToList())
            {
                if (Path.GetFileName(entry) == LockName)
                {
                    continue;
                }

                if (System.IO.Directory.Exists(entry) && !File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                {
                    System.IO.Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            File.Delete(Path.Combine(place, LockName));
            System.IO.Directory.Delete(place);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the owner, 2026-10-29, "Silent": a start removes a place left with its marked lock file
        }
    }

    /// <summary>
    /// Opens a lock file for reading and writing so that no child inherits it: on Linux through <c>open</c> with <c>O_CLOEXEC</c>, since
    /// .NET's own open takes a lock of its own that does not wait; on Windows with delete sharing, so that another command's handle
    /// does not hold up a removal.
    /// </summary>
    private static SafeFileHandle Open(string path, bool create)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.OpenHandle(path, create ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }

        var fd = Native.Open(path, Native.ReadWrite | Native.CloseOnExec | (create ? Native.Create | Native.Exclusive : 0), Convert.ToUInt32("600", 8));
        if (fd < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new SafeFileHandle(fd, ownsHandle: true);
    }

    /// <summary>Takes the exclusive lock on the whole file, waiting for it or not; whether it was taken.</summary>
    private static bool Lock(SafeFileHandle file, bool wait)
    {
        if (OperatingSystem.IsWindows())
        {
            var overlapped = default(NativeOverlapped);
            if (Native.LockFileEx(file, Native.ExclusiveLock | (wait ? 0 : Native.FailImmediately), 0, 1, 0, ref overlapped))
            {
                return true;
            }

            var error = Marshal.GetLastPInvokeError();
            if (!wait && error == Native.LockViolation)
            {
                return false;
            }

            throw new Win32Exception(error);
        }

        while (true)
        {
            if (Native.Flock(file, Native.LockExclusive | (wait ? 0 : Native.LockNonBlocking)) == 0)
            {
                return true;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == Native.Interrupted)
            {
                continue;
            }

            if (!wait && error == Native.WouldBlock)
            {
                return false;
            }

            throw new Win32Exception(error);
        }
    }

    /// <summary>The system calls of the lock, as Linux on x86-64 and Windows number their flags.</summary>
    private static class Native
    {
        public const int ReadWrite = 2;
        public const int Create = 0x40;
        public const int Exclusive = 0x80;
        public const int CloseOnExec = 0x80000;
        public const int LockExclusive = 2;
        public const int LockNonBlocking = 4;
        public const int Interrupted = 4;
        public const int WouldBlock = 11;
        public const int FailImmediately = 1;
        public const int ExclusiveLock = 2;
        public const int LockViolation = 33;

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

        [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
        public static extern int Flock(SafeFileHandle file, int operation);

        [DllImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LockFileEx(SafeFileHandle file, int flags, int reserved, int lengthLow, int lengthHigh, ref NativeOverlapped overlapped);
    }
}
