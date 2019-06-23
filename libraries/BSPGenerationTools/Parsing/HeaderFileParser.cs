﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BSPGenerationTools.Parsing
{
    public struct SimpleToken
    {
        public CppTokenizer.TokenType Type;
        public string Value;
        public int Line;

        public SimpleToken(CppTokenizer.TokenType type, string value, int line)
        {
            Type = type;
            Value = value;
            Line = line;
        }

        public override string ToString()
        {
            return Value;
        }

        public SimpleToken WithAppendedText(string text)
        {
            SimpleToken result = this;
            result.Value += text;
            return result;
        }
    }

    public struct PreprocessorMacro
    {
        public string Name;
        public SimpleToken[] Value;
        public string CombinedComments;

        public override string ToString()
        {
            return $"#define {Name} " + string.Join(" ", Value.Select(v => v.ToString()));
        }
    }

    public class ParsedStructure
    {
        public class Entry
        {
            public string Name;
            public SimpleToken[] Type;

            public string TrailingComment;
            public int ArraySize;
        }

        public string Name;
        public Entry[] Entries;

        public override string ToString()
        {
            return Name;
        }
    }

    public class PreprocessorMacroGroup
    {
        public SimpleToken PrecedingComment;
        public List<PreprocessorMacro> Macros = new List<PreprocessorMacro>();
    }

    public class PreprocessorMacroCollection
    {
        public Dictionary<string, PreprocessorMacro> PreprocessorMacros = new Dictionary<string, PreprocessorMacro>();
    }

    public class ParsedHeaderFile : PreprocessorMacroCollection
    {
        public string Path;
        public Dictionary<string, ParsedStructure> Structures = new Dictionary<string, ParsedStructure>();
    }

    public class HeaderFileParser
    {
        private string _FilePath;

        public HeaderFileParser(string fn)
        {
            _FilePath = fn;
        }

        class PreprocessorMacroGroupBuilder
        {
            public void OnPreprocessorMacroDefined(PreprocessorMacro macro)
            {
            }

            public void OnTokenProcessed(CppTokenizer.Token token, bool isFirstTokenInLine)
            {
            }
        }

        List<SimpleToken> TokenizeFileAndFillPreprocessorMacroCollection(string[] lines, PreprocessorMacroCollection collection)
        {
            List<SimpleToken> result = new List<SimpleToken>();
            var tt = CppTokenizer.TokenType.Whitespace;
            var tokenizer = new CppTokenizer("");

            PreprocessorMacroGroupBuilder builder = new PreprocessorMacroGroupBuilder();

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var tokens = tokenizer.TokenizeLine(line, ref tt, false);
                if (tokens.Length == 0)
                    continue;

                if (tokens[0].Type == CppTokenizer.TokenType.PreprocessorDirective)
                {
                    if (line.Substring(tokens[0].Start, tokens[0].Length) == "#define")
                    {
                        while (tokens.Last().Type == CppTokenizer.TokenType.Operator &&
                               line.Substring(tokens.Last().Start, tokens.Last().Length) == "\\")
                        {
                            //This is a multi-line #define statement. Combine it with the next line until we reach a line without a trailing backslash
                            var newTokens = tokenizer.TokenizeLine(lines[++lineIndex], ref tt, false);
                            tokens = tokens.Take(tokens.Length - 1).Concat(newTokens).ToArray();
                        }

                        List<SimpleToken> macroTokens = new List<SimpleToken>();
                        List<string> comment = new List<string>();

                        foreach (var token in tokens.Skip(2))
                        {
                            if (token.Type == CppTokenizer.TokenType.Comment)
                                comment.Add(line.Substring(token.Start, token.Length));
                            else
                                macroTokens.Add(new SimpleToken(token.Type, line.Substring(token.Start, token.Length), lineIndex));
                        }

                        PreprocessorMacro macro = new PreprocessorMacro
                        {
                            Name = line.Substring(tokens[1].Start, tokens[1].Length),
                            Value = macroTokens.ToArray(),
                            CombinedComments = comment.Count == 0 ? null : string.Join(" ", comment.ToArray())
                        };

                        builder.OnPreprocessorMacroDefined(macro);
                        collection.PreprocessorMacros[macro.Name] = macro;
                    }
                }
                else
                {
                    bool isFirstToken = true;

                    foreach (var token in tokens)
                    {
                        builder.OnTokenProcessed(token, isFirstToken);

                        if (token.Type == CppTokenizer.TokenType.Comment && result.Count > 0 && result[result.Count - 1].Type == CppTokenizer.TokenType.Comment)
                        {
                            //Merge adjacent comments
                            string separator = isFirstToken ? "\n" : " ";
                            result[result.Count - 1] = result[result.Count - 1].WithAppendedText(separator + line.Substring(token.Start, token.Length));
                        }
                        else
                            result.Add(new SimpleToken(token.Type, line.Substring(token.Start, token.Length), lineIndex));

                        isFirstToken = false;
                    }
                }
            }

            return result;
        }

        public ParsedHeaderFile ParseHeaderFile()
        {
            ParsedHeaderFile result = new ParsedHeaderFile { Path = _FilePath };
            var tokens = TokenizeFileAndFillPreprocessorMacroCollection(File.ReadAllLines(_FilePath), result);
            ExtractStructureDefinitions(tokens, result.Structures, parameters);
            return result;
        }

        class SimpleTokenReader
        {
            private int _Index;
            private List<SimpleToken> _Tokens;

            public SimpleTokenReader(List<SimpleToken> tokens)
            {
                _Index = -1;
                _Tokens = tokens;
            }

            public bool EOF => _Index >= _Tokens.Count;

            public SimpleToken ReadNext(bool skipComments)
            {
                if (!EOF)
                {
                    _Index++;
                    while (skipComments && Current.Type == CppTokenizer.TokenType.Comment)
                    {
                        _Index++;
                    }
                }

                return Current;
            }

            public SimpleToken Current
            {
                get
                {
                    if (EOF)
                        return new SimpleToken();
                    else
                        return _Tokens[_Index];
                }
            }
        }

        public class WarningEventArgs : EventArgs
        {
            public string File;
            public int Line;
            public string Text;

            public WarningEventArgs(string file, int line, string text)
            {
                File = file;
                Line = line;
                Text = text;
            }
        }

        public event EventHandler<WarningEventArgs> Warning;

        private void ExtractStructureDefinitions(List<SimpleToken> tokens, Dictionary<string, ParsedStructure> structures, HeaderFileParseParameters parameters)
        {
            SimpleTokenReader reader = new SimpleTokenReader(tokens);

            while (!reader.EOF)
            {
                var token = reader.ReadNext(true);
                if (token.Type != CppTokenizer.TokenType.Identifier || token.Value != "typedef")
                    continue;

                token = reader.ReadNext(true);
                if (token.Type != CppTokenizer.TokenType.Identifier || token.Value != "struct")
                    continue;

                token = reader.ReadNext(true);
                if (token.Type == CppTokenizer.TokenType.Identifier)
                    token = reader.ReadNext(true);  //Skip through the struct name definition, as we will use the typedef name

                if (token.Type != CppTokenizer.TokenType.Bracket || token.Value != "{")
                {
                    ReportUnexpectedToken(token);
                    continue;
                }

                List<ParsedStructure.Entry> entries = new List<ParsedStructure.Entry>();
                List<SimpleToken> tokensInThisStatement = new List<SimpleToken>();

                while (!reader.EOF)
                {
                    token = reader.ReadNext(false);
                    if (token.Type == CppTokenizer.TokenType.Comment)
                    {
                        if (entries.Count > 0)
                            entries[entries.Count - 1].TrailingComment = token.Value;
                    }
                    if (token.Type == CppTokenizer.TokenType.Bracket && token.Value == "{")
                    {
                        //Nested structs are not supported
                        ReportUnexpectedToken(token);
                        break;
                    }
                    else if (token.Type == CppTokenizer.TokenType.Bracket && token.Value == "}")
                    {
                        token = reader.ReadNext(true);
                        if (token.Type != CppTokenizer.TokenType.Identifier)
                            ReportUnexpectedToken(token);
                        else
                            structures[token.Value] = new ParsedStructure { Name = token.Value, Entries = entries.ToArray() };

                        break;
                    }
                    else if (token.Type == CppTokenizer.TokenType.Operator && token.Value == ";")
                    {
                        entries.Add(ParseSingleStructureMember(tokensInThisStatement));
                        tokensInThisStatement.Clear();
                    }
                    else
                        tokensInThisStatement.Add(token);
                }

            }

        }

        private ParsedStructure.Entry ParseSingleStructureMember(List<SimpleToken> tokensInThisStatement)
        {
            int idx = tokensInThisStatement.Count - 1;
            if (tokensInThisStatement.Count == 0)
                throw new Exception("Empty structure member");

            int arraySize = 1;

            if (idx >= 2 && tokensInThisStatement[idx].Type == CppTokenizer.TokenType.Bracket)
            {
                if (tokensInThisStatement[idx].Value != "]")
                    throw new Exception("Unexpected bracket at the end of a structure statement");
                
                arraySize = (int)ParseMaybeHex(tokensInThisStatement[idx - 1].Value);
                if (tokensInThisStatement[idx - 2].Value != "[")
                    throw new Exception("Unexpected bracket at the end of a structure statement");

                idx -= 3;
            }

            if (tokensInThisStatement[idx].Type != CppTokenizer.TokenType.Identifier)
            {
                ReportUnexpectedToken(tokensInThisStatement[idx]);
                throw new Exception("Unexpected token");
            }

            return new ParsedStructure.Entry
            {
                Name = tokensInThisStatement[idx].Value,
                Type = tokensInThisStatement.Take(idx).ToArray(),
                ArraySize = arraySize
            };
        }

        public static ulong ParseMaybeHex(string text)
        {
            if (text.StartsWith("0x"))
                return ulong.Parse(text.Substring(2), NumberStyles.AllowHexSpecifier, null);
            else
                return ulong.Parse(text);
        }

        private void ReportUnexpectedToken(SimpleToken token)
        {
            Warning?.Invoke(this, new WarningEventArgs(_FilePath, token.Line, $"Unexpected '{token.Value}'"));
        }
    }
}
