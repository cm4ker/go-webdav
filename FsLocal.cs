using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace WebDav
{
    public class LocalFileSystem : IFileSystem
    {
        private readonly string _root;

        public LocalFileSystem(string root)
        {
            _root = root;
        }

        private string LocalPath(string name)
        {
            if (Path.DirectorySeparatorChar != '/' && name.Contains(Path.DirectorySeparatorChar) || name.Contains('\0'))
            {
                throw new WebDavException(HttpStatusCode.BadRequest, "Invalid character in path");
            }

            name = Path.GetFullPath(Path.Combine(_root, name.TrimStart('/')));
            if (!name.StartsWith(_root))
            {
                throw new WebDavException(HttpStatusCode.BadRequest, "Expected absolute path");
            }

            return name;
        }

        private string ExternalPath(string name)
        {
            return "/" + Path.GetRelativePath(_root, name).Replace(Path.DirectorySeparatorChar, '/');
        }

        public async Task<Stream> OpenAsync(string name)
        {
            var path = LocalPath(name);
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private FileInfo FileInfoFromOS(string path, System.IO.FileInfo fi)
        {
            return new FileInfo
            {
                Path = path,
                Size = fi.Length,
                ModTime = fi.LastWriteTimeUtc,
                IsDir = (fi.Attributes & FileAttributes.Directory) != 0,
                MIMEType = MimeMapping.GetMimeMapping(path),
                ETag = $"{fi.LastWriteTimeUtc.Ticks:x}{fi.Length:x}"
            };
        }

        private Exception ErrFromOS(Exception ex)
        {
            if (ex is FileNotFoundException)
            {
                return new WebDavException(HttpStatusCode.NotFound, ex.Message);
            }
            else if (ex is UnauthorizedAccessException)
            {
                return new WebDavException(HttpStatusCode.Forbidden, ex.Message);
            }
            else if (ex is IOException)
            {
                return new WebDavException(HttpStatusCode.ServiceUnavailable, ex.Message);
            }
            else
            {
                return ex;
            }
        }

        public async Task<FileInfo> StatAsync(string name)
        {
            var path = LocalPath(name);
            var fi = new System.IO.FileInfo(path);
            if (!fi.Exists)
            {
                throw ErrFromOS(new FileNotFoundException());
            }
            return FileInfoFromOS(name, fi);
        }

        public async Task<IEnumerable<FileInfo>> ReadDirAsync(string name, bool recursive)
        {
            var path = LocalPath(name);
            var files = new List<FileInfo>();

            foreach (var file in Directory.EnumerateFileSystemEntries(path, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                var fi = new System.IO.FileInfo(file);
                files.Add(FileInfoFromOS(ExternalPath(file), fi));
            }

            return files;
        }

        public async Task<(FileInfo, bool)> CreateAsync(string name, Stream body, CreateOptions options)
        {
            var path = LocalPath(name);
            var fi = await StatAsync(name);
            var created = fi == null;
            var etag = fi?.ETag ?? "";

            if (options.IfMatch.IsSet && !options.IfMatch.MatchETag(etag))
            {
                throw new WebDavException(HttpStatusCode.PreconditionFailed, "If-Match condition failed");
            }
            if (options.IfNoneMatch.IsSet && options.IfNoneMatch.MatchETag(etag))
            {
                throw new WebDavException(HttpStatusCode.PreconditionFailed, "If-None-Match condition failed");
            }

            using (var wc = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await body.CopyToAsync(wc);
            }

            fi = await StatAsync(name);
            return (fi, created);
        }

        public async Task RemoveAllAsync(string name)
        {
            var path = LocalPath(name);

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                throw ErrFromOS(new FileNotFoundException());
            }

            Directory.Delete(path, true);
        }

        public async Task MkdirAsync(string name)
        {
            var path = LocalPath(name);
            Directory.CreateDirectory(path);
        }

        private async Task CopyRegularFileAsync(string src, string dst, FileAttributes perm)
        {
            using (var srcFile = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var dstFile = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await srcFile.CopyToAsync(dstFile);
            }
        }

        public async Task<bool> CopyAsync(string src, string dst, CopyOptions options)
        {
            var srcPath = LocalPath(src);
            var dstPath = LocalPath(dst);

            var srcInfo = new System.IO.FileInfo(srcPath);
            if (!srcInfo.Exists)
            {
                throw ErrFromOS(new FileNotFoundException());
            }
            var srcPerm = srcInfo.Attributes;

            var created = !File.Exists(dstPath);
            if (!created && options.NoOverwrite)
            {
                throw new WebDavException(HttpStatusCode.PreconditionFailed, "File already exists");
            }

            if (Directory.Exists(dstPath))
            {
                Directory.Delete(dstPath, true);
            }

            foreach (var file in Directory.EnumerateFileSystemEntries(srcPath, "*", SearchOption.AllDirectories))
            {
                var fi = new System.IO.FileInfo(file);
                var destFile = Path.Combine(dstPath, Path.GetRelativePath(srcPath, file));

                if (fi.Attributes.HasFlag(FileAttributes.Directory))
                {
                    Directory.CreateDirectory(destFile);
                }
                else
                {
                    await CopyRegularFileAsync(file, destFile, srcPerm);
                }

                if (!options.Recursive && fi.Attributes.HasFlag(FileAttributes.Directory))
                {
                    break;
                }
            }

            return created;
        }

        public async Task<bool> MoveAsync(string src, string dst, MoveOptions options)
        {
            var srcPath = LocalPath(src);
            var dstPath = LocalPath(dst);

            var created = !File.Exists(dstPath);
            if (!created && options.NoOverwrite)
            {
                throw new WebDavException(HttpStatusCode.PreconditionFailed, "File already exists");
            }

            if (Directory.Exists(dstPath))
            {
                Directory.Delete(dstPath, true);
            }

            File.Move(srcPath, dstPath);
            return created;
        }
    }
}
