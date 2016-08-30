namespace MBINRawTemplateParser
{
    class FNV32
    {
        private static readonly uint fnvPrime = 0x811C9DC5;

        public static uint getHash(string str)
        {
            uint i, hash = 0;
            int len = str.Length;

            for (i = 0; i < len; i++) {
                hash *= fnvPrime;
                hash ^= ((byte)str[(int)i]);
            }

            return hash;
        }
    }
}
