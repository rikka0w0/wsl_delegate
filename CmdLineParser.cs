using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;

namespace wsl_delegate
{
    class InputOptionDefinition
    {
        public readonly string shortName;
        public readonly string longName;
        public readonly bool hasParam;
        public readonly bool allowDupe;
        public InputOptionDefinition(string shortName, string longName, bool hasParam, bool allowDupe)
        {
            this.shortName = shortName;
            this.longName = longName;
            this.hasParam = hasParam;
            this.allowDupe = allowDupe;
        }

        public InputOptionDefinition(string shortName, string longName) : this(shortName, longName, false, false)
        {

        }

        public InputOptionDefinition newShort(string name)
        {
            return new InputOptionDefinition(name, null);
        }

        public InputOptionDefinition newLong(string name)
        {
            return new InputOptionDefinition(null, name);
        }

        public bool accepts(string s)
        {
            if (shortName != null && shortName.Equals(s))
                return true;
            else if (longName != null && longName.Equals(s))
                return true;
            else
                return false;
        }
        public bool mappable()
        {
            return !((shortName == null) ^ (longName == null));
        }

        public override string ToString()
        {
            string ret = "";
            if (shortName != null)
            {
                ret = shortName;
            }
            if (longName != null)
            {
                ret += "(" + longName + ")";
            }
            if (hasParam)
            {
                ret += " <>";
            }
            return ret;
        }
    }

    class TokenrizedInputArgs
    {
        public readonly Tuple<InputOptionDefinition, string>[] options;
        public readonly string payload;
        private TokenrizedInputArgs(string payload, Tuple<InputOptionDefinition, string>[] options)
        {
            this.payload = payload;
            this.options = options;
        }

        public bool contains(InputOptionDefinition optDefIn)
        {
            foreach (Tuple<InputOptionDefinition, string> option in options)
            {
                if (optDefIn == option.Item1)
                {
                    return true;
                }
            }
            return false;
        }

        public string getFirst(InputOptionDefinition optDefIn)
        {
            foreach (Tuple<InputOptionDefinition, string> option in options)
            {
                if (optDefIn == option.Item1)
                {
                    return option.Item2;
                }
            }
            return null;
        }

        public static TokenrizedInputArgs parse(InputOptionDefinition[] optionDefs, string argsIn)
        {
            InputArgs parsed = InputArgs.parse(new HasParamDefinition(optionDefs), argsIn);
            if (parsed == null)
            {
                return null;
            }
            return parse(optionDefs, parsed);
        }

        public static TokenrizedInputArgs parse(InputOptionDefinition[] optionDefs, InputArgs rawArgs)
        {
            LinkedList<Tuple<InputOptionDefinition, string>> tokenrized = new LinkedList<Tuple<InputOptionDefinition, string>>();
            foreach (Tuple<string, string> optionIn in rawArgs.options)
            {
                string name = optionIn.Item1;
                string value = optionIn.Item2;
                bool foundInDef = false;
                foreach (InputOptionDefinition opDef in optionDefs)
                {
                    if (opDef.accepts(name))
                    {
                        foundInDef = true;

                        bool hasDupe = false;
                        foreach (Tuple<InputOptionDefinition, string> existingToken in tokenrized)
                        {
                            if (existingToken.Item1 == opDef)
                            {
                                hasDupe = true;
                            }
                        }

                        if (!hasDupe || (hasDupe && opDef.allowDupe))
                        {
                            tokenrized.AddLast(new Tuple<InputOptionDefinition, string>(opDef, value));
                        }
                    }
                }

                if (!foundInDef)
                {
                    Console.WriteLine("Unknown option: " + name);
                    return null;
                }
            }

            return new TokenrizedInputArgs(rawArgs.payload, tokenrized.ToArray());
        }
    }

    interface IHasParamHandler
    {
        bool hasParam(string verb);
    }

    class NoParamDefinition : IHasParamHandler
    {
        public static readonly IHasParamHandler instance = new NoParamDefinition();
        private NoParamDefinition() { }
        public bool hasParam(string verb)
        {
            return false;
        }
    }

    class HasParamDefinition : IHasParamHandler
    {
        private readonly InputOptionDefinition[] optDefs;
        public HasParamDefinition(InputOptionDefinition[] optDefs)
        {
            this.optDefs = optDefs;
        }

        public bool hasParam(string verb)
        {
            foreach (InputOptionDefinition optDef in optDefs)
            {
                if (optDef.accepts(verb))
                    return optDef.hasParam;
            }

            return false;
        }
    }

    class InputArgs
    {
        enum ParserState
        {
            FindVerb,
            Minus,
            MinusLetter,
            MinusMinus,
            MinusMinusLetters,
            ParseParamStart,
            ParseParamInQuote,
            ParseParamPostQuote,
            ParseParam,
            Payload
        }

        public readonly LinkedList<Tuple<string, string>> options;
        public readonly string payload;

        private InputArgs(string payload, LinkedList<Tuple<string, string>> options)
        {
            this.payload = payload;
            this.options = options;
        }

        public static string getRawArgs()
        {
            string imagePath = Environment.GetCommandLineArgs()[0];
            string rawArgs;
            if (Environment.CommandLine[0] == '\"')
            {
                rawArgs = Environment.CommandLine.Substring(imagePath.Length + 2);
            }
            else
            {
                rawArgs = Environment.CommandLine.Substring(imagePath.Length);
            }

            return rawArgs.TrimStart();
        }

