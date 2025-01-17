﻿using SharedLibrary;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Vanara.Extensions;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace AuxiliaryTrustProcess.Class
{
    public static class RecycleBinController
    {
        public static IReadOnlyList<IDictionary<string, string>> GetRecycleItems()
        {
            ConcurrentBag<Dictionary<string, string>> RecycleItemList = new ConcurrentBag<Dictionary<string, string>>();

            Parallel.ForEach(RecycleBin.GetItems(), (Item) =>
            {
                try
                {
                    Dictionary<string, string> PropertyDic = new Dictionary<string, string>(4)
                    {
                        { "ActualPath", Item.FileSystemPath }
                    };

                    try
                    {
                        if (Item.IShellItem is Shell32.IShellItem2 Shell2)
                        {
                            PropertyDic.Add("DeleteTime", Shell2.GetFileTime(Ole32.PROPERTYKEY.System.Recycle.DateDeleted).ToInt64().ToString());
                        }
                        else
                        {
                            throw new Exception();
                        }
                    }
                    catch (Exception)
                    {
                        PropertyDic.Add("DeleteTime", Convert.ToString(default(FILETIME).ToInt64()));
                    }

                    if (File.Exists(Item.FileSystemPath))
                    {
                        PropertyDic.Add("StorageType", "File");

                        if (Path.GetExtension(Item.Name).Equals(Item.FileInfo.Extension, StringComparison.OrdinalIgnoreCase))
                        {
                            PropertyDic.Add("OriginPath", Item.Name);
                        }
                        else
                        {
                            PropertyDic.Add("OriginPath", Item.Name + Item.FileInfo.Extension);
                        }

                        RecycleItemList.Add(PropertyDic);
                    }
                    else if (Directory.Exists(Item.FileSystemPath))
                    {
                        PropertyDic.Add("OriginPath", Item.Name);
                        PropertyDic.Add("StorageType", "Folder");

                        try
                        {
                            if (Item.IShellItem is Shell32.IShellItem2 Shell2)
                            {
                                PropertyDic.Add("Size", Convert.ToString(Shell2.GetUInt64(Ole32.PROPERTYKEY.System.Size)));
                            }
                            else
                            {
                                throw new Exception();
                            }
                        }
                        catch (Exception)
                        {
                            PropertyDic.Add("Size", "0");
                        }

                        RecycleItemList.Add(PropertyDic);
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"An exception was threw when fetching recycle bin, name: {Item.Name}, path: {Item.FileSystemPath}");
                }
                finally
                {
                    Item.Dispose();
                }
            });

            return RecycleItemList.ToArray();
        }

        public static bool EmptyRecycleBin()
        {
            try
            {
                RecycleBin.Empty(false);
                return true;
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(EmptyRecycleBin)}");
            }

            return false;
        }

        public static ShellItem GetItemFromOriginPath(string OriginPath)
        {
            ArgumentNullException.ThrowIfNull(OriginPath);

            IReadOnlyDictionary<string, ShellItem> PathItemMapping = GetAllPathItemMapping();

            try
            {
                if (PathItemMapping.TryGetValue(OriginPath, out ShellItem Item))
                {
                    return new ShellItem(Item.FileSystemPath);
                }

                return null;
            }
            finally
            {
                foreach (ShellItem Item in PathItemMapping.Values)
                {
                    Item.Dispose();
                }
            }
        }

        public static bool Restore(IEnumerable<string> OriginPathList)
        {
            IReadOnlyDictionary<string, ShellItem> PathItemMapping = GetAllPathItemMapping();

            try
            {
                bool HasError = false;

                foreach (string OriginPath in OriginPathList)
                {
                    if (PathItemMapping.TryGetValue(OriginPath, out ShellItem SourceItem))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(SourceItem.Name));

                        if (File.Exists(SourceItem.FileSystemPath))
                        {
                            File.Move(SourceItem.FileSystemPath, Helper.GenerateUniquePathOnLocal(OriginPath, CreateType.File));
                        }
                        else if (Directory.Exists(SourceItem.FileSystemPath))
                        {
                            Directory.Move(SourceItem.FileSystemPath, Helper.GenerateUniquePathOnLocal(OriginPath, CreateType.Folder));
                        }

                        string ExtraInfoPath = Path.Combine(Path.GetDirectoryName(SourceItem.FileSystemPath), Path.GetFileName(SourceItem.FileSystemPath).Replace("$R", "$I"));

                        if (File.Exists(ExtraInfoPath))
                        {
                            File.Delete(ExtraInfoPath);
                        }
                    }
                    else
                    {
                        HasError = true;
                    }
                }

                return !HasError;
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(Restore)}");
                return false;
            }
            finally
            {
                foreach (ShellItem Item in PathItemMapping.Values)
                {
                    Item.Dispose();
                }
            }
        }

        public static bool Delete(string Path)
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
                else if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
                else
                {
                    throw new FileNotFoundException(Path);
                }

                string ExtraInfoFilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), System.IO.Path.GetFileName(Path).Replace("$R", "$I"));

                if (File.Exists(ExtraInfoFilePath))
                {
                    File.Delete(ExtraInfoFilePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw in {nameof(Delete)}");
            }

            return false;
        }

        private static IReadOnlyDictionary<string, ShellItem> GetAllPathItemMapping()
        {
            Dictionary<string, ShellItem> PathItemMapping = new Dictionary<string, ShellItem>();

            try
            {
                foreach (ShellItem Item in RecycleBin.GetItems())
                {
                    if (File.Exists(Item.FileSystemPath))
                    {
                        if (Path.GetExtension(Item.Name).Equals(Item.FileInfo.Extension, StringComparison.OrdinalIgnoreCase))
                        {
                            PathItemMapping.TryAdd(Item.Name, Item);
                        }
                        else
                        {
                            PathItemMapping.TryAdd(Item.Name + Item.FileInfo.Extension, Item);
                        }
                    }
                    else if (Directory.Exists(Item.FileSystemPath))
                    {
                        PathItemMapping.TryAdd(Item.Name, Item);
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the path item mapping in recycle bin");
            }

            return PathItemMapping;
        }
    }
}
