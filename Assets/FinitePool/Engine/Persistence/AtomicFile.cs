using System.IO;
using System.Text;

namespace FinitePool.Engine.Persistence
{
    /// <summary>
    /// Crash-safe file writes (brief §3.3). A file is never overwritten in place:
    ///   1. write the new content to "name.tmp" and flush it to disk,
    ///   2. swap it in with File.Replace, which keeps the old file as "name.bak".
    /// A crash before step 2 leaves the old file untouched. A crash during step 2
    /// leaves "name.bak". <see cref="Recover"/> puts it back, so a reader always
    /// sees either the complete old file or the complete new one.
    /// </summary>
    internal static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string TempPath(string path) => path + ".tmp";

        public static string BackupPath(string path) => path + ".bak";

        public static void WriteAllText(string path, string contents)
        {
            string tempPath = TempPath(path);
            byte[] bytes = Utf8NoBom.GetBytes(contents);

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, BackupPath(path), ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        /// <summary>The file's text after any recovery, or null when it doesn't exist.</summary>
        public static string ReadAllText(string path)
        {
            Recover(path);
            return File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null;
        }

        /// <summary>
        /// Undoes an interrupted write. A missing file with a backup means the swap
        /// stopped halfway: restore the backup (the last committed version). A leftover
        /// temp file was never committed, so it is deleted.
        /// </summary>
        public static void Recover(string path)
        {
            string backupPath = BackupPath(path);
            if (!File.Exists(path) && File.Exists(backupPath))
            {
                File.Move(backupPath, path);
            }

            string tempPath = TempPath(path);
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
