﻿using System;
using System.IO;

namespace BlackTundra.Foundation.IO {

    /// <summary>
    /// References an object on the systems file system.
    /// </summary>
    public sealed class FileSystemReference : IEquatable<FileSystemReference> {

        #region variable

        /// <summary>
        /// Path to the file/directory.
        /// This will always be formatted propperly and will use <c>'/'</c> rather than <c>'\'</c>.
        /// </summary>
        private readonly string path;

        /// <summary>
        /// Tracks if the file system reference is local to the application or not.
        /// </summary>
        private readonly bool isLocal;

        /// <summary>
        /// Tracks if the file system reference is a directory (<c>true</c>) or a file (<c>false</c>).
        /// </summary>
        private readonly bool isDirectory;

        #endregion

        #region property

        /// <summary>
        /// Local path for the file system reference.
        /// If the reference is not a local reference this will return the absolute path.
        /// </summary>
        public string LocalPath => path;

        /// <summary>
        /// Absolute path for the file system reference.
        /// </summary>
        public string AbsolutePath => isLocal ? FileSystem.ToCanonicalPath(path, isDirectory) : path;

        /// <summary>
        /// <c>true</c> if the <see cref="FileSystemReference"/> is referencing a file.
        /// </summary>
        public bool IsFile => !isDirectory;

        /// <summary>
        /// <c>true</c> if the <see cref="FileSystemReference"/> is referencing a directory.
        /// </summary>
        public bool IsDirectory => isDirectory;

        /// <summary>
        /// <c>true</c> if the path is local to the application.
        /// </summary>
        public bool IsLocal => isLocal;

        /// <summary>
        /// Gets the file name with the file extension of this reference.
        /// </summary>
        public string FileName => isDirectory
            ? throw new NotSupportedException("Cannot get file name of a directory.")
            : Path.GetFileName(path);

        /// <summary>
        /// Gets the directory name of this reference.
        /// </summary>
        public string DirectoryName => isDirectory
            ? new DirectoryInfo(path).Name
            : throw new NotSupportedException("Cannot get directory name of a file.");

        /// <summary>
        /// Gets the file name without the file extension of this reference.
        /// </summary>
        public string FileNameWithoutExtension => isDirectory
            ? throw new NotSupportedException("Cannot get file name of directory.")
            : Path.GetFileNameWithoutExtension(path);

        /// <summary>
        /// Gets the file extension.
        /// </summary>
        public string FileExtension => isDirectory
            ? throw new NotSupportedException("Cannot get file extension of a directory.")
            : Path.GetExtension(path)[1..];

        #endregion

        #region constructor

        public FileSystemReference(string path, in bool isLocal, in bool isDirectory) {
            if (path == null) throw new ArgumentNullException(nameof(path));
            path = path.Replace('\\', '/');
            this.path = isDirectory && path[path.Length - 1] != '/' ? (path + '/') : path;
            this.isLocal = isLocal;
            this.isDirectory = isDirectory;
        }

        #endregion

        #region logic

        #region Delete

        /// <summary>
        /// Deletes the referenced file/directory from the system.
        /// This is not a recursive by default.
        /// </summary>
        public void Delete() {
            if (isDirectory) Directory.Delete(AbsolutePath);
            else File.Delete(AbsolutePath);
        }

        /// <summary>
        /// Deletes the referenced file/directory from the system.
        /// </summary>
        /// <param name="recursive">When <c>true</c>, children of a directory will be recursivly deleted when deleting a directory.</param>
        public void Delete(in bool recursive) {
            if (isDirectory) Directory.Delete(AbsolutePath, recursive);
            else File.Delete(AbsolutePath);
        }

        #endregion

        #region GetParent

        public FileSystemReference GetParent() {

            string path = AbsolutePath;
            for (int i = path.Length - 1; i >= 0; i--) {

                if (path[i] == '/') return new FileSystemReference(
                    path.Substring(0, i + 1),
                    false,
                    true
                );

            }

            return null; // failed to find parent directory
            //return new FileSystemReference("/", false, true);

        }

        #endregion

        #region ToDirectory

        public FileSystemReference ToDirectory() {

            if (isDirectory) return this; // already a directory
            return GetParent();

        }

        #endregion

        #region GetFiles

        public FileSystemReference[] GetFiles() {
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetFiles(AbsolutePath), false, false);
        }

        public FileSystemReference[] GetFiles(in string searchPattern) {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetFiles(AbsolutePath, searchPattern), false, false);
        }

        public FileSystemReference[] GetFiles(in string searchPattern, in EnumerationOptions enumerationOptions) {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            if (enumerationOptions == null) throw new ArgumentNullException(nameof(enumerationOptions));
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetFiles(AbsolutePath, searchPattern, enumerationOptions), false, false);
        }

        public FileSystemReference[] GetFiles(in string searchPattern, in SearchOption searchOptions) {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetFiles(AbsolutePath, searchPattern, searchOptions), false, false);
        }

        #endregion

        #region GetDirectories

        public FileSystemReference[] GetDirectories() {
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetDirectories(AbsolutePath), false, true);
        }

        public FileSystemReference[] GetDirectories(in string searchPattern) {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetDirectories(AbsolutePath, searchPattern), false, true);
        }

        public FileSystemReference[] GetDirectories(in string searchPattern, in EnumerationOptions enumerationOptions) {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            if (enumerationOptions == null) throw new ArgumentNullException(nameof(enumerationOptions));
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetDirectories(AbsolutePath, searchPattern, enumerationOptions), false, true);
        }

        public FileSystemReference[] GetDirectories(in string searchPattern, in SearchOption searchOptions) {
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));
            if (!isDirectory) throw new Exception($"{nameof(FileSystemReference)} is not a directory.");
            return ToFSRArray(Directory.GetDirectories(AbsolutePath, searchPattern, searchOptions), false, true);
        }

        #endregion

        #region ToFSRArray

        private FileSystemReference[] ToFSRArray(in string[] paths, in bool isLocal, in bool isDirectory) {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            int pathCount = paths.Length;
            FileSystemReference[] buffer = new FileSystemReference[pathCount];
            if (pathCount == 0) return buffer;
            for (int i = 0; i < pathCount; i++) {
                buffer[i] = new FileSystemReference(paths[i], isLocal, isDirectory);
            }
            return buffer;
        }

        #endregion

        #region ToString

        public sealed override string ToString() => AbsolutePath;

        #endregion

        #region Equals

        public bool Equals(FileSystemReference fsr) => fsr != null && AbsolutePath.Equals(fsr.AbsolutePath);

        #endregion

        #endregion

    }

}