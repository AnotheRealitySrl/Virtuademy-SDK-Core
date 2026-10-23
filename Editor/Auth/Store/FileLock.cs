using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// An exclusive lock between processes: a lock file opened with <see cref="FileShare.None"/>. The
    /// operating system releases it when the process that holds it dies, which a stale lock record would
    /// not survive. Not a <see cref="Mutex"/>, which belongs to the thread that took it: MSAL may call
    /// its before and after cache hooks on different threads.
    /// </summary>
    internal sealed class FileLock : IDisposable
    {
        private FileStream stream;

        private FileLock(FileStream stream)
        {
            this.stream = stream;
        }

        /// <summary>The lock, or null when another process still holds it after <paramref name="timeoutMs"/>.</summary>
        public static FileLock TryAcquire(string path, int timeoutMs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (true)
            {
                try
                {
                    return new FileLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow >= deadline)
                        return null;

                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException)
                {
                    // A lock file that is being deleted or replaced reads as access denied for a moment.
                    if (DateTime.UtcNow >= deadline)
                        return null;

                    Thread.Sleep(25);
                }
            }
        }

        /// <summary>
        /// <see cref="TryAcquire"/> that waits for the other process with <c>Task.Delay</c>, so the editor's main thread
        /// is never held up behind it.
        /// </summary>
        public static async Task<FileLock> TryAcquireAsync(string path, int timeoutMs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (true)
            {
                try
                {
                    return new FileLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (DateTime.UtcNow >= deadline)
                        return null;

                    await Task.Delay(25);
                }
            }
        }

        public void Dispose()
        {
            try { stream?.Dispose(); } catch (IOException) { }
            stream = null;
        }
    }
}
