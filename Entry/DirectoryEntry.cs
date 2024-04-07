﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VirtualDisk = ShellX.Disk.VirtualDisk;
using FatTable = ShellX.Disk.FatTable;

/*
------------------------------------------------------------------------------------------------------------------------
| FileName (11 bytes) | FileAttribute (1 byte) | FileEmpty (12 bytes) | FirstCluster (4 bytes) | FileSize (4 bytes)   |
-------------------------------------------------------------------------------------------------------------------------
|        Name [0,10]  |         Attr [11]      |   12 zeros [12,23]  | Cluster Number [24,27] | Size in Bytes [28,31] |
------------------------------------------------------------------------------------------------------------------------
 */

namespace ShellX.Entry
{
    public class DirectoryEntry
    {
        public string FileName { get; set; } = ""; // 11 bytes
        public byte FileAttribute { get; set; } // 1 byte
        public byte[] FileEmpty { get; set; } = new byte[12]; // 12 bytes
        public int FirstCluster { get; set; } // 4 bytes
        public int FileSize { get; set; } // 4 bytes
        public Directory Parent { get; set; }


        public DirectoryEntry() { }

        // ---- Constructors ----
        public DirectoryEntry(string name, byte attribute, int cluster, int size, Directory parent)
        {
            FileName = FormateFileName(name, attribute);
            FileAttribute = attribute;
            FileSize = size;
            FirstCluster = cluster;
            Parent = parent;
            // parent?.AddChild(this); // Causes Circular Dependency
        }

        public DirectoryEntry(DirectoryEntry entry, Directory parent) : this(entry.FileName, entry.FileAttribute, entry.FirstCluster, entry.FileSize, parent) { }

        public DirectoryEntry(byte[] data)
        {
            DirectoryEntry entry = MetaFromByteArray(data);
            FileName = entry.FileName;
            FileAttribute = entry.FileAttribute;
            FirstCluster = entry.FirstCluster;
            FileSize = entry.FileSize;
        }


        // ---- Meta Functions ----
        public byte[] MetaToByteArray()
        {
            byte[] data = new byte[32];
            Encoding.ASCII.GetBytes(FileName.PadRight(11)).CopyTo(data, 0);
            data[11] = FileAttribute;
            FileEmpty.CopyTo(data, 12);
            BitConverter.GetBytes(FirstCluster).CopyTo(data, 24);
            BitConverter.GetBytes(FileSize).CopyTo(data, 28);
            return data;
        }

        public DirectoryEntry MetaFromByteArray(byte[] data)
        {
            return new DirectoryEntry
            {
                FileName = Encoding.ASCII.GetString(data, 0, 11).Trim(),
                FileAttribute = data[11],
                FirstCluster = BitConverter.ToInt32(data, 24),
                FileSize = BitConverter.ToInt32(data, 28)
            };
        }

        // ------ Read Functions --------    
        public void ReadEntryFromDisk()
        {
            List<byte> entryBytes = ReadBytesFromDisk();
            ConvertBytesToContent(entryBytes);
        }
        public List<byte> ReadBytesFromDisk()
        {
            List<byte> entryBytes = new List<byte>();
            int currentCluster = FirstCluster;

            if (currentCluster < 5 || FatTable.GetValue(currentCluster) == 0)
                return entryBytes;

            while (currentCluster != -1)
            {
                byte[] blockData = VirtualDisk.ReadBlock(currentCluster);
                entryBytes.AddRange(blockData);
                currentCluster = FatTable.GetValue(currentCluster);
            }

            return entryBytes;
        }
        public virtual void ConvertBytesToContent(List<byte> data)
        {
            Console.WriteLine("Bytes To Content from Parent");
        }


        // ------ Write Functions --------
        public void WriteEntryToDisk()
        {
            List<byte> directoryBytes = ConvertContentToBytes();
            WriteBytesToDisk(directoryBytes);
        }
        public virtual List<byte> ConvertContentToBytes()
        {
            Console.WriteLine("Content To Bytes from Parent");
            return new List<byte>();
        }
        public void WriteBytesToDisk(List<byte> bytesToWrite)
        {
            ClearFat();
            if (bytesToWrite.Count > 0)
            {
                AllocateFirstCluster();
                List<int> fatIndex = new List<int>() { FirstCluster };
                int totalBytes = bytesToWrite.Count;
                int totalBlocks = (totalBytes + 1023) / 1024;
                for (int i = 0; i < totalBlocks; i++)
                {
                    int blockSize = Math.Min(1024, totalBytes - i * 1024);
                    byte[] blockData = bytesToWrite.Skip(i * 1024).Take(blockSize).ToArray();
                    if (i >= fatIndex.Count) fatIndex.Add(FatTable.GetAvailableBlock());
                    FatTable.SetValue(fatIndex[i], -1);
                    if (i > 0) FatTable.SetValue(fatIndex[i - 1], fatIndex[i]);
                    VirtualDisk.WriteBlock(blockData, fatIndex[i]);
                }
            }

        }
        // ------ Copy Functions --------
        public virtual DirectoryEntry CopyEntry(Directory newParent)
        {
            return new DirectoryEntry(FileName, FileAttribute, 0, FileSize, newParent);
        }

        // ------ Delete Functions --------
        public virtual void DeleteEntryFromDisk()
        {
            ClearFat();
            Parent?.RemoveChild(this);
        }

        // ------ General Functions --------
        public void ClearFat()
        {
            int currentIndex = FirstCluster;
            while (currentIndex != -1 && currentIndex != 0)
            {
                int nextIndex = FatTable.GetValue(currentIndex);
                FatTable.SetValue(currentIndex, 0);
                VirtualDisk.WriteBlock(VirtualDisk.GetEmptyBlock('#'), currentIndex);
                currentIndex = nextIndex;
            }
        }

        public void AllocateFirstCluster()
        {
            if (FirstCluster == 0)
            {
                FirstCluster = FatTable.GetAvailableBlock();
                FatTable.SetValue(FirstCluster, -1);
                Parent?.AddChild(this);
            }
        }

        public virtual void UpdateName(string name)
        {
            FileName = FormateFileName(name, FileAttribute);
            Parent?.WriteEntryToDisk();
        }

        public static string FormateFileName(string name, int attribute = 1)
        {
            name = name.Trim();
            if (name.Contains("."))
            {
                string[] parts = name.Split('.'); parts[1] = parts[1].Substring(0, Math.Min(3, parts[1].Length));
                return $"{parts[0].Substring(0, Math.Min(11 - parts[1].Length - 1, parts[0].Length))}.{parts[1]}";
            }
            return name.Substring(0, Math.Min(11, name.Length));
        }
    }
}

