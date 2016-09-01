using System;
using System.IO;

namespace MBINRawTemplateParser
{
    class Program
    {
        static void Main(string[] args)
        {

#if DEBUG
            bool verbose = true;
            string inputFile = "testinput.c";
            if (args.Length > 0)
                inputFile = args[0];
#else
            if (args.Length == 0) {
                Console.WriteLine("bad input");
                return;
            }

            string inputFile = args[0];

            bool verbose = false;
            if (args.Length > 1)
                verbose = args[1].Equals("1");
#endif

            if (!File.Exists(inputFile)) {
                Console.WriteLine("file doesn't exists: " + inputFile);
                return;
            }

            Console.WriteLine("reading " + inputFile + "...");
            string[] input = null;
            try {
                input = File.ReadAllLines(inputFile);
            } catch (Exception ex) {
                Console.WriteLine("error reading file");
                Console.WriteLine(ex.Message);
                return;
            }

            if (input == null || input.Length == 0) {
                Console.WriteLine("cannot read file!");
                return;
            }

            Parser parser = new Parser(verbose);
            string output = parser.parse(input);

            string outputFile = inputFile + ".cs";
            Console.WriteLine("writing " + outputFile + "...");
            try {
                File.WriteAllText(outputFile, output);
            } catch (Exception ex) {
                Console.WriteLine("error writing file");
                Console.WriteLine(ex.Message);
                return;
            }

            Console.ReadLine();
        }
    }
}
