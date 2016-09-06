using System;
using System.Globalization;
using System.Collections.Generic;

namespace MBINRawTemplateParser
{
    enum LineType { NOP, NUMBER, STRING, CALL, BLOCK };

    class Sub
    {
        public string subName;
        public string className;
        public int sz;

        public Sub(string subName, string className, int sz)
        {
            this.subName = subName;
            this.className = className;
            this.sz = sz;
        }
    }

    class Parser
    {
        private Dictionary<string, Sub> preSubs = new Dictionary<string, Sub>();

        private static readonly string EMPTY_STRING = "";
        private static readonly string VAR_PREFIX = "Unknown";
        private static readonly string TAB = "    ";
        private static readonly string TAB2 = TAB + TAB;

        private static readonly string HEADER_START = "namespace MBINCompiler.Models.Structs\r\n{\r\n" +
            TAB + "public class ";
        private static readonly string HEADER_END = " : NMSTemplate\r\n" + TAB + "{\r\n" + TAB2 + "// generated with MBINRawTemplateParser\r\n\r\n";
        private static readonly string FOOTER = TAB + "}\r\n}\r\n";

        private static readonly string COMMENT_START = TAB + " // ";
        private string className = null;

        private bool verbose;

        private int lastOffset = 0;
        private int lastOffsetSz = 0;

        public Parser(bool verbose = false)
        {
            this.verbose = verbose;
        }

        private string updateOffsetDiff(int offset, int sz)
        {
            string res = EMPTY_STRING;
            int diff = offset - (lastOffset + lastOffsetSz);

            // Console.WriteLine("offset:" + offset + ", lastOffset: " + lastOffset + ", sz: " + lastOffsetSz);

            if (diff > 0) {
                res = "\r\n" + TAB2 + "// missing " + diff.ToString() +
                    " bytes at offset " + lastOffset.ToString() + "\r\n" + TAB2 + "// ";
                switch (diff) {
                    default:
                        res += "could be a subroutine, padding or something that the parser skipped";
                        break;
                    case 1:
                        res += "does " + lastOffset.ToString() + " contain a WORD?";
                        break;
                    case 3:
                        res += "does " + lastOffset.ToString() + " contain a DWORD?";
                        break;
                    case 7:
                        res += "does " + lastOffset.ToString() + " contain a QWORD?";
                        break;
                }
                int padOffset = lastOffset + lastOffsetSz;
                res += "\r\n" + TAB2 + "[NMS(Size = 0x" + diff.ToString("X") + ", Ignore = true)]\r\n";
                res += TAB2 + "public byte[] Padding" + padOffset.ToString("X") + ";" +
                    TAB2 + "// offset: " + padOffset.ToString() + ", sz: " + diff + ", comment: auto padding \r\n\r\n";
            }

            lastOffset = offset;
            lastOffsetSz = sz;

            return res;
        }

        private bool hasStr(string str1, string str2)
        {
            return str1.IndexOf(str2) > -1;
        }

        private bool isLineString(string line)
        {
            return hasStr(line, "strncpy");
        }

        private bool isLineNumber(string line)
        {
            return hasStr(line, "*(") && hasStr(line, " *)") && hasStr(line, " = ");
        }

        private bool isLineCall(string line)
        {
            return hasStr(line, ");") && (hasStr(line, "sub_") || hasStr(line, "_"));
        }

        private bool isLineBlock(string line)
        {
            return hasStr(line, "  do") || hasStr(line, "  while");
        }

        // clould be something in the lines of ^[a,v]\w*$ at some point
        private bool hasVar(string str)
        {
            for (int i = 0; i < 16; i++) {
                if (hasStr(str, "a" + i.ToString()) || hasStr(str, "v" + i.ToString()))
                    return true;
            }
            return false;
        }

        private LineType getLineType(string line)
        {
            if (isLineBlock(line))
                return LineType.BLOCK;

            if (!hasVar(line)) // no variables -  not acting on the buffer?
                return LineType.NOP;

            if (isLineString(line))
                return LineType.STRING;
            if (isLineNumber(line))
                return LineType.NUMBER;
            if (isLineCall(line))
                return LineType.CALL;
            return LineType.NOP;
        }

        private void WriteParsedLineType(string line, LineType lineType, int i)
        {
            line = line.Replace("{", "{{").Replace("}", "}}");
            Console.WriteLine("lineN: {0,-6} | found " + lineType.ToString() + ", origin: " + line, i + 1);
        }

