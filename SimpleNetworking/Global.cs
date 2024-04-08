namespace SimpleNetworking
{
    /// <summary>
    /// Valid protocol types for the SimpleNetworking library
    /// </summary>
    public enum Protocol
    {
        /// <summary>
        /// TCP protocol
        /// </summary>
        Tcp,
        /// <summary>
        /// UDP protocol
        /// </summary>
        Udp
    }

    /// <summary>
    /// Extension methods for the SimpleNetworking library wich I didnt know where else to put
    /// </summary>
    public static class Extension
    {
        /// <summary>
        /// Searches for a pattern in a byte array. Returns the index of the first occurence of the pattern.
        /// </summary>
        /// <param name="src">The array to search through</param>
        /// <param name="pattern">The pattern to search for</param>
        /// <returns>The index of the first occurence</returns>
        public static int Search(this byte[] src, byte[] pattern)
        {
            if (pattern.Length == 0)
                return 0;

            int[] prefixTable = ComputePrefixTable(pattern);

            int j = 0; // index for pattern
            for (int i = 0; i < src.Length; i++)
            {
                while (j > 0 && src[i] != pattern[j])
                {
                    j = prefixTable[j - 1];
                }

                if (src[i] == pattern[j])
                {
                    j++;
                }

                if (j == pattern.Length)
                {
                    return i - j + 1; // found the pattern at index (i - j + 1) in src
                }
            }

            return -1; // pattern not found in src
        }

        private static int[] ComputePrefixTable(byte[] pattern)
        {
            int[] prefixTable = new int[pattern.Length];
            int j = 0;

            for (int i = 1; i < pattern.Length; i++)
            {
                while (j > 0 && pattern[i] != pattern[j])
                {
                    j = prefixTable[j - 1];
                }

                if (pattern[i] == pattern[j])
                {
                    j++;
                }

                prefixTable[i] = j;
            }

            return prefixTable;
        }
    }
}
