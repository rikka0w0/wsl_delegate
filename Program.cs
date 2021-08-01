using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace wsl_delegate
{

    class OutputParser
    {
        static bool isValidStart(char c)
        {
            return c == '\"' || c == ' ' || c == '\'' || c == '=' || c==':';
        }

        public static string toWinByLine(MappingService mappingService, string line)
        {
            foreach (MappingDefinition curMapDef in mappingService.pathMappings)
            {
                for (int i = line.IndexOf(curMapDef.unixPath); i >= 0; i = line.IndexOf(curMapDef.unixPath, i))
                {
                    if (i == 0 || isValidStart(line[i - 1]) || (i > 1 && line[i - 2] == '-' && Char.IsUpper(line[i - 1])))
                    {
                        String before = line.Substring(0, i);
                        String after = line.Substring(i + curMapDef.unixPath.Length);
                        line = before + curMapDef.winPath + after;
                        i += curMapDef.winPath.Length;
                    }
                    else
                    {
                        i += curMapDef.unixPath.Length;
                    }
                }
            }

            return line.Replace('/', '\\').Replace('\u2018', '\'').Replace('\u2019', '\'');
        }
    }


    class Program
    {
        static readonly InputOptionDefinition optionHelp = new InputOptionDefinition("h", "help");
        static readonly InputOptionDefinition optionShowMounts = new InputOptionDefinition("l", "list-mounts");
        static readonly InputOptionDefinition optionToUnixPath = new InputOptionDefinition("u", null);
        static readonly InputOptionDefinition optionToWinPath = new InputOptionDefinition("w", null);
        static readonly InputOptionDefinition optionVerbose = new InputOptionDefinition("v", "verbose");
        static readonly InputOptionDefinition optionTemplate = new InputOptionDefinition(null, "template", true, false);

        static readonly InputOptionDefinition[] options = 
            {optionHelp, optionShowMounts, optionToUnixPath, optionToWinPath, optionVerbose, optionTemplate};

        const string helpText = "Usage: wsl_delegate <options> <unix command> \r\n" +
            "";

        static void Main(string[] parsedArgs)
        {
            bool verbose = false;
            string workingDir = Directory.GetCurrentDirectory();
            string rawArgs = InputArgs.getRawArgs();
            TokenrizedInputArgs inputArgs = TokenrizedInputArgs.parse(options, rawArgs);
            if (inputArgs == null)
            {
                Console.WriteLine(helpText);
                Environment.ExitCode = 128;
                return;
            }

            if (inputArgs.contains(optionHelp))
            {
                Console.WriteLine(helpText);
            }

            if (inputArgs.contains(optionVerbose))
            {
                verbose = true;
                Console.WriteLine("Command line:");
                Console.WriteLine(Environment.CommandLine);
                Console.WriteLine();
                Console.WriteLine("Parsed options:");
                foreach (Tuple<InputOptionDefinition, string> option in inputArgs.options)
                {
                    Console.WriteLine(option.Item1.ToString() + " = " + option.Item2.ToString());
                }
                Console.WriteLine();
                Console.WriteLine("Unix command (with param):");
                Console.WriteLine(inputArgs.payload);
                Console.WriteLine();
            }

            MappingService ms = new MappingService();
            if (inputArgs.contains(optionShowMounts))
            {
                Console.WriteLine("Windows-Unix path mappings:");
                ms.print();
            }

            string wslCmdLine = inputArgs.payload;
            string template = inputArgs.getFirst(optionTemplate);
            if (template != null)
            {
                if (verbose)
                {
                    Console.WriteLine("Using template: " + template);
                }

                if (template.Equals("gcc"))
                {
                    GccCmdParser gccCmdParser = new GccCmdParser(ms);
                    wslCmdLine = gccCmdParser.mapToUnixPath(inputArgs.payload);
                } else
                {
                    Console.WriteLine("Template " + template + " not found");
                    Environment.ExitCode = 128;
                    return;
                }
            }

            string workingDirMapped = ms.toUnixPath(workingDir);
            string wslArgs = "cd \"" + workingDirMapped + "\" && " + wslCmdLine;
            if (verbose)
            {
                Console.WriteLine("WSL Working Directory: \r\n" + workingDirMapped);
                Console.WriteLine("WSL Command: \r\n" + wslCmdLine);
            }

            Process proc = new Process();

            proc.EnableRaisingEvents = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            proc.StartInfo.FileName = "wsl";
            proc.StartInfo.Arguments = wslArgs;

            var standardOutput = new StreamWriter(Console.OpenStandardOutput(10240));
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);

            var standardError = new StreamWriter(Console.OpenStandardError());
            standardError.AutoFlush = true;
            Console.SetError(standardError);

            proc.OutputDataReceived += new DataReceivedEventHandler(onOutputDataReceived);
            proc.ErrorDataReceived += new DataReceivedEventHandler(onErrorDataReceived);
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            bool isEnd = false;
            while (!isEnd)
            {
                string o = null;
                if (queueOutput.TryDequeue(out o))
                {
                    if (o == null)
                    {
                        isEnd = true;
                    } else
                    {
                        standardOutput.WriteLine(OutputParser.toWinByLine(ms, o));
                    }
                }
            }

            isEnd = false;
            while (!isEnd)
            {
                string o = null;
                if (queueError.TryDequeue(out o))
                {
                    if (o == null)
                    {
                        isEnd = true;
                    }
                    else
                    {
                        standardError.WriteLine(OutputParser.toWinByLine(ms, o));
                    }
                }
            }
            

            //proc.WaitForExit();
        }

        static ConcurrentQueue<string> queueOutput = new ConcurrentQueue<string>();
        static ConcurrentQueue<string> queueError = new ConcurrentQueue<string>();
        static void onOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            queueOutput.Enqueue(e.Data);
        }

        static void onErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            queueError.Enqueue(e.Data);
        }
    }
}