        private void WriteSkipLine(string line, int i)
        {
            line = line.Replace("{", "{{").Replace("}", "}}");
            Console.WriteLine("lineN: {0,-6} | skipping..." + ", origin: " + line, i + 1);
        }

        private void WriteWarningLineType(string line, LineType lineType, int i)
        {
            line = line.Replace("{", "{{").Replace("}", "}}");
            Console.WriteLine("lineN: {0,-6} | WARNING cannot parse " + lineType.ToString() + ", origin: " + line, i + 1);
        }

        private string parseNumber(string line)
        {
            string res = EMPTY_STRING;

            bool valueIs64Bit = hasStr(line, "i64") && hasStr(line, "QWORD");

            string valueStr = line.Substring(line.IndexOf("=") + 2);
            valueStr = valueStr.Substring(0, valueStr.LastIndexOf(";"));
            string valueStrOrigin = valueStr;
            valueStr = valueStr.Replace("i64", "");
            long value;
            bool parsedValue;

            if (valueStr.IndexOf("0x") > -1)
                parsedValue = long.TryParse(valueStr.Substring(2), NumberStyles.AllowHexSpecifier, null, out value);
            else
                parsedValue = long.TryParse(valueStr, out value);

            if (parsedValue) {
                byte[] bytes = BitConverter.GetBytes(value);

                float f = BitConverter.ToSingle(bytes, 0);

                string offset;
                string offsetHex;
                int offsetInt = 0;
                if (!hasStr(line, " + ")) {
                    offset = "0";
                    offsetHex = "0";
                } else {
                    offset = line;
                    int idxPlus = offset.IndexOf("+ ") + 2;
                    offset = offset.Substring(idxPlus, offset.LastIndexOf(")") - idxPlus);
                    if (offset.IndexOf("0x") > -1)
                        parsedValue = int.TryParse(offset.Substring(2), NumberStyles.AllowHexSpecifier, null, out offsetInt);
                    else
                        parsedValue = int.TryParse(offset, out offsetInt);
                    if (parsedValue)
                        offsetHex = offsetInt.ToString("X");
                    else
                        offsetHex = offset + "_int";
                }
                string type;

                int sz = 0;

                // some checks for sub-64bit values
                if (hasStr(line, "_WORD")) {
                    sz = 2;
                    res += updateOffsetDiff(offsetInt, sz);
                    type = "short";
                    valueStr = value.ToString();
                    goto output;
                } else if (hasStr(line, "_BYTE")) {
                    sz = 1;
                    res += updateOffsetDiff(offsetInt, sz);
                    type = "bool";
                    valueStr = value.ToString();
                    goto output;
                } else {
                    // attempt to parse a float
                    sz = 4;
                    valueStr = f.ToString();
                    type = "float";
                }

                if (valueIs64Bit && valueStr.IndexOf("E") == -1) {
                    if (hasStr(line, " = 0i64")) { // either a long or two floats?

                        res += updateOffsetDiff(offsetInt, 8);

                        sz = 8;
                        type = "long";
                        valueStr = "0, comment: either a long or two floats";
                    } else {
                        // e.g. *(_QWORD *)(v1 + 15680) = 0x40000000i64;

                        res += updateOffsetDiff(offsetInt, 8);

                        string packedComment = ", comment: two packed floats in a QWORD?";

                        // first float is a value
                        valueStr = valueStr.Replace(',', '.');
                        res += TAB2 + "public " + type + " " + VAR_PREFIX + offsetHex + ";";
                        res += COMMENT_START + "offset: " + offset + ", sz: 4, origin: " + valueStrOrigin + ", parsed: " + valueStr + packedComment + "(1)" + "\r\n";

                        // second float is a 0
                        offsetInt += 4;
                        offset = offsetInt.ToString();
                        offsetHex = offsetInt.ToString("X");
                        valueStr = "0";
                        res += TAB2 + "public " + type + " " + VAR_PREFIX + offsetHex + ";";
                        res += COMMENT_START + "offset: " + offset + ", sz: 4, origin: " + valueStrOrigin + ", parsed: " + valueStr + packedComment + "(2)";

                        return res;
                    }
                } else if (valueStr.IndexOf("E") > -1) { // not a float; possibly a int / long
                    valueStr = value.ToString();
                    if (hasStr(line, "_QWORD")) {
                        sz = 8;
                        res += updateOffsetDiff(offsetInt, sz);
                        type = "long";
                    } else if (hasStr(line, "_DWORD")) {
                        sz = 4;
                        res += updateOffsetDiff(offsetInt, sz);
                        type = "int";
                    }
                } else {
                    res += updateOffsetDiff(offsetInt, 4); // just a float
                }
            output:
                valueStr = valueStr.Replace(',', '.');
                res += TAB2 + "public " + type + " " + VAR_PREFIX + offsetHex + ";";
                res += COMMENT_START + "offset: " + offset + ", sz: " + sz.ToString() + ", origin: " + valueStrOrigin + ", parsed: " + valueStr;
            }

            return res;
        }

