﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SwiftPbo
{
    public enum PackingType
    {
        Uncompressed,
        Packed
    };

    public class PboArchive
    {
        private ProductEntry _productEntry = new ProductEntry("", "", "", new List<string>());
        private List<FileEntry> _files = new List<FileEntry>();
        private string _path;
        private long _dataStart;
        private MemoryStream _memory;
        private byte[] _checksum;

        public static Boolean Create(string directoryPath, string outpath, ProductEntry productEntry)
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var entries = (from file in files select new FileInfo(file) into info let path = PboUtilities.GetRelativePath(info.FullName, directoryPath) select new FileEntry(path, 0x0, 0x0, (ulong) (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds, (ulong) info.Length)).ToList();
            try
            {
                using (var stream = File.Create(outpath))
                {
                    stream.WriteByte(0x0);
                    WriteProductEntry(productEntry, stream);
                    stream.WriteByte(0x0);
                    entries.Add(new FileEntry(null, "", 0, 0, 0, 0));
                    foreach (var entry in entries)
                    {
                        WriteFileEntry(stream, entry);
                    }
                    entries.Remove(entries.Last());
                    foreach (var entry in entries)
                    {
                        var buffer = new byte[16384];
                        using (var open = File.OpenRead(Path.Combine(directoryPath, entry.FileName)))
                        {
                            var read = 4324324;
                            while (read > 0)
                            {
                                read = open.Read(buffer, 0, buffer.Length);
                                stream.Write(buffer, 0, read);
                            }
                        }
                    }
                    stream.Position = 0;
                    byte[] hash;
                    using (var sha1 = new SHA1Managed())
                    {
                        hash = sha1.ComputeHash(stream);
                    }
                    stream.WriteByte(0x0);
                    stream.Write(hash, 0, 20);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
            return true;
        }

        public static void Clone(string path, ProductEntry productEntry, Dictionary<FileEntry, string> files, Byte[] checksum = null)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)) && !String.IsNullOrEmpty(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path) );
                using (var stream = File.Create(path))
                {
                    stream.WriteByte(0x0);
                    WriteProductEntry(productEntry, stream);
                    stream.WriteByte(0x0);
                    files.Add(new FileEntry(null, "", 0, 0, 0, 0, 0), "");
                    foreach (var entry in files.Keys)
                    {
                        WriteFileEntry(stream, entry);
                    }
                    files.Remove(files.Last().Key);
                    foreach (var file in files.Values)
                    {
                        var buffer = new byte[16384];
                        var len = new FileInfo(file).Length;
                        using (var open = File.OpenRead(file))
                        {
                            int bytesRead;
                            while ((bytesRead =
                                         open.Read(buffer, 0, 16384)) > 0)
                            {
                                stream.Write(buffer, 0, bytesRead);
                            }
                        }
                    }
                    if (checksum != null)
                    {
                        stream.WriteByte(0x0);
                        stream.Write(checksum, 0, checksum.Length);
                    }
                    else
                    {
                        stream.Position = 0;
                        byte[] hash;
                        using (var sha1 = new SHA1Managed())
                        {
                            hash = sha1.ComputeHash(stream);
                        }
                        stream.WriteByte(0x0);
                        stream.Write(hash, 0, 20);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static void WriteFileEntry(FileStream stream, FileEntry entry)
        {
            PboUtilities.WriteString(stream, entry.FileName);
            long packing = 0x0;
            switch (entry.PackingMethod)
            {
                case PackingType.Packed:
                    packing = 0x43707273;
                    break;
            }
            PboUtilities.WriteLong(stream, packing);
            PboUtilities.WriteLong(stream, (long)entry.OriginalSize);
            PboUtilities.WriteLong(stream, 0x0); // reserved
            PboUtilities.WriteLong(stream, (long)entry.TimeStamp);
            PboUtilities.WriteLong(stream, (long)entry.DataSize);
        }

        private static void WriteProductEntry(ProductEntry productEntry, FileStream stream)
        {
            PboUtilities.WriteString(stream, "sreV");
            stream.Write(new byte[15], 0, 15);
            if (!String.IsNullOrEmpty(productEntry.Prefix))
                PboUtilities.WriteString(stream, productEntry.Prefix);
            else
                return;
            if (!String.IsNullOrEmpty(productEntry.ProductName))
                PboUtilities.WriteString(stream, productEntry.ProductName);
            else
                return;
            if (!String.IsNullOrEmpty(productEntry.ProductVersion))
                PboUtilities.WriteString(stream, productEntry.ProductVersion);
            else
                return;
            foreach (var str in productEntry.Addtional)
            {
                PboUtilities.WriteString(stream, str);
            }
        }

        public PboArchive(String path, Boolean loadIntoMemory = false)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("File not Found");
            _path = path;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    if (stream.ReadByte() != 0x0)
                        return;
                    if (!ReadHeader(stream))
                        stream.Position = 0;
                    while (true)
                    {
                        if (!ReadEntry(stream))
                            break;
                    }
                    _dataStart = stream.Position;
                    ReadChecksum(stream);
                    if (!loadIntoMemory) return;
                    long length = stream.Length - (_dataStart + 20);
                    var buffer = new byte[length];
                    stream.Read(buffer, 0, (int)length);
                    _memory = new MemoryStream(buffer, true);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void ReadChecksum(FileStream stream)
        {
            var pos = _dataStart + Files.Sum(fileEntry => (long)fileEntry.DataSize) + 1;
            stream.Position = pos;
            _checksum = new byte[20];
            stream.Read(Checksum, 0, 20);
            stream.Position = _dataStart;
        }

        public List<FileEntry> Files
        {
            get { return _files; }
        }

        public ProductEntry ProductEntry
        {
            get { return _productEntry; }
        }

        public byte[] Checksum
        {
            get { return _checksum; }
        }

        public string PboPath
        {
            get { return _path; }
        }

        private bool ReadEntry(FileStream stream)
        {
            var filename = PboUtilities.ReadString(stream);

            var packing = PboUtilities.ReadLong(stream);

            var size = PboUtilities.ReadLong(stream);

            var unknown = PboUtilities.ReadLong(stream);

            var timestamp = PboUtilities.ReadLong(stream);
            var datasize = PboUtilities.ReadLong(stream);
            var entry = new FileEntry(this, filename, packing, size, timestamp, datasize, unknown);
            if (entry.FileName == "") return false;
            Files.Add(entry);
            return true;
        }

        private Boolean ReadHeader(FileStream stream)
        {
            // TODO FIX SO BROKEN
            var str = PboUtilities.ReadString(stream);
            if (str != "sreV")
                return false;
            int count = 0;
            while (count < 15)
            {
                stream.ReadByte();
                count++;
            }
            var prefix = "";
            var list = new List<string>();
            var pboname = "";
            var version = "";
            prefix = PboUtilities.ReadString(stream);
            if (!String.IsNullOrEmpty(prefix))
            {
                pboname = PboUtilities.ReadString(stream);
                if (!String.IsNullOrEmpty(pboname))
                {
                    version = PboUtilities.ReadString(stream);

                    if (!String.IsNullOrEmpty(version))
                    {
                        while (stream.ReadByte() != 0x0)
                        {
                            stream.Position--;
                            var s = PboUtilities.ReadString(stream);
                            list.Add(s);
                        }
                    }
                }
            }
            _productEntry = new ProductEntry(prefix, pboname, version, list);

            return true;
        }

        public Boolean ExtractAll(string outpath)
        {
            if (!Directory.Exists(outpath))
                Directory.CreateDirectory(outpath);
            using (var stream = GetFileStream(Files.First()))
            {
                foreach (var file in Files)
                {
                    ulong totalread = file.DataSize;
                    var pboPath = Path.Combine(outpath, file.FileName.Replace('\\',Path.DirectorySeparatorChar));
                    if (!Directory.Exists(Path.GetDirectoryName(pboPath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(pboPath));
                    using (var outfile = File.Create(pboPath))
                    {
                        while (totalread > 0)
                        {
                            var buffer = new byte[16384];
                            var read = stream.Read(buffer, 0, (int)Math.Min(16384, totalread));
                            outfile.Write(buffer,0,read);
                            totalread -= (ulong) read;
                        }
                    }
                }
            }
            return true;
        }

        public Boolean Extract(FileEntry fileEntry, string outpath)
        {
            if(String.IsNullOrEmpty(outpath))
                throw new NullReferenceException("Is null or empty");
            Stream mem = GetFileStream(fileEntry);
            if (mem == null)
                throw new Exception("WTF no stream");
            if (!Directory.Exists(Path.GetDirectoryName(outpath)))
                Directory.CreateDirectory(Path.GetDirectoryName(outpath));
            var totalread = fileEntry.DataSize;
            using (var outfile = File.OpenWrite(outpath))
            {
                while (totalread > 0)
                {
                    var buffer = new byte[16384];
                    var read = mem.Read(buffer, 0, (int)Math.Min(16384, totalread));
                    outfile.Write(buffer, 0, read);
                    totalread -= (ulong)read;
                }
            }
            mem.Close();
            return true;
        }

        private Stream GetFileStream(FileEntry fileEntry)
        {
            Stream mem;
            if (_memory != null)
                mem = ExtractMemory(fileEntry);
            else
            {
                mem = File.OpenRead(PboPath);
                mem.Position = (long)GetFileStreamPos(fileEntry);
            }
            return mem;
        }

        private ulong GetFileStreamPos(FileEntry fileEntry)
        {
            var start = (ulong)_dataStart;
            return Files.TakeWhile(entry => entry != fileEntry).Aggregate(start, (current, entry) => current + entry.DataSize);
        }

        private long GetFileMemPos(FileEntry fileEntry)
        {
            ulong start = Files.TakeWhile(entry => entry != fileEntry).Aggregate<FileEntry, ulong>(0, (current, entry) => current + entry.DataSize);
            return (long)start;
        }

        private Stream ExtractMemory(FileEntry fileEntry)
        {
            var buffer = new byte[fileEntry.DataSize];
            _memory.Position = GetFileMemPos(fileEntry);
            _memory.Read(buffer, 0, buffer.Length);
            _memory.Position = 0;
            return new MemoryStream(buffer);
        }

        // returns a stream
        public Stream Extract(FileEntry fileEntry)
        {
            var stream = GetFileStream(fileEntry);
            if (stream is MemoryStream)
                return stream;
            using (stream)
            {
                var mem = new MemoryStream((int)fileEntry.DataSize);
                var buffer = new byte[fileEntry.DataSize];
                stream.Read(buffer, 0, buffer.Length);
                mem.Write(buffer, 0, buffer.Length);
                mem.Position = 0;
                return mem;
            }
        }
    }
}