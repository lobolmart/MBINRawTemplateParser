using System;
using System.Globalization;

namespace MBINRawTemplateParser
{
    enum LineType { NOP, NUMBER, STRING, CALL, BLOCK };

    class Parser
    {
        private static readonly string EMPTY_STRING = "";
        private static readonly string VAR_PREFIX = "Unknown";
        private static readonly string TAB = "\t";
        private static readonly string TAB2 = TAB + TAB;

        private static readonly string HEADER_START = "namespace MBINCompiler.Models.Structs\n{\n" +
            TAB + "public class ";
        private static readonly string HEADER_END = " : NMSTemplate\n" + TAB + "{\n";
        private static readonly string FOOTER = TAB + "}\n}\n";

        private static readonly string COMMENT_START = TAB2 + " // ";

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
                res = "\n" + TAB2 + "// missing " + diff.ToString() +
                    " bytes at offset " + lastOffset.ToString() + "\n" + TAB2 + "// ";
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
                res += "\n" + TAB2 + "[NMS(Size = 0x" + diff.ToString("X") + ", Ignore = true)]\n";
                res += TAB2 + "public byte[] Padding" + padOffset.ToString("X") + ";" +
                    TAB2 + "// offset: " + padOffset.ToString() + ", sz: " + diff + ", comment: auto padding \n\n";
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

        private LineType getLineType(string line)
        {
            if (isLineBlock(line))
                return LineType.BLOCK;

            if (!hasStr(line, "a1") && !hasStr(line, "v1")) // not acting on the buffer?
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
                        res += COMMENT_START + "offset: " + offset + ", sz: 4, origin: " + valueStrOrigin + ", parsed: " + valueStr + packedComment + "(1)" + "\n";

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
                res += TAB2 + "[NMS(Size = " + sz + ")]\n";
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

            int idxPlus = line.IndexOf("+ ") + 2;
            string offset = line.Substring(idxPlus, line.LastIndexOf(");") - idxPlus);

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
                idxPlus = nextLine.IndexOf("+ ") + 2;
                string offsetNext = nextLine.Substring(idxPlus, nextLine.LastIndexOf(")") - idxPlus);

                int offsetIntNext;
                if (offsetNext.IndexOf("0x") > -1)
                    parsedValue = int.TryParse(offsetNext.Substring(2), NumberStyles.AllowHexSpecifier, null, out offsetIntNext);
                else
                    parsedValue = int.TryParse(offsetNext, out offsetIntNext);

                sz = offsetIntNext - offsetInt;
                szStr = sz.ToString();
                szStrComment = szStr;
            }

            res += updateOffsetDiff(offsetInt, sz);

            string lineTrim = line.TrimStart(' ');
            res += "\n" + TAB2 + "// call to subroutine: " + lineTrim.Substring(0, lineTrim.IndexOf("(")) + "\n";
            res += TAB2 + "// filling with bytes as we don't have a way to expand it for now\n";
            res += TAB2 + "[NMS(Size = 0x" + sz.ToString("X") + ", Ignore = false)]\n";
            res += TAB2 + "public byte[] Subroutine" + offsetHex + ";";
            res += COMMENT_START + "offset: " + offset + ", sz: " + szStrComment + ", origin: " + lineTrim + "\n";

            return res;
        }

        private string parseBlock(string line)
        {
            string res = EMPTY_STRING;
            res = "\n" + COMMENT_START + "comment: 'do/while' loop start/end detected, origin: " + line;
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

            string templateHash = FNV32.getHash(string.Join("\n", lines)).ToString("X");
            string routineHash = FNV32.getHash(lines[0]).ToString("X");
            string header = "// generated output for subroutine:\n// " + lines[0] + " -----> hash: " + routineHash +
                "\n// hash of whole input: " + templateHash + "\n\n";
            header += HEADER_START + "UnknownTemplate" + routineHash + HEADER_END;
            output += header;

            int i, len = lines.Length, propCounter = 0;
            for (i = 1; i < len; i++) {
                string line = lines[i];
                if (!line.Equals(EMPTY_STRING))
                    line = TAB2 + " // line: " + line + "\n";

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

            if (output.Equals(EMPTY_STRING))
                Console.WriteLine("something went wrong - no output produced!");

            return output;
        }
    }
}
