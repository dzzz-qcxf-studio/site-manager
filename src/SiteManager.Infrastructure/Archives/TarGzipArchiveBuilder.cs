using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using SiteManager.Core.Publishing;

namespace SiteManager.Infrastructure.Archives;

public sealed class TarGzipArchiveBuilder : IArchiveBuilder
{
    private const int StreamBufferSize = 1024 * 1024;

    public async Task<ArchiveResult> BuildAsync(
        string sourceDirectory,
        string outputPath,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.GetFullPath(sourceDirectory);
        var archivePath = Path.GetFullPath(outputPath);
        ValidatePaths(sourcePath, archivePath);

        var files = EnumerateFiles(sourcePath, cancellationToken);
        var outputCreated = false;

        try
        {
            long sourceBytes = 0;
            await using (var output = new FileStream(
                             archivePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             StreamBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                outputCreated = true;
                await using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
                using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);

                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNotReparsePoint(filePath);

                    var entryName = Path.GetRelativePath(sourcePath, filePath)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    await using var input = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        StreamBufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                    {
                        DataStream = input
                    };

                    await writer.WriteEntryAsync(entry, cancellationToken);
                    sourceBytes = checked(sourceBytes + input.Length);
                    progress?.Report(sourceBytes);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            await using var archive = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(archive, cancellationToken);

            return new ArchiveResult(
                archivePath,
                sourceBytes,
                archive.Length,
                Convert.ToHexString(hash).ToLowerInvariant());
        }
        catch
        {
            if (outputCreated)
            {
                File.Delete(archivePath);
            }

            throw;
        }
    }

    private static void ValidatePaths(string sourcePath, string archivePath)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourcePath}");
        }

        EnsureNotReparsePoint(sourcePath);

        var sourcePrefix = sourcePath.EndsWith(Path.DirectorySeparatorChar)
            ? sourcePath
            : sourcePath + Path.DirectorySeparatorChar;
        if (archivePath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(archivePath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The archive output cannot be inside the source directory.", nameof(archivePath));
        }
    }

    private static IReadOnlyList<string> EnumerateFiles(string sourcePath, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(sourcePath);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = directories.Pop();
            EnsureNotReparsePoint(current);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Archive source contains a reparse point: {entry}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(
            Path.GetRelativePath(sourcePath, left).Replace(Path.DirectorySeparatorChar, '/'),
            Path.GetRelativePath(sourcePath, right).Replace(Path.DirectorySeparatorChar, '/')));
        return files;
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Archive source contains a reparse point: {path}");
        }
    }
}
