using System;
using System.Collections.Generic;

namespace wsl_delegate
{
	class GccCmdParser
	{
		private readonly MappingService mappingService;
		public GccCmdParser(MappingService mappingService)
		{
			this.mappingService = mappingService;
		}

		enum ParserState
		{
			FindOpt,
			Minus,
			MinusLetter,
			ParseParamInQuote,
			ParseParam,
			Payload
		}

		private static bool isSpace(char c)
		{
			return c == ' ' || c == '\t';
		}

		private static char handleSlash(char c)
		{
			return c;
		}

		private void examineParam(string wslCmdLine, int startIndex, int length, LinkedList<Tuple<int, int>> probedPathLoc)
		{
			//Console.WriteLine("param: " + wslCmdLine.Substring(startIndex, length));

			if (wslCmdLine[startIndex] == '-')
			{
				if (wslCmdLine[startIndex+1] == 'I')
				{
					startIndex += 2;
					length -= 2;
				}
			}

			if (wslCmdLine[startIndex] == '\"')
			{
				startIndex += 1;
				length -= 2;
			}

			string possibleWinPath = wslCmdLine.Substring(startIndex, length);
			if (mappingService.toUnixPath(possibleWinPath) != null)
			{
				probedPathLoc.AddLast(new Tuple<int, int>(startIndex, length));
				//Console.WriteLine("unix: " + wslCmdLine.Substring(startIndex, length));
			}
		}

		public string mapToUnixPath(string cmdLine)
		{
			// Skip the executable name
			int firstSpaceIndex = 0;
			for (; firstSpaceIndex < cmdLine.Length && cmdLine[firstSpaceIndex] == ' '; firstSpaceIndex++) ;
			for (; firstSpaceIndex < cmdLine.Length && cmdLine[firstSpaceIndex] != ' '; firstSpaceIndex++) ;

			// loc, len
			LinkedList<Tuple<int, int>> probedPathLoc = new LinkedList<Tuple<int, int>>();

			int startIndex = -1;
			bool lastSlash = false;
			ParserState parserState = ParserState.FindOpt;
			for (int i = firstSpaceIndex; i < cmdLine.Length; i++)
			{
				switch (parserState)
				{
					case ParserState.FindOpt:
						if (cmdLine[i] == '-')
						{
							startIndex = i;
							parserState = ParserState.Minus;
						}
						else if (isSpace(cmdLine[i]))
						{
							continue;
						}
						else if (cmdLine[i] == '\"')
						{
							startIndex = i;
							parserState = ParserState.ParseParamInQuote;
						} else
						{
							startIndex = i;
							parserState = ParserState.ParseParam;
						}
						break;
					case ParserState.Minus: // -s or -I or --
						if (Char.IsLetterOrDigit(cmdLine[i]) || cmdLine[i] == '-')
						{
							lastSlash = false;
							parserState = ParserState.MinusLetter;
						}
						else
						{
							return null;
						}
						break;
					case ParserState.MinusLetter: // -st or -I"
						if (cmdLine[i] == '\"')
						{
							parserState = ParserState.ParseParamInQuote;
						} else if (isSpace(cmdLine[i]))
						{
							examineParam(cmdLine, startIndex, i - startIndex, probedPathLoc);
							parserState = ParserState.FindOpt;
						} else
						{

						}
						break;
					case ParserState.ParseParamInQuote:
					case ParserState.ParseParam:
						if (lastSlash)
						{
							lastSlash = false;
						}
						else if (cmdLine[i] == '\\')
						{
							lastSlash = true;
						}
						else if (parserState == ParserState.ParseParamInQuote && cmdLine[i] == '\"')
						{
							examineParam(cmdLine, startIndex, i - startIndex + 1, probedPathLoc);
							parserState = ParserState.FindOpt;
						}
						else if (parserState == ParserState.ParseParam && isSpace(cmdLine[i]))
						{
							examineParam(cmdLine, startIndex, i - startIndex, probedPathLoc);
							parserState = ParserState.FindOpt;
						}
						else
						{
						}
						break;
					default:
						break;
				}
			}

			if (parserState == ParserState.ParseParam)
			{
				examineParam(cmdLine, startIndex, cmdLine.Length - startIndex, probedPathLoc);
			}

			string ret = "";
			int lastLoc = 0;
			foreach (Tuple<int, int> strLocDef in probedPathLoc) { 
				ret += cmdLine.Substring(lastLoc, strLocDef.Item1 - lastLoc);
				string w2u = cmdLine.Substring(strLocDef.Item1, strLocDef.Item2);
				w2u = mappingService.toUnixPath(w2u);
				ret += w2u;

				lastLoc = strLocDef.Item1 + strLocDef.Item2;
			}

			if (lastLoc < cmdLine.Length)
			{
				ret += cmdLine.Substring(lastLoc, cmdLine.Length - lastLoc);
			}

			return ret;
		}
	}
}