        private string parseString(string line)
        {
            string res = EMPTY_STRING;

            string[] args = line.Split(',');
            if (args.Length == 3) {
                string sz = args[2]; // only hex sz??
                sz = sz.Replace(" ", "").Replace("ui64);", ""); // hex only?
                res += TAB2 + "[NMS(Size = " + sz + ")]\r\n";
                string offset = args[0];
                offset = offset.Substring(offset.IndexOf("+ ") + 2);
                offset = offset.Replace(")", "");

                string offsetHex;
                bool parsedValue;
                int offsetInt;
                if (offset.IndexOf("0x") > -1)
                    parsedValue = int.TryParse(offset.Substring(2), NumberStyles.AllowHexSpecifier, null, out offsetInt);
                else
                    parsedValue = int.TryParse(offset, out offsetInt);
                if (parsedValue)
                    offsetHex = offsetInt.ToString("X");
                else
                    offsetHex = offset + "_int";

                int szInt;
                int.TryParse(sz.Substring(2), NumberStyles.AllowHexSpecifier, null, out szInt);
                res += updateOffsetDiff(offsetInt, szInt);

                res += TAB2 + "public string " + VAR_PREFIX + offsetHex + ";";
                res += COMMENT_START + "offset: " + offset + ", sz: " + szInt.ToString() + ", origin: " + args[1];
            }
            return res;
        }

        private string parseCall(string line, string nextLine)
        {
            string res = EMPTY_STRING;

            // no subroutines at offset 0?

            int idxPlus = line.IndexOf("+ ");
            if (idxPlus == -1)
                idxPlus = line.IndexOf("- ");
            string offset = "0";
            if (idxPlus != -1) {
                idxPlus += 2;
                offset = line.Substring(idxPlus, line.LastIndexOf(");") - idxPlus);
            }

            string offsetHex;
            bool parsedValue;
            int offsetInt;
            if (offset.IndexOf("0x") > -1)
                parsedValue = int.TryParse(offset.Substring(2), NumberStyles.AllowHexSpecifier, null, out offsetInt);
            else
                parsedValue = int.TryParse(offset, out offsetInt);
            if (parsedValue)
                offsetHex = offsetInt.ToString("X");
            else
                offsetHex = offset + "_int";

            int sz = 0;
            string szStr;
            string szStrComment = "";

            if (nextLine == null) {
                szStr = "0";
                szStrComment = "unknown";
            } else {
                string offsetNext;
                if (hasStr(nextLine, " + ") || hasStr(nextLine, " - ")) {
                    idxPlus = nextLine.IndexOf("+ ") + 2;
                    if (idxPlus == -1)
                        idxPlus = nextLine.IndexOf("- ") + 2;
                    if (idxPlus == -1)
                        offsetNext = offset;
                    else
                        offsetNext = nextLine.Substring(idxPlus, nextLine.LastIndexOf(")") - idxPlus);
                } else {
                    offsetNext = offset;
                }

                int offsetIntNext;
                if (offsetNext.IndexOf("0x") > -1)
                    parsedValue = int.TryParse(offsetNext.Substring(2), NumberStyles.AllowHexSpecifier, null, out offsetIntNext);
                else
                    parsedValue = int.TryParse(offsetNext, out offsetIntNext);

                sz = offsetIntNext - offsetInt;
                szStr = sz.ToString();
                szStrComment = szStr;
            }

            // check for sub in dictionary
            Sub subMatch = null;
            foreach (KeyValuePair<string, Sub> entry in preSubs) {
                if (hasStr(line, entry.Key)) {
                    subMatch = entry.Value;
                    break;
                }
            }

            if (subMatch != null) {
                res += updateOffsetDiff(offsetInt, subMatch.sz);

                string lineTrim = line.TrimStart(' ');
                res += TAB2 + "public " + subMatch.className + " Template" + offsetHex + ";";
                res += COMMENT_START + "offset: " + offset + ", sz: " + subMatch.sz.ToString() + ", origin: " + lineTrim + ", comment: call sub";
            } else {
                res += updateOffsetDiff(offsetInt, sz);

                string lineTrim = line.TrimStart(' ');
                res += "\r\n" + TAB2 + "// call to subroutine: " + lineTrim.Substring(0, lineTrim.IndexOf("(")) + "\r\n";
                res += TAB2 + "// filling with bytes as we don't have a way to expand it for now\r\n";
                res += TAB2 + "[NMS(Size = 0x" + sz.ToString("X") + ", Ignore = false)]\r\n";
                res += TAB2 + "public byte[] Subroutine" + offsetHex + ";";
                res += COMMENT_START + "offset: " + offset + ", sz: " + szStrComment + ", origin: " + lineTrim;
            }

            return res;
        }

