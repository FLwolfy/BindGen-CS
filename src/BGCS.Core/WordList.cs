namespace BGCS.Core
{
    using System;
    using System.Buffers.Binary;
    using System.Diagnostics.CodeAnalysis;
    using System.IO.Compression;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>CharCaseInsensitiveEqualityComparer</c>.
    /// </summary>
    public class CharCaseInsensitiveEqualityComparer : IEqualityComparer<char>
    {
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public static readonly CharCaseInsensitiveEqualityComparer Default = new();

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public bool Equals(char x, char y)
        {
            return char.ToLower(x) == char.ToLower(y);
        }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public int GetHashCode([DisallowNull] char obj)
        {
            return char.ToLower(obj).GetHashCode();
        }
    }

    /// <summary>
    /// Defines the public class <c>WordList</c>.
    /// </summary>
    public class WordList
    {
        private readonly TrieStringSet words = new(CharCaseInsensitiveEqualityComparer.Default);

        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public static readonly WordList EN_EN = new("en_EN");

        /// <summary>
        /// Initializes a new instance of <see cref="WordList"/>.
        /// </summary>
        public WordList()
        {
        }

        /// <summary>
        /// Executes public operation <c>WordList</c>.
        /// </summary>
        public WordList(string countryCode)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BGCS.Core.Resources." + countryCode + ".txt");
            if (stream != null)
            {
                ReadTxt(stream);
            }
            else
            {
                ReadTxt(countryCode + ".txt");
            }
        }

        /// <summary>
        /// Executes public operation <c>CreateFromTxt</c>.
        /// </summary>
        public static unsafe void CreateFromTxt(string source, string destination)
        {
            var lines = File.ReadAllLines(source);
            var fs = File.Create(destination);
            fs.Position = 4;

            byte* buffer = (byte*)Marshal.AllocHGlobal(4096);
            int bufferSize = 4096;
            var span = new Span<byte>(buffer, bufferSize);

            DeflateStream stream = new(fs, CompressionLevel.Optimal, true);

            HashSet<string> words = new(lines.Length);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (words.Contains(line) || line.Length == 1)
                {
                    continue;
                }

                words.Add(line);

                int size = Encoding.Unicode.GetByteCount(line);
                if (size + 4 > bufferSize)
                {
                    bufferSize = (size + 4) * 2;
                    buffer = (byte*)Marshal.ReAllocHGlobal((nint)buffer, bufferSize);
                    span = new Span<byte>(buffer, bufferSize);
                }
                BinaryPrimitives.WriteInt32LittleEndian(span, size);
                var written = Encoding.Unicode.GetBytes(line, span[4..]);
                stream.Write(span[..(written + 4)]);
            }

            stream.Close();

            fs.Position = 0;
            BinaryPrimitives.WriteInt32LittleEndian(span, words.Count);
            fs.Write(span[..4]);

            Marshal.FreeHGlobal((nint)buffer);
            fs.Close();
        }

        /// <summary>
        /// Executes public operation <c>Read</c>.
        /// </summary>
        public unsafe void Read(string path)
        {
            var fs = File.OpenRead(path);

            byte* buffer = (byte*)Marshal.AllocHGlobal(4096);
            int bufferSize = 4096;
            var span = new Span<byte>(buffer, bufferSize);

            fs.Read(span[..4]);
            var count = BinaryPrimitives.ReadInt32LittleEndian(span);

            DeflateStream stream = new(fs, CompressionMode.Decompress);
            for (int i = 0; i < count; i++)
            {
                stream.Read(span[..4]);
                var size = BinaryPrimitives.ReadInt32LittleEndian(span);
                if (size + 4 > bufferSize)
                {
                    bufferSize = (size + 4) * 2;
                    buffer = (byte*)Marshal.ReAllocHGlobal((nint)buffer, bufferSize);
                    span = new Span<byte>(buffer, bufferSize);
                }
                stream.Read(span[..size]);
                var word = Encoding.Unicode.GetString(span[..size]);
                if (!words.Contains(word))
                {
                    words.Add(word);
                }
            }

            Marshal.FreeHGlobal((nint)buffer);

            stream.Close();
            fs.Close();
        }

        /// <summary>
        /// Executes public operation <c>ReadTxt</c>.
        /// </summary>
        public unsafe void ReadTxt(string path)
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!words.Contains(line))
                {
                    words.Add(line);
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>ReadTxt</c>.
        /// </summary>
        public unsafe void ReadTxt(Stream stream)
        {
            TextReader reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!words.Contains(line))
                {
                    words.Add(line);
                }
            }
        }

        /// <summary>
        /// Writes output for <c>Write</c>.
        /// </summary>
        public unsafe void Write(string path)
        {
            /*
            var fs = File.Create(path);

            byte* buffer = (byte*)Marshal.AllocHGlobal(4096);
            int bufferSize = 4096;
            var span = new Span<byte>(buffer, bufferSize);

            BinaryPrimitives.WriteInt32LittleEndian(span, words.Count);
            fs.Write(span[..4]);

            DeflateStream stream = new(fs, CompressionLevel.Optimal);

            foreach (var word in words)
            {
                var line = word.Key;
                int size = Encoding.Unicode.GetByteCount(line);
                if (size + 4 > bufferSize)
                {
                    bufferSize = (size + 4) * 2;
                    buffer = (byte*)Marshal.ReAllocHGlobal((nint)buffer, bufferSize);
                    span = new Span<byte>(buffer, bufferSize);
                }
                BinaryPrimitives.WriteInt32LittleEndian(span, size);
                var written = Encoding.Unicode.GetBytes(line, span[4..]);
                stream.Write(span[..(written + 4)]);
            }

            Marshal.FreeHGlobal((nint)buffer);
            stream.Close();
            fs.Close();*/
        }

        /// <summary>
        /// Writes output for <c>WriteTxt</c>.
        /// </summary>
        public unsafe void WriteTxt(string path)
        {
            /*
            var fs = File.Create(path);

            byte* buffer = (byte*)Marshal.AllocHGlobal(4096);
            int bufferSize = 4096;
            var span = new Span<byte>(buffer, bufferSize);

            foreach (var word in words)
            {
                var line = word.Key;
                int size = Encoding.Unicode.GetByteCount(line);
                if (size + 4 > bufferSize)
                {
                    bufferSize = (size + 4) * 2;
                    buffer = (byte*)Marshal.ReAllocHGlobal((nint)buffer, bufferSize);
                    span = new Span<byte>(buffer, bufferSize);
                }
                var written = Encoding.Unicode.GetBytes(line, span);
                written += Encoding.Unicode.GetBytes(Environment.NewLine, span[written..]);
                fs.Write(span[..written]);
            }

            Marshal.FreeHGlobal((nint)buffer);
            fs.Close();*/
        }

        /// <summary>
        /// Executes public operation <c>ReadFrom</c>.
        /// </summary>
        public static WordList ReadFrom(string path)
        {
            WordList list = new();
            list.Read(path);
            return list;
        }

        /// <summary>
        /// Executes public operation <c>ReadFromTxt</c>.
        /// </summary>
        public static WordList ReadFromTxt(string path)
        {
            WordList list = new();
            list.ReadTxt(path);

            return list;
        }

        /// <summary>
        /// Executes public operation <c>FindMostMatching</c>.
        /// </summary>
        public ReadOnlySpan<char> FindMostMatching(ReadOnlySpan<char> text)
        {
            return words.FindLargestMatch(text);
        }

        /// <summary>
        /// Executes public operation <c>SplitWords</c>.
        /// </summary>
        public string[] SplitWords(string text)
        {
            List<string> words = new();

            int index = 0;
            while (index < text.Length)
            {
                var word = this.words.FindLargestMatch(text.AsSpan(index));
                if (word.Length == 0)
                {
                    index++;
                    continue;
                }

                words.Add(word.ToString());
                index += word.Length;
            }

            return words.ToArray();
        }
    }

    /// <summary>
    /// Defines the public class <c>WordListCollection</c>.
    /// </summary>
    public class WordListCollection
    {
    }
}
