using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace wsl_delegate
{
    class MappingDefinition
    {
        public readonly string winPath;
        public readonly string unixPath;
        public readonly DriveType driveType;
        public readonly bool mountable;
        public readonly bool wslNative;
        public bool mounted;
        public MappingDefinition(DriveType drvType, bool mountable, string winPath, string unixPath)
        {
            this.wslNative = false;
            this.driveType = drvType;
            this.mountable = mountable;
            this.mounted = false;
            this.winPath = winPath;
            this.unixPath = unixPath;
        }
        public MappingDefinition(string winPath, string unixPath)
        {
            this.wslNative = true;
            this.driveType = DriveType.Unknown;
            this.mountable = false;
            this.mounted = true;
            this.winPath = winPath;
            this.unixPath = unixPath;
        }
    }

    class MappingService
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int WNetGetConnection(
            [MarshalAs(UnmanagedType.LPTStr)] string localName,
            [MarshalAs(UnmanagedType.LPTStr)] StringBuilder remoteName,
            ref int length);

        public readonly List<MappingDefinition> pathMappings;
        public MappingService()
        {
            pathMappings = new List<MappingDefinition>();
            // All logic drives in Windows 
            foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
            {
                MappingDefinition mapdef;
                mapdef = new MappingDefinition(driveInfo.DriveType, driveInfo.IsReady, driveInfo.Name.Substring(0, 2).ToLower(), "/mnt/" + driveInfo.Name.Substring(0, 1).ToLower());
                pathMappings.Add(mapdef);
            }

            // Check if they are mounted in WSL
            NameValueCollection wslMounts = getWSLMounts();
            foreach (MappingDefinition mapdef in pathMappings)
            {
                foreach (string wslMountedWinPath in wslMounts.Keys)
                {
                    if (pathEquals(wslMountedWinPath, mapdef.winPath))
                    {
                        mapdef.mounted = true;
                    }
                    else
                    {
                        if (mapdef.driveType == DriveType.Network)
                        {
                            string networkPath = getNetDriveMappedPath(mapdef.winPath);
                            if (pathEquals(wslMountedWinPath, networkPath))
                            {
                                mapdef.mounted = true;
                            }
                        }
                    }
                }

            }

            // Add root directories in WSL
            string wslDistName = getDefaultWSLDist();
            foreach (String wslRootDir in getWSLRootDirs())
            {
                if (wslRootDir.Equals("mnt"))
                {
                    // Exclude /mnt to prevent recursive conversion
                    continue;
                }

                String winPath = "\\\\wsl$\\" + wslDistName + "\\" + wslRootDir;
                MappingDefinition mapdef = new MappingDefinition(winPath, "/" + wslRootDir);
                pathMappings.Add(mapdef);
            }
        }

        public static string getNetDriveMappedPath(string netdrive)
        {
            netdrive = netdrive.Trim().Substring(0, 2);
            var sb = new StringBuilder(512);
            var size = sb.Capacity;
            var error = WNetGetConnection(netdrive, sb, ref size);
            return error == 0 ? sb.ToString() : null;
        }

        public static bool pathEquals(string path1, string path2)
        {
            path1 = path1.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            path2 = path2.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            if (path1[path1.Length - 1] != Path.AltDirectorySeparatorChar)
            {
                path1 = path1 + Path.AltDirectorySeparatorChar;
            }
            if (path2[path2.Length - 1] != Path.AltDirectorySeparatorChar)
            {
                path2 = path2 + Path.AltDirectorySeparatorChar;
            }
            return String.Equals(path1, path2, StringComparison.OrdinalIgnoreCase);
        }

        public static NameValueCollection getWSLMounts()
        {
            NameValueCollection ret = new NameValueCollection();
            Process proc = new Process();

            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.FileName = "wsl";
            proc.StartInfo.Arguments = "-e mount";
            proc.Start();

            for (string line = proc.StandardOutput.ReadLine(); line != null; line = proc.StandardOutput.ReadLine())
            {
                string[] splittedLine = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (splittedLine[4].Equals("drvfs"))
                {
                    if (splittedLine[0][1] == ':')
                    {
                        string winPath = splittedLine[0].Substring(0, 2).ToLower();
                        string unixPath = splittedLine[2];
                        if (winPath[0] == unixPath.Substring(unixPath.Length - 1)[0])
                        {
                            ret.Add(splittedLine[0].Substring(0, 2).ToLower(), splittedLine[2]);
                        }
                        else
                        {
                            // Console.WriteLine("Inconsistent mount path");
                        }
                    }
                    else if (splittedLine[0].StartsWith("\\"))
                    {
                        ret.Add(splittedLine[0], splittedLine[2]);
                    }
                }
            }

            proc.WaitForExit();

            return ret;
        }

        public static List<string> getWSLRootDirs()
        {
            List<string> ret = new List<string>();
            Process proc = new Process();

            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.FileName = "wsl";
            proc.StartInfo.Arguments = "-e ls -a /";
            proc.Start();

            for (string line = proc.StandardOutput.ReadLine(); line != null; line = proc.StandardOutput.ReadLine())
            {
                if (!line.Equals(".") && !line.Equals(".."))
                {
                    ret.Add(line);
                }
            }

            proc.WaitForExit();

            return ret;
        }

        public static string getDefaultWSLDist()
        {
            string ret = null;
            Process proc = new Process();

            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.FileName = "wsl";
            proc.StartInfo.Arguments = "-l -v";
            proc.Start();

            string line = "";
            foreach (char c in proc.StandardOutput.ReadToEnd().ToCharArray())
            {
                if (c == '\n')
                {
                    if (line[0] == '*')
                    {
                        string[] splittedLine = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        ret = splittedLine[1];
                    }
                    line = "";
                }
                else if (c != '\0' && c != '\r')
                {
                    line = line + c;
                }
            }

            proc.WaitForExit();
            return ret;
        }

        public void print()
        {
            foreach (MappingDefinition mapdef in pathMappings)
            {
                string driveTypeStr = mapdef.wslNative ? "wsl" : mapdef.driveType.ToString().ToLower();
                System.Console.WriteLine(mapdef.winPath + " on " + mapdef.unixPath + " " + driveTypeStr +
                    (mapdef.mounted ? " ready" : (mapdef.mountable ? " mountable" : " n/a")) +
                    (mapdef.driveType == DriveType.Network ? " (" + getNetDriveMappedPath(mapdef.winPath) + ")" : ""));
            }
        }

        public string toUnixPath(string winPath)
        {
            foreach (MappingDefinition curMapDef in pathMappings)
            {
                if (winPath.Length >= curMapDef.winPath.Length &&
                    pathEquals(curMapDef.winPath, winPath.Substring(0, curMapDef.winPath.Length)))
                {
                    winPath = curMapDef.unixPath + winPath.Substring(curMapDef.winPath.Length);
                    winPath = winPath.Replace('\\', '/');
                    return winPath;
                }
            }

            return null;
        }

        public string toWinPath(string unixPath)
        {
            foreach (MappingDefinition curMapDef in pathMappings)
            {
                if (unixPath.StartsWith(curMapDef.unixPath, StringComparison.OrdinalIgnoreCase))
                {
                    unixPath = curMapDef.winPath + unixPath.Substring(curMapDef.unixPath.Length);
                    unixPath = unixPath.Replace('/', '\\');
                    return unixPath;
                }
            }

            return null;
        }
    }

}