        private string parseBlock(string line)
        {
            string res = EMPTY_STRING;
            res = "\r\n" + COMMENT_START + "comment: 'do/while' loop start/end detected, origin: " + line;
            return res;
        }

        private bool skipNextLine = false;

        private string parseLine(ref string[] lines, int i)
        {
            string result = null;

            string line = lines[i];
            string nextLine = null;
            if (i < lines.Length - 1)
                nextLine = lines[i + 1];
            LineType lineType = getLineType(line);

            switch (lineType) {
                case LineType.STRING:
                    result = parseString(line);
                    if (!result.Equals(EMPTY_STRING))
                        skipNextLine = true;
                    break;
                case LineType.NUMBER:
                    result = parseNumber(line);
                    break;
                case LineType.CALL:
                    result = parseCall(line, nextLine);
                    break;
                case LineType.BLOCK:
                    result = parseBlock(line);
                    break;
                case LineType.NOP:
                default:
                    WriteSkipLine(line, i);
                    return EMPTY_STRING;
            }

            if (result.Equals(EMPTY_STRING))
                WriteWarningLineType(line, lineType, i);
            else
                WriteParsedLineType(line, lineType, i);

            return result;
        }

        public string parse(string[] lines)
        {
            Console.WriteLine("parsing...");
            string output = EMPTY_STRING;

            int propCounter = 0;

            int i = 0;
            int len = lines.Length;


            // preprocessor
            for (i = 0; i < len; i++) {
                string line = lines[i];
                line = line.Trim();
                string[] args;
                if (line.StartsWith("#define_sub")) { // #define_sub sub_140141660 GcGalaxyMarkerSettings 112
                    args = line.Split(' ');
                    int sz = int.Parse(args[3]);
                    preSubs.Add(args[1], new Sub(args[1], args[2], sz));
                } else if (line.StartsWith("#define_class")) {
                    args = line.Split(' ');
                    className = args[1];
                }
            }

            for (i = 0;  i < len; i++) {
                string line = lines[i];
                if ((hasStr(line, ")") && hasStr(line, "(")) && line.IndexOf("//") != 0)
                    break;
            }

            if (i == len) {
                Console.WriteLine("skipped the whole input for some reason!");
                return output;
            }

            string templateHash = FNV32.getHash(string.Join("\r\n", lines)).ToString("X");
            string routineHash = FNV32.getHash(lines[i]).ToString("X");
            string header = "// generated output for subroutine:\r\n// " + lines[i] + " -----> hash: " + routineHash +
                "\r\n// hash of whole input: " + templateHash + "\r\n\r\n";            
            header += HEADER_START + (className != null ? className : "UnknownTemplate" + routineHash) + HEADER_END;
            output += header;

            len = lines.Length;
            for (; i < len; i++) {
                string line = lines[i];
                if (!line.Equals(EMPTY_STRING))
                    line = TAB2 + "// line: " + line + "\r\n";

                if (skipNextLine) {
                    skipNextLine = false;
                    output += line;
                    continue;
                }
                string lineResult = parseLine(ref lines, i);
                if (!lineResult.Equals(EMPTY_STRING)) {
                    output += lineResult + line;
                    propCounter++;
                } else {
                    output += line;
                }
            }

            output += FOOTER;

            Console.WriteLine("number of properties parsed: " + propCounter.ToString());

            return output;
        }
    }
}