        private static bool isSpace(char c)
        {
            return c == ' ' || c == '\t';
        }

        private static char handleSlash(char c)
        {
            return c;
        }

        public static InputArgs parse(IHasParamHandler hph, string rawArgs)
        {
            LinkedList<Tuple<string, string>> options = new LinkedList<Tuple<string, string>>();
            rawArgs = rawArgs.TrimStart();

            string payload = "";
            string curVerb = null;
            string curParam = null;
            bool lastSlash = false;
            ParserState parserState = ParserState.FindVerb;
            for (int i = 0; i < rawArgs.Length; i++)
            {
                switch (parserState)
                {
                    case ParserState.FindVerb:
                        if (rawArgs[i] == '-')
                        {
                            parserState = ParserState.Minus;
                        }
                        else if (isSpace(rawArgs[i]))
                        {
                            continue;
                        }
                        else
                        {
                            parserState = ParserState.Payload;
                            payload += rawArgs[i];
                        }
                        break;
                    case ParserState.Minus:
                        if (rawArgs[i] == '-')
                        {
                            parserState = ParserState.MinusMinus;
                        }
                        else if (Char.IsLetterOrDigit(rawArgs[i]))
                        {
                            curVerb = "" + rawArgs[i];
                            if (hph.hasParam(curVerb))
                            {
                                parserState = ParserState.ParseParamStart;
                                lastSlash = false;
                            }
                            else
                            {
                                parserState = ParserState.MinusLetter;
                                options.AddLast(new Tuple<string, string>(curVerb, ""));
                            }
                        }
                        else
                        {
                            return null;
                        }
                        break;
                    case ParserState.MinusLetter:
                        if (isSpace(rawArgs[i]))
                        {
                            parserState = ParserState.FindVerb;
                        }
                        else if (Char.IsLetterOrDigit(rawArgs[i]))
                        {
                            curVerb = "" + rawArgs[i];
                            if (hph.hasParam(curVerb))
                            {
                                parserState = ParserState.ParseParamStart;
                                lastSlash = false;
                            }
                            else
                            {
                                parserState = ParserState.MinusLetter;
                                options.AddLast(new Tuple<string, string>(curVerb, ""));
                            }
                        }
                        else
                        {
                            return null;
                        }
                        break;
                    case ParserState.MinusMinus:
                        if (Char.IsLetterOrDigit(rawArgs[i]))
                        {
                            curVerb = "" + rawArgs[i];
                            parserState = ParserState.MinusMinusLetters;
                        }
                        else
                        {
                            return null;
                        }
                        break;
                    case ParserState.MinusMinusLetters:
                        if (isSpace(rawArgs[i]))
                        {
                            if (hph.hasParam(curVerb))
                            {
                                parserState = ParserState.ParseParamStart;
                                lastSlash = false;
                            }
                            else
                            {
                                options.AddLast(new Tuple<string, string>(curVerb, ""));
                                parserState = ParserState.FindVerb;
                            }
                        }
                        else if (Char.IsLetterOrDigit(rawArgs[i]) || rawArgs[i] == '-')
                        {
                            curVerb += rawArgs[i];
                        }
                        else if (rawArgs[i] == '=')
                        {
                            // always treat the string after = as a value of the current option
                            parserState = ParserState.ParseParamStart;
                            lastSlash = false;
                        }
                        else
                        {
                            return null;
                        }
                        break;
                    case ParserState.ParseParamStart:
                        if (lastSlash)
                        {
                            lastSlash = false;
                            curParam = "" + handleSlash(rawArgs[i]);
                            parserState = ParserState.ParseParam;
                        }
                        else if (rawArgs[i] == '\\')
                        {
                            lastSlash = true;
                        }
                        else if (rawArgs[i] == '-')
                        {
                            // Missing parameter
                            return null;
                        }
                        else if (rawArgs[i] == '\"')
                        {
                            curParam = "";
                            parserState = ParserState.ParseParamInQuote;
                        }
                        else if (isSpace(rawArgs[i]))
                        {
                            continue;
                        }
                        else
                        {
                            curParam = "" + rawArgs[i];
                            parserState = ParserState.ParseParam;
                        }
                        break;

                    case ParserState.ParseParam:
                    case ParserState.ParseParamInQuote:
                        if (lastSlash)
                        {
                            lastSlash = false;
                            curParam += handleSlash(rawArgs[i]);
                        }
                        else if (rawArgs[i] == '\\')
                        {
                            lastSlash = true;
                        }
                        else if (parserState == ParserState.ParseParam && isSpace(rawArgs[i]))
                        {
                            options.AddLast(new Tuple<string, string>(curVerb, curParam));
                            parserState = ParserState.FindVerb;
                        }
                        else if (parserState == ParserState.ParseParamInQuote && rawArgs[i] == '\"')
                        {
                            options.AddLast(new Tuple<string, string>(curVerb, curParam));
                            parserState = ParserState.ParseParamPostQuote;
                        }
                        else
                        {
                            curParam += rawArgs[i];
                        }
                        break;
                    case ParserState.ParseParamPostQuote:
                        if (isSpace(rawArgs[i]))
                        {
                            parserState = ParserState.FindVerb;
                        } else
                        {
                            return null;
                        }
                        break;
                    default:
                        payload += rawArgs[i];
                        break;
                }
            }

            InputArgs ret = new InputArgs(payload, options);
            return ret;
        }
    }
}