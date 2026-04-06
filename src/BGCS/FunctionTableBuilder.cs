namespace BGCS
{
    using BGCS.Metadata;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>FunctionTableBuilder</c>.
    /// </summary>
    public class FunctionTableBuilder
    {
        private readonly StringBuilder sb = new();
        private int index;

        private readonly List<CsFunctionTableEntry> entries = [];
        private readonly Dictionary<string, int> entryPointToIndex = [];

        /// <summary>
        /// Initializes a new instance of <see cref="FunctionTableBuilder"/>.
        /// </summary>
        public FunctionTableBuilder()
        {
        }

        /// <summary>
        /// Executes public operation <c>FunctionTableBuilder</c>.
        /// </summary>
        public FunctionTableBuilder(int funcTableableStart)
        {
            index = funcTableableStart;
        }

        /// <summary>
        /// Exposes public member <c>entries</c>.
        /// </summary>
        public List<CsFunctionTableEntry> Entries => entries;

        /// <summary>
        /// Executes public operation <c>Append</c>.
        /// </summary>
        public void Append(List<CsFunctionTableEntry> functionTableEntries)
        {
            foreach (var entry in functionTableEntries)
            {
                entryPointToIndex.Add(entry.EntryPoint, entry.Index);
                entries.Add(entry);
                sb.AppendLine($"funcTable.Load({entry.Index}, \"{entry.EntryPoint}\");");
                index = Math.Max(index, entry.Index + 1);
            }
        }

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public int Add(string name)
        {
            if (entryPointToIndex.TryGetValue(name, out var id))
            {
                return id;
            }

            id = index;
            entries.Add(new(id, name));
            entryPointToIndex.Add(name, id);
            sb.AppendLine($"funcTable.Load({id}, \"{name}\");");
            index++;
            return id;
        }

        /// <summary>
        /// Executes public operation <c>Finish</c>.
        /// </summary>
        public string Finish(out int count)
        {
            count = entries.Count;
            return sb.ToString();
        }
    }
}
