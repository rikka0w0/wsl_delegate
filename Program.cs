using System;
using System.Collections;
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

    class OutputParserWorker
    {
        MappingService mappingService;
        StreamReader input;
        StreamWriter output;
        Process proc;
        public OutputParserWorker(MappingService mappingService, StreamReader input, StreamWriter output, Process proc)
        {
            this.mappingService = mappingService;
            this.input = input;
            this.output = output;
            this.proc = proc;
        }

        public void doWork()
        {
            while (!proc.HasExited)
            mappingService.toWinOnStream(input, output);
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

            bool attemptMount = false;
            





            Process proc = new Process();

            proc.EnableRaisingEvents = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            proc.StartInfo.FileName = "wsl";
            proc.StartInfo.Arguments = wslArgs;


            // Console.SetBufferSize(Console.BufferWidth, 5000);

            var standardOutput = new StreamWriter(Console.OpenStandardOutput());
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);

            //var standardError = new StreamWriter(Console.OpenStandardError());
            //standardError.AutoFlush = true;
            //Console.SetError(standardError);

            // OutputParserWorker errorParser = new OutputParserWorker(ms, proc.StandardError, standardError);
            //Thread errorParserThread = new Thread(errorParser.doWork);
            //errorParserThread.Start();
            proc.OutputDataReceived += new DataReceivedEventHandler(onOutputDataReceived);
            proc.Start();
            proc.BeginOutputReadLine();

            //OutputParserWorker outputParser = new OutputParserWorker(ms, proc.StandardOutput, standardOutput, proc);
            //Thread outputParserThread = new Thread(outputParser.doWork);
            //outputParserThread.Start();
            //outputParserThread.Join();

            // ms.toWinOnStream(proc.StandardOutput, standardOutput);

            //errorParserThread.Join();

            //string stdout = proc.StandardOutput.ReadToEnd();
            //string stderr = proc.StandardError.ReadToEnd();
            //stderr = stderr.Replace('\u2018', '\'').Replace('\u2019', '\'');
            proc.WaitForExit();
           

            Console.ReadLine();
        }
    }
}
