using System.IO;
using System.Threading.Tasks;

using BGCS.Core.IO;

string destination = Path.GetFullPath(args[0]);
string acquired = Path.GetFullPath(args[1]);
string release = Path.GetFullPath(args[2]);
using var transaction = new OutputDirectoryTransaction(destination);
File.WriteAllText(Path.Combine(transaction.stagingPath, "value.txt"), "child");
File.WriteAllText(acquired, "owned");
while (!File.Exists(release))
    await Task.Delay(20);
transaction.Commit